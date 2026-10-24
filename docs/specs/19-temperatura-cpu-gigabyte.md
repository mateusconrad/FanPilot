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

## Critérios de aceite

- [ ] B760M com PawnIO: o card mostra a temperatura de pacote, próxima da do HWiNFO.
- [ ] Sem PawnIO: comportamento atual, com o rótulo "socket" e a dica.
- [ ] Caminho LHM: nenhuma mudança visível.
- [ ] O GCC continua funcionando normalmente com as duas leituras ativas (roteiro de hardware).

## Testes

- Automatizados: atribuição do `role` com fontes falsas; painel usando o `role` (teste JS).
- Manuais: B760M com e sem PawnIO.
