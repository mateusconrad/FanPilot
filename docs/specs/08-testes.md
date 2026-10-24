# Testes automatizados: unitários e de integração

- **Item:** 08
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** infra
- **Esforço:** M
- **Depende de:** 09

## Problema

Não existe nenhum teste automatizado. As regras que protegem o hardware (validação das curvas, piso de 20%, 100% obrigatório, Turbo, watchdog) e as de segurança (pareamento, sessões) só são conferidas à mão, e qualquer refatoração das próximas ondas pode quebrá-las sem aviso.

## Objetivo

Cobrir com testes rápidos tudo que é regra de segurança, de persistência ou de cálculo de curva, sem hardware e sem Administrador.

## Solução

1. **Projeto `tests/FanPilot.Tests`** (xUnit, `net472`; o esqueleto nasce no item 09), com `InternalsVisibleTo` no projeto principal.
2. **`FakeBoard : IBoard`** configurável: headers, sensores, ordem, falhas injetadas e leitura "com perda" (para o watchdog). Em builds Debug, `--fake-board` liga o FanPilot inteiro com ela, para desenvolver sem hardware.
3. **Relógio injetável** (`Func<DateTime>`) no `AuthManager`, no `Log` e em tudo que depende de tempo.
4. **Cobertura mínima:**
   - `FanManager`: número de pontos, primeiro ponto em 0 °C, temperaturas crescentes, temperatura máxima, piso, curva que nunca desce, último ponto em 255, modo fixo com o ponto de segurança, sensor de referência inválido.
   - `FanCurve.PwmAt` e `Signature`.
   - **Paridade C# × JS:** `curveAt`, `resample` e `normalize` saem do `app.js` para `wwwroot/js/curve.mjs` (módulo ES; `.mjs` entra na tabela de tipos do `WebServer`). Uma fixture compartilhada (`tests/fixtures/curves.json`) é conferida pelo xUnit e pelo `node --test`.
   - Turbo: ligar e desligar devolve o estado anterior (inclusive "sem gerenciamento") e sobrevive a reinício.
   - Watchdog: reaplica quando a assinatura muda e não entra em loop com leitura com perda.
   - `AuthManager`: código de uso único, expiração, bloqueio por IP, validade da sessão, revogação.
   - Regressão do item 01 (`HostGuard`), que vem antes desta infraestrutura. Do item 03 em diante, cada item traz os próprios testes.
5. **Smoke de integração:** sobe o `WebServer` com a `FakeBoard` numa porta livre e confere as rotas, `401` sem sessão, `421` com `Host` estranho e o SSE.
6. **Convenção para os próximos itens:** todo item entrega testes automatizados do que muda, e o PR não fecha sem eles (ver "Como cada item é entregue" no `docs/ROADMAP.md`).

## Fora de escopo

- Meta de porcentagem de cobertura: o critério é cobrir as regras listadas.
- Testes de interface no navegador e smoke do executável: item 26.
- Teste automatizado na placa real: item 27.

## Critérios de aceite

- [ ] `dotnet test` e `node --test` passam em menos de 30 s, sem hardware.
- [ ] Remover o piso de 20% ou o ponto de segurança do modo fixo faz pelo menos um teste falhar.
- [ ] A fixture de curvas falha se `PwmAt` e `curveAt` divergirem.

## Testes

- É o próprio item. A CI (item 11) passa a rodar tudo em cada PR.
