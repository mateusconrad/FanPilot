# Notebook Avell G1513 (Uniwill/Tongfang): o que o app da Avell faz com o fan

Estudo de 03/11/2026 sobre o pacote `08_Retroiluminacao` da Avell (na verdade um `.rar` com
`MyAPP_1.4.4.0_STD.exe`, instalador Inno Setup 5.5.7). Máquina-alvo: Avell G1513 MXTI, i7-7700HQ, GTX 1050 Ti,
16 GB. O protocolo abaixo vem do instalador.

**No notebook (03/11/2026):** o FanPilot controla o fan e lê as temperaturas. **Pendente:** o RPM (o painel mostra
0) e a calibração dos níveis, que dependem do relatório da sonda.

## Como foi extraído

O Inno guarda os arquivos num único bloco LZMA (`zlb\x1a` + 5 bytes de propriedades), que o `lzma` do Python abre
em modo raw. Os executáveis passam pelo filtro de CALL/JMP do Inno 5.2+ (`TransformCallInstructions`, blocos de
64 KiB, endereço só nas instruções E8/E9 cujo byte alto é 00/FF); desfeito o filtro, os metadados .NET ficam
íntegros. O IL foi lido com um dump próprio sobre `System.Reflection.Metadata`.

## O que tem no pacote

| Arquivo | Papel |
|---|---|
| `MyAPP.exe`, `MyAPPTray.exe` (.NET 4.5, WPF) | menu e bandeja; leem os bytes de suporte/estado do EC |
| `MyFanBoost.exe` (.NET 4.5) | **o controle do fan**: modo normal, boost ou 5 níveis fixos |
| `MyColor.exe`, `MyColor2.exe`, `DefaultColor.exe` + `GRGBKB_D.dll` ("TF_GRGB") | iluminação RGB do teclado |
| `MyEC.dll`, `APPServ.exe`, `LaunchCtrl.exe`, `inpout32/x64.sys` | acesso cru a portas (EC); o fan **não** passa por aqui |
| `OSDAPI.dll`, `acpimof.dll` | OSD das teclas Fn; MOF da interface WMI |

## A interface do fan: WMI, sem driver

`MyFanBoost.WMIEC` usa só `System.Management`:

- Objeto: `root\WMI`, `AcpiTest_MULong.InstanceName='ACPI\PNP0C14\1_1'`, método `GetSetULong(Data) -> Return`.
- **Leitura** de um byte da RAM do EC: `Data = 2^40 + endereço`; o valor vem no byte baixo de `Return`.
- **Escrita**: `Data = (valor << 16) + endereço`.
- O app espera 200 ms antes de cada chamada (`Thread.Sleep(200)`).
- Eventos: `SELECT * FROM AcpiTest_EventULong` (teclas Fn).

Registradores (`ECSpec`, igual nos três executáveis):

| Endereço | Nome no app | Uso |
|---|---|---|
| `0x0751` | `ADDR_MAFAN_CONTROL_BYTE` | `0x00` normal (o EC decide), `0x40` boost (máximo), `0x80 + n` nível n = 1..5 |
| `0x0765` | `ADDR_SUPPORT_BYTE1` | bit 7 `FanBoost` suportado |
| `0x0766` | `ADDR_SUPPORT_BYTE2` | bit 0 modo silencioso, bit 2 teclado RGB |
| `0x073C` | `ADDR_SUPPORT_BYTE3` | teclado: zona única / 4 zonas |
| `0x0767` / `0x075D` | `ADDR_TRIGGER_BYTE(2)` | gatilhos (bit 2 FanBoost) |
| `0x0768` | `ADDR_STAUTS_BYTE` | estado (bit 2 FanBoost ligado) |
| `0x0476` | `BIOSFuncReg` | |
| `0x0490` | `ecPowSource` | tomada/bateria |
| `0x04A2`, `0x04AB` | bateria | temperatura, carga |

Comportamento do `MyFanBoost` (`MainWindow..ctor`, `SetFanMode`, `SetFanLevel`): grava o modo e o nível no registro
(`HKLM\SOFTWARE\OEM\MyApp\MyFanBoost`, `Mode`/`Level`), reaplica no boot e a cada `PowerModeChanged` (volta da
suspensão), e reaplica de novo se, 3 s depois, `0x0751` estiver em 0. Ou seja: **o EC esquece o modo** em suspensão e
partida, e quem controla precisa reescrever.

O app **não lê RPM nem temperatura** do EC. Os candidatos abaixo vêm de outros Uniwill e precisam ser confirmados
na máquina: `0x043E` temp. CPU, `0x044F` temp. GPU, `0x0464/65` e `0x046C/6D` RPM dos fans, `0x1804`/`0x1809` duty
manual (este último par só existe em gerações mais novas; não escrever sem confirmar).

## Como o FanPilot usa isso (`src/UniwillEc.cs`)

- Sem o chip de sensores (Super I/O) que o LibreHardwareMonitor procura, o `LhmBoard` procura a interface WMI. Ela só
  entra se o bit 7 de `0x0765` disser que o EC tem o controle do fan, como o app da Avell confere.
- O fan entra como mais um fan do caminho LibreHardwareMonitor (`Notebook fan`, chave `uniwill/ec/0751`). As
  temperaturas da CPU continuam vindo do LibreHardwareMonitor (com o PawnIO) e a da GPU da NVML.
- O `SoftwareCurveEngine` calcula o duty como em qualquer placa. O `UniwillFanOutput` transforma a porcentagem em um
  de 6 degraus iguais (níveis 1-5 e boost), arredondando para cima, para nunca resfriar menos que a curva pede. O
  painel mostra o topo do degrau (16,7%, 33,3%… 100%).
- A cada segundo, `0x0751` é lido e reescrito se mudou (suspensão, o app da Avell). "BIOS", saída e falhas
  escrevem `0x00`. Ao abrir, o FanPilot também escreve `0x00`, como faz com as placas.
- Se o MyFanBoost/MyAPP estiver aberto, o aviso do painel pede para fechá-lo.
- Ainda sem RPM: o teste de hardware (`--hardware-test`) vai reclamar que a rotação não mudou até o endereço do RPM
  ser achado pela sonda.

A **iluminação do teclado** (`GRGBKB_D.dll`) está fora do escopo do FanPilot.

A divisão em 6 degraus iguais é um palpite. Com a sonda, troca-se pelo RPM medido em cada nível.

## Próximo passo: a sonda no notebook

`tools\uniwill-probe.ps1`, num PowerShell como Administrador no notebook (o app da Avell fechado):

```powershell
powershell -ExecutionPolicy Bypass -File .\uniwill-probe.ps1              # só leitura (~1 min; ponha carga no meio)
powershell -ExecutionPolicy Bypass -File .\uniwill-probe.ps1 -TestWrite   # + boost e níveis 1/3/5, depois volta ao original
```

O `.txt` que sai responde: se `AcpiTest_MULong` existe nesse modelo; quais bytes de `0x0400-0x04FF` acompanham
temperatura e RPM; se os níveis de `0x0751` mudam o RPM e se o EC mantém o valor escrito.
