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
   - O navegador é o **Edge** que já vem no Windows (`channel: 'msedge'`), no PC e no runner: nada de navegador para baixar.
   - O harness (`tests/e2e/fanpilot.mjs`) copia o build de Debug para uma pasta temporária, escreve o `config.json` com a porta, sobe o exe e espera o `/api/info` responder 200. Um FanPilot por vez (é instância única), e um de cada cenário.
   - **Cenários** (`--fake-board=<cenário>`, `FakeBoard.Scenarios`), os mesmos do mock: `demo` (a B760M), `worst` (8 fans, nomes longos, sensor desconectado, GPU de nome longo), `unsupported` (sem controle de fans nem GPU), `down` (serviço da placa fora do ar), `lhm` (curvas executadas pelo FanPilot) e `slow` (a placa demora 4 s para abrir, como o GCC). O Turbo é ligado pelo próprio teste. Uma configuração órfã e um arquivo de estado danificado são criados de verdade na `data-fake\` antes de o FanPilot subir.
   - A placa falsa roda **sem Administrador**, só em `localhost` (o prefixo `+`, que deixa os celulares entrarem, exige elevação). O harness sobe o exe com `__COMPAT_LAYER=RunAsInvoker`, que faz o Windows ignorar o pedido de elevação do manifesto; elevado, nada muda.
2. **Fluxos cobertos:**
   - pareamento: `/pair` no PC mostra o QR; o "celular" (outro contexto do navegador, acessando pelo IP do próprio PC, que não conta como loopback) vê a tela de bloqueio, recebe 401 na API e 403 no `/pair`, abre `/auth?c=<código>` e chega ao painel; o mesmo código num segundo aparelho é recusado; o PC desparea e o celular volta à tela de bloqueio. Essa parte precisa de Administrador: roda na CI e é pulada num PC sem elevação;
   - painel: cards, gráficos, lista de fans, tabela de sensores, rodapé com a versão e estados de erro (serviço fora, sem controle, sem GPU, avisos, configuração órfã descartada pelo botão);
   - editor: abrir, arrastar pontos contra cada restrição (vizinhos, mínimo de 20%, primeiro ponto em 0 °C, último em 100% até 90 °C), escolher uma predefinição, aplicar (confere o corpo enviado e o estado devolvido pela API), fixo, restaurar, fechar sem aplicar; Turbo liga, bloqueia o editor e desliga;
   - GPU: fixo e automático;
   - SSE: o painel se reconecta depois de o FanPilot reiniciar (pelo cenário `slow`, a reconexão encontra o servidor ainda iniciando e recebe 503) e o painel recusado por excesso de conexões entra quando uma vaga abre;
   - acessibilidade: axe-core sem violações sérias ou críticas no painel, no editor e no pareamento, nos dois temas.
3. Larguras de 320, 390 e 1024 px, nos temas claro e escuro, nos cenários `demo` e `worst`: os screenshots do painel e do editor ficam em `tests/e2e/screens` e no relatório, os dois como artefatos da CI, e nenhuma largura pode ter rolagem horizontal. A comparação visual com tolerância fica como segunda etapa, opcional.
4. **Smoke do executável** (`smoke.spec.mjs`, quando `FANPILOT_ZIP` aponta o zip): extrai o zip, roda o `FanPilot.exe` no runner (sem GCC e sem PawnIO, então com os avisos de placa não suportada) com a `data\` vazia, confere `/`, `/pair`, `/api/state` e a versão do `/api/info`, `--version`, e que o `--diagnose` termina com 0, grava o relatório e não muda nenhum arquivo de estado. Com `--no-browser`, o `--diagnose` não abre o Bloco de Notas.
5. Roda na CI (item 11) a cada push, depois do zip, e no workflow de release antes de publicar. Tempo na CI: cerca de 1 min para os 36 testes.

## O que os testes acharam

- **SSE:** o `EventSource` só tenta de novo sozinho depois de uma falha de rede; uma resposta HTTP de erro (o 503 do FanPilot ainda iniciando, item 05, ou do 17º painel, item 02) o fecha de vez, e o painel ficava em "reconectando…" até alguém recarregar a página. Agora o `app.js` abre um novo stream em 3 s. Os dois testes de SSE falham com o `app.js` anterior.
- **Contraste:** o texto secundário (`--muted`) tinha cerca de 3,5:1 no tema claro, e o tema escuro nem redefinia o token (4,2:1 sobre o fundo dos cartões da GPU). Agora fica acima de 4,5:1 em todos os fundos. O QR do pareamento ganhou `role="img"` para o `aria-label` valer.
- **Nomes longos** (screenshots do `worst`): o rótulo direto do gráfico de RPM é cortado na borda. Não foi corrigido aqui; fica registrado para quem mexer no gráfico.

## Fora de escopo

- Testes em celulares reais: continuam manuais, registrados na issue do item que muda a interface.

## Critérios de aceite

- [x] Quebrar de propósito uma restrição do arraste no editor faz um teste de ponta a ponta falhar (sem o limite do próximo ponto, o teste do arraste falha com 95% em vez de 65%).
- [x] A execução da CI mostra o relatório do Playwright e os screenshots como artefatos.
- [x] O smoke do zip passa na CI e falha se o exe não subir (com um zip cujo exe sai na hora, o teste falha com "O FanPilot.exe do zip saiu com 1").

## Testes

- É o próprio item: `cd tests\e2e; npm ci; npx playwright test`, depois de `dotnet build src\FanPilot.csproj -c Debug`.
- `FakeScenarioTests` (xUnit): cada cenário da placa falsa abre e se comporta como o painel espera.
