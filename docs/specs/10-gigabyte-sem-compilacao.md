# Ponte Gigabyte sem dependência de compilação

- **Item:** 10
- **Onda:** 2 · Entrega
- **Tipo:** infra
- **Esforço:** M
- **Depende de:** 09

## Problema

- O exe só tem suporte à Gigabyte se for compilado num PC com o GCC instalado (`build.ps1:74-83`; README: "Para distribuir, gere o zip num PC com GCC"). Isso impede gerar releases na CI.
- Uma atualização do GCC pode mudar a API interna e quebrar o caminho Gigabyte (README, seção Compatibilidade). Hoje isso aparece como exceção em tempo de execução, sem um diagnóstico claro.

## Objetivo

Um único build, gerado em qualquer máquina, que atende tanto a Gigabyte quanto as outras placas e reconhece com clareza um GCC incompatível.

## Solução

1. **`GigabyteApi`**, uma camada fina que acessa os tipos do GCC por reflection a partir de `Program.GccLibDir` (o resolvedor atual continua). Ela cobre exatamente o que `GigabyteBridge` usa hoje:
   - `HardwareMonitorControlModule`: construtor, `GetCurrentMonitoredData(ref HardwareMonitoredDataCollection)` e `Dispose`;
   - `HardwareMonitoredData`: `Type`, `Title`, `DeviceUUID`, `Value` e `Unit`;
   - `SmartGuardianFanControlCoordinateSystemModule`: construtor, `IsSupported`, `HardwareInfo` (`FanControlCount`, `TemperatureInputCount`), `GetSensorTitle(SensorTypes, int, out string)`, `GetHardwareMonitorDatas(ref List<float>, ref List<float>)`, `Get(int, ref SmartFanDataPointSet)`, `Set(int, SmartFanDataPointSet)`, `GetTemperatureInputSelection(int, out int)`, `SetTemperatureInputSelection(int, int)` e `Dispose`;
   - `SmartFanDataPointSet` (`Mode`, `DeltaTemperature`, `FanSpeed`, `DataPoints`), `SmartFanDataPoint` (`X`, `Y`) e os enums `FanConfigMode` e `SensorTypes`.

   Os `MethodInfo`/`PropertyInfo` são resolvidos uma vez e guardados: a 1 Hz, o custo é desprezível.
2. **Checagem de contrato na inicialização:** todos os tipos e membros são resolvidos com as assinaturas esperadas antes de criar a ponte. Se faltar algo:
   - com `backend: "auto"` → usa o LibreHardwareMonitor e mostra no painel "Gigabyte Control Center <versão> não é compatível com este FanPilot (falta <membro>)";
   - com `backend: "gigabyte"` → erro claro e saída.
3. **Versão do GCC** (`FileVersionInfo` de `Gigabyte.Engine.EnvironmentControl.dll`) no log e no `--diagnose`.
4. **Comportamento idêntico** ao atual: chamadas serializadas, nova tentativa na primeira chamada IPC, chamada de aquecimento.
5. **Remover** o `NO_GIGABYTE`, a exclusão condicional de arquivos e as referências de build ao GCC (`build.ps1` e item 09).
6. Continua sem redistribuir nenhuma DLL da Gigabyte.

## Fora de escopo

- Timeout das chamadas IPC (é das DLLs do GCC o canal). Fica registrado como risco conhecido.

## Critérios de aceite

- [ ] Um build feito numa máquina sem GCC gera um exe que, na B760M com GCC, passa no roteiro de verificação em hardware como o build atual.
- [ ] Com um assembly de teste em que um membro foi renomeado, a checagem de contrato falha com mensagem clara e, no modo `auto`, cai para o LHM.
- [ ] A versão do GCC aparece no log e no `--diagnose`.

## Testes

- Automatizados: assemblies stub com os mesmos nomes de tipos e membros, compilados só no projeto de testes (nunca distribuídos) e carregados pela `GigabyteApi` a partir de uma pasta de teste. Um stub "quebrado" testa a checagem de contrato.
- Manuais: roteiro de verificação em hardware na B760M.
