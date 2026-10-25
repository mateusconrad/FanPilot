# Temperatura dos núcleos da CPU na placa Gigabyte

- **Item:** 19
- **Onda:** 3 · Produto
- **Tipo:** produto
- **Esforço:** P–M
- **Depende de:** —

## Problema

Na Gigabyte, o card "CPU" mostra o sensor do socket na placa-mãe, e não a temperatura dos núcleos (README, observações da B760M). O valor costuma ficar bem abaixo do que HWiNFO, Gerenciador de Tarefas e jogos mostram, e o usuário conclui que o FanPilot está errado. O painel acha esse sensor pelo título "CPU" (`wwwroot/app.js:79`), e o histórico usa um id fixo da Gigabyte como alternativa (`wwwroot/app.js:121`).

## Objetivo

O card e o gráfico mostram a temperatura que o usuário espera, nos dois caminhos.

## Solução

1. **Caminho Gigabyte, com o PawnIO instalado:** uma instância do LibreHardwareMonitor só com a CPU (`IsCpuEnabled`, sem placa-mãe nem Super I/O, para não disputar o chip com o GCC) fornece a temperatura de pacote.
2. **Papel do sensor no snapshot:** cada leitura pode ter um `role`. O `cpu-main` vai para a temperatura de pacote quando ela existe e, senão, para o sensor do socket. Card, gráfico e histórico usam o `role`, e não o título.
3. **Rótulos claros:** "CPU · pacote" ou "CPU · socket"; a tabela de sensores mostra os dois.
4. **Curvas na Gigabyte** continuam nos sensores do chip (limite do hardware). O editor explica: "a curva segue o sensor da placa; o card mostra a temperatura dos núcleos".
5. **Sem PawnIO:** o card mostra "socket" e uma dica para instalar o PawnIO e ver a temperatura dos núcleos.

### Como ficou (25/10/2026)

- `src/CpuTemperature.cs`: `CpuPackageMonitor` (o LibreHardwareMonitor só com a CPU, que o `GigabyteBridge` abre e fecha junto com ele) e `CpuRoles.Assign`, que marca as leituras de cada placa: com o pacote, ele entra como "CPU · pacote" (`cpu-main`) e o sensor do GCC vira "CPU · socket" (`cpu-socket`); sem o pacote, o sensor do GCC é o `cpu-main`, com o título "CPU · socket" e a dica; no LibreHardwareMonitor, o "CPU" já é o pacote e não muda. `SensorReading` ganhou `Role` e `Hint`, e o snapshot os leva.
- O painel acha a CPU pelo `role` (`wwwroot/js/sensors.mjs`, testado com `node --test`); o histórico ganhou a série `cpu-main`; os alertas (item 16) e a trava de 80 °C do `--hardware-test` também usam o `cpu-main` (com o pacote, a trava fica mais conservadora: os núcleos esquentam mais que o socket).
- O editor, numa placa com curva no chip e com o pacote, explica que a curva segue o sensor da placa.
- A placa falsa `demo` tem o pacote (14 °C acima do socket); a `worst`, não, com a dica; a `lhm` mostra "CPU" como antes.

## Critérios de aceite

- [ ] B760M com PawnIO: o card mostra a temperatura de pacote, próxima da do HWiNFO (verificação em hardware).
- [x] Sem PawnIO: comportamento atual, com o rótulo "socket" e a dica.
- [x] Caminho LHM: nenhuma mudança visível.
- [ ] O GCC continua funcionando normalmente com as duas leituras ativas (roteiro de hardware).

## Testes

- Automatizados: atribuição do `role` com fontes falsas (`CpuRolesTests`, `CpuSnapshotTests`, o `GigabyteBridge` com o stub do GCC e um monitor de CPU sem leitura), a série `cpu-main` no histórico, e o painel usando o `role` (`tests/js/sensors.test.mjs`); de ponta a ponta, o card nas placas falsas `demo`, `worst` e `lhm`.
- Manuais: B760M com e sem PawnIO.
