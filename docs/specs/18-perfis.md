# Perfis globais: Silencioso, Padrão e Performance

- **Item:** 18
- **Onda:** 3 · Produto
- **Tipo:** produto
- **Esforço:** P–M
- **Depende de:** 07, 17

## Problema

Mudar o comportamento do PC inteiro (silencioso à noite, performance para jogar) exige editar fan por fan. As predefinições existem, mas só dentro do editor de cada fan (`wwwroot/app.js:387-392`). O Turbo já sabe lembrar e restaurar o estado anterior, mas só para "tudo em 100%".

## Objetivo

Trocar o comportamento de todos os fans com um toque e depois voltar exatamente à configuração pessoal.

## Solução

1. **Modo global** no lugar do estado específico do Turbo: `custom` (configuração pessoal, a de hoje) ou `profile:quiet|standard|performance`. O Turbo vira uma camada temporária por cima de qualquer modo e, ao desligar, volta ao modo anterior. O `global-mode.json` substitui o `turbo.json`, com migração automática.
2. **Aplicar um perfil:** cada fan que não é bomba recebe a curva da predefinição, redimensionada para o número de pontos e ajustada aos limites, mantendo o sensor de referência atual. Bombas seguem a política do item 17. As configurações pessoais nunca são sobrescritas: "Personalizado" reaplica exatamente o que havia.
3. **Editar um fan com perfil ativo:** o modo vira `custom`, e os outros fans ficam com a curva do perfil como configuração pessoal (o painel avisa). Com o Turbo ativo, a edição continua bloqueada.
4. **Predefinições num lugar só:** saem do `app.js` e passam a vir do backend (`/api/fans`), usadas tanto pelo editor quanto pelos perfis.
5. **Painel:** seletor no card dos fans (Silencioso · Padrão · Performance · Personalizado), ao lado do Turbo. **Bandeja** (item 14): o mesmo menu.

### Como ficou (25/10/2026)

- `FanManager`: `_mode` (`custom` ou `profile:<nome>`), `_turbo` e `_personal` (o pedido do usuário para cada header, `null` para o que o FanPilot não controlava), em `global-mode.json`. `_personal` é tirado ao sair do `custom` (para um perfil ou para o Turbo) e reaplicado ao voltar. Um perfil escolhido durante o Turbo só é gravado; vale quando o Turbo desliga. O `turbo.json` antigo (v1 ou v2) é convertido na primeira leitura e apagado (o v1 continua como `.v1`).
- O pedido de um perfil leva `"profile": "<nome>"`, para o painel mostrar "Perfil Silencioso", e a resposta (item 15) que o fan já usava.
- `FanPresets` (`src/FanPresets.cs`): as quatro predefinições e o mesmo `resample`/`normalize` do `curve.mjs` (com o arredondamento do JavaScript), mais temperaturas sempre crescentes quando o `maxFullSpeedTemp` aperta. `GET /api/fans` traz `mode` e `presets`; o editor monta os chips a partir deles.
- `POST /api/fans/mode` com `{"mode"}`. O `--hardware-test` recusa rodar com um perfil ativo (editar um fan sairia do perfil).

## Fora de escopo

- Agendamento (ex.: Silencioso às 23h) e troca automática ao abrir um jogo: anotados como próximos passos.

## Critérios de aceite

- [x] Ir de um perfil para Personalizado devolve exatamente as configurações anteriores.
- [x] Turbo ligado durante um perfil → ao desligar, volta ao perfil.
- [x] Bombas não mudam com os perfis.
- [x] O modo sobrevive a reinício nos dois caminhos (GCC e LHM), com a placa falsa; na B760M, fica na verificação em hardware.

## Testes

- Automatizados (`ProfileTests`, `FanPresetsTests`): matriz de transições (pessoal → perfil → pessoal, perfil → perfil, Turbo por cima de um perfil, perfil escolhido durante o Turbo, edição saindo do perfil), bombas, reinício nos dois tipos de placa, conversão do `turbo.json`, e o `resample` igual ao do painel. De ponta a ponta: o seletor no painel com o Turbo por cima, o aviso no editor e as bombas do pior caso.
