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

## Fora de escopo

- Agendamento (ex.: Silencioso às 23h) e troca automática ao abrir um jogo: anotados como próximos passos.

## Critérios de aceite

- [ ] Ir de um perfil para Personalizado devolve exatamente as configurações anteriores.
- [ ] Turbo ligado durante um perfil → ao desligar, volta ao perfil.
- [ ] Bombas não mudam com os perfis.
- [ ] O modo sobrevive a reinício nos dois caminhos (GCC e LHM).

## Testes

- Automatizados: matriz de transições entre modos, persistência e bombas.
