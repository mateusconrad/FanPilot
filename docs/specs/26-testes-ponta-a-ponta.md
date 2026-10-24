# Testes de ponta a ponta do painel e do executável

- **Item:** 26
- **Onda:** 2 · Entrega
- **Tipo:** infra
- **Esforço:** M
- **Depende de:** 08, 11

## Problema

Os testes do item 08 cobrem as regras e a API, mas não o que o usuário vê. O painel (`wwwroot/app.js`, com o editor de curvas por arraste, o SSE e o pareamento) só é testado à mão, no `tools/ui-mock`. E nada confere o executável empacotado (o zip da release) antes de publicar.

## Objetivo

Cada push verifica automaticamente, num navegador de verdade, os fluxos principais do painel, e confirma que o executável da release sobe e responde.

## Solução

1. **Playwright** (Node) em `tests/e2e`, rodando contra o FanPilot de verdade com a placa falsa (`--fake-board`, item 08) numa porta livre. Não roda contra o mock: assim o backend é testado junto.
   - Cenários da placa falsa equivalentes aos do mock: demo, pior caso (8 fans, nomes longos, sensores desconectados), sem controle, serviço fora do ar, Turbo.
2. **Fluxos cobertos:**
   - pareamento: `/pair` no PC mostra o QR; o "celular" (outro contexto do navegador, acessando pelo IP do próprio runner, que não conta como loopback) vê a tela de bloqueio, abre `/auth?c=<código>` e chega ao painel; código usado duas vezes é recusado;
   - painel: cards, gráficos, lista de fans, tabela de sensores e estados de erro;
   - editor: abrir, arrastar um ponto (com as restrições), escolher uma predefinição, aplicar (confere o corpo enviado e o estado atualizado), restaurar; Turbo liga e desliga;
   - GPU: fixo e automático;
   - SSE: o painel se reconecta depois de a conexão cair;
   - acessibilidade: axe-core sem violações sérias nas telas principais.
3. Larguras de 320, 390 e 1024 px, nos temas claro e escuro. Os screenshots ficam como artefato da CI. A comparação visual com tolerância fica como segunda etapa, opcional.
4. **Smoke do executável:** a CI extrai o zip da release e roda o `FanPilot.exe` no runner (sem GCC e sem PawnIO, então com os avisos de placa não suportada). Confere `/`, `/pair`, `/api/state`, `--version`, e que o `--diagnose` termina sem alterar nada.
5. Roda na CI (item 11) a cada push, com tempo alvo abaixo de 5 min.

## Fora de escopo

- Testes em celulares reais: continuam manuais, registrados na issue do item que muda a interface.

## Critérios de aceite

- [ ] Quebrar de propósito uma restrição do arraste no editor faz um teste de ponta a ponta falhar.
- [ ] A execução da CI mostra o relatório do Playwright e os screenshots como artefatos.
- [ ] O smoke do zip passa na CI e falha se o exe não subir.

## Testes

- É o próprio item.
