# Como o Gigabyte Control Center monitora temperatura e controla os fans

Análise feita com REA (metadados/IL dos assemblies .NET), dumps de API *reflection-only* e decompilação
pontual (ILSpy), confirmada em runtime com probes em `tools/`. Máquina: **B760M GAMING X AX**, i5-13400,
GCC com `EasyTuneEngineService`.

## Arquitetura

```
GCC.exe (UI, roda como usuário/admin)
  └─ Lib\MBEasyTune\Gigabyte.Engine.EnvironmentControl.dll   ← "client SDK" (módulos *Module)
        │  .NET Remoting, IpcChannel
        ▼  ipc://GigabyteEngine/EnvironmentControl/<Objeto>
EasyTuneEngineService.exe  (serviço Windows, SYSTEM, C:\Program Files (x86)\GIGABYTE\EasyTuneEngineService)
  └─ Gigabyte.EnvironmentControl.Remoting.dll   (wrappers MarshalByRefObject publicados)
  └─ Gigabyte.EnvironmentControl.dll            (lógica: SmartGuardianFanControlCoordinateSystem, HardwareMonitorControl…)
  └─ Gigabyte.EnvironmentControl.LowPinCount.dll → Super I/O **ITE IT8689E** (portas LPC 2E/2F ou 4E/4F)
```

### Canal IPC (evidência: strings de `EasyTuneEngineService.exe` e `Gigabyte.Engine.dll`)

- Porta IPC: `GigabyteEngine`; URL: `ipc://{porta}/{grupo}/{objeto}` (formato `ipc://{0}/{1}/{2}`).
- Objetos publicados em `EnvironmentControl/…`: `FanControl`, `SmartGuardianFanControl`, `FanSwitchControl`,
  `SmartFanEmbeddedControlSwitch`, `HardwareMonitor`, `SystemInfo`, `PowerSupplyManagement`,
  `AutoFanStopManagement`, `HighAmperageFanHeaderManagement`, `SmartGuardianFanControlCoordinateSystem`.
- Segurança (versão recente do serviço): ACL **só Administradores** (`authorizedGroup`), `TypeFilterLevel.Low`,
  e um `SecureSerializationBinder` com allowlist + alertas no Event Log. Confirmado: processo não elevado recebe
  `Falha ao se conectar a uma porta de IPC: Acesso negado`.
- Peculiaridade: a **primeira chamada** em uma conexão nova falha com "O pipe está sendo fechado"; um retry funciona.

## Monitoramento

`HardwareMonitorControlModule.GetCurrentMonitoredData(ref HardwareMonitoredDataCollection)` devolve itens
`{Type, Title, DeviceUUID, Value, Unit}`. Nesta placa (23 itens):

| Tipo | UUIDs | Exemplos |
|---|---|---|
| Temperature | 512–517 | System 1, PCH, **CPU**, PCIEX16, VRM MOS, System 2 |
| Fan | 768–771 | CPU, System 1, System 2, System 3 (RPM) |
| Voltage | 0–5 | VCore, +3.3V, +12V, +5V, VAXG, VCCIN AUX |
| Frequency | 256–259 | CPU, BCLK, multiplicador, DDR |
| Power | 1280–1283 | CPU Package, CPU Cores, CPU Graphics |

A GPU **não** passa por aqui (o GCC usa outro serviço para placas Gigabyte). O FanPilot usa **NVML** (`nvml.dll`).

## Controle de fans (caminho de "3ª geração", usado nesta placa)

O GCC escolhe a geração pelo *group number* da placa (`0x90040606` aqui). Para a 3ª geração,
`Gigabyte.SIV.UnifiedManagement.ControlFanSpeed3rdGenerationManagement` usa
**`SmartGuardianFanControlCoordinateSystemModule`** (os módulos `SmartGuardianFanControlModule`/`FanControlModule`
respondem *não suportado* nesta placa):

- `Get(i, ref SmartFanDataPointSet)` / `Set(i, SmartFanDataPointSet)` — curva do header *i*.
- `GetTemperatureInputSelection` / `SetTemperatureInputSelection` — sensor que comanda o header.
- `GetHardwareMonitorDatas(ref temps, ref rpms)` — leituras alinhadas por índice.

`SmartFanDataPointSet` = `{Mode, DeltaTemperature, FanSpeed, DataPoints[(X=°C, Y=PWM 0..255)]}` — é exatamente o
formato salvo em `C:\Program Files\GIGABYTE\Control Center\Fan\Profile-0.xml`.

No serviço: `SmartGuardianFanControlCoordinateSystem.Set` → `FanIT86xxCoordinateSystemRegulator.Set4IT8689` →
`SmartGuardianFanConfigConverter.To(...)` → `IT8689XXBase.SetFanSmartGuardianConfig`, ou seja, a curva vira os
parâmetros nativos **SmartGuardian** do chip ITE e **é executada pelo hardware** (continua valendo sem software).

### Regras da conversão (de `SmartGuardianFanConfigConverter.ToPhysicalLayerFormat`)

| Ponto | Vira |
|---|---|
| `P[0].X` | temperatura de desligamento do fan (`TemperatureLimitValueOfFanOff`) |
| `P[1]` | temperatura de partida + PWM de partida (`…FanStart`, `StartPWM`) |
| `P[2..n-2]` | "extra vectors" (início, PWM e inclinação de cada segmento) |
| `P[n-1]` | temperatura de **full speed**; **Y precisa ser 255** |

- Menos de 3 pontos, ou último Y ≠ 255 → a conversão aborta **silenciosamente** e o serviço grava uma config
  zerada (que o chip trata como velocidade máxima). Foi o que fez o primeiro teste de escrita "não funcionar".
- `SmartFanMode` = interpolação linear; `StairStepMode` = degraus; `RpmFixedMode` tem conversão própria.

### Validação em hardware (`tools/Probe4.cs`)

System 1 com curva 40% até 70 °C / 100% em 85 °C: 2352 → 1573 RPM em 8 s; restaurado → voltou a subir.

## Arquivos de configuração do GCC

- `Control Center\Fan\Profile-0.xml` — perfis de curva (`ArrayOfSmartFanDataPointConfig`).
- `Control Center\Fan\setupmode.xml` — modo global (`FullSpeed` nesta máquina, por isso todas as curvas lidas estão em 255).
- `Control Center\Fan\location.xml` — posições físicas dos fans (não usado).
