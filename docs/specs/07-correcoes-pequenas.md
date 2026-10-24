# Correções pequenas: mensagem ao aplicar, corrida do Turbo, pareamento

- **Item:** 07
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** bug
- **Esforço:** P
- **Depende de:** —

## Problemas e correções

1. **Mensagem errada ao aplicar uma curva.** O aviso "Aplicado no chip da placa-mãe" (`wwwroot/app.js:606`) aparece também nas placas em que quem executa a curva é o FanPilot. → Texto conforme `hardwareCurves()`: "Aplicado no chip da placa-mãe" ou "Aplicado: o FanPilot está controlando este fan".
2. **Corrida entre o Turbo e os ajustes.** `SetTurbo` confere o estado fora de qualquer trava (`src/FanManager.cs:55`), e `Apply` confere o Turbo antes de aplicar, sem exclusão mútua (`src/FanManager.cs:177-181`). Dois toques rápidos no Turbo, ou um ajuste chegando junto, podem gravar um "estado anterior" errado ou aplicar uma curva no meio do Turbo. → Uma trava de operação (`_opLock`) serializa tudo que altera fans: `SetTurbo`, `Apply`, `Restore`, reaplicação e watchdog. A trava curta `_sync` continua protegendo só os dicionários, para nunca ficar presa durante uma chamada IPC.
3. **Mapa de tentativas de pareamento cresce sem limite.** `_failures` (`src/Auth.cs:31`) nunca remove IPs. → Remover as entradas vazias ao limpar a janela de 1 min.
4. **Erros silenciosos na página de pareamento.** "Desparear" e "Desparear todos" não tratam falha (`wwwroot/pair.js:48`, `:65-69`). → Mostrar a mensagem de erro na página.

## Critérios de aceite

- [ ] Placa LHM: aplicar uma curva mostra a mensagem do FanPilot. Placa Gigabyte: a do chip.
- [ ] 10 chamadas simultâneas alternando o Turbo terminam num estado consistente, e Turbo desligado devolve exatamente as configurações de antes.
- [ ] Um ajuste individual enviado durante a ativação do Turbo é recusado ou aplicado antes dela, nunca no meio.
- [ ] Um erro ao desparear aparece na página `/pair`.

## Testes

- Automatizados: corrida do Turbo com placa falsa e chamadas concorrentes.
- Manuais: mensagens no painel (mock e B760M).
