# Integrações: Prometheus, JSON e Home Assistant (MQTT)

- **Item:** 23
- **Onda:** 4 · Crescimento
- **Tipo:** crescimento
- **Esforço:** M
- **Depende de:** 01, 09

## Problema e oportunidade

O público de homelab e de Home Assistant quer monitorar o PC junto com o resto da casa. Hoje só um navegador pareado lê os dados, e não há como integrar sem passar por esse fluxo.

## Objetivo

Expor os dados para o Prometheus/Grafana e para o Home Assistant, só para leitura e com autenticação própria.

## Solução

**Fase 1: tokens, `/metrics` e JSON**

1. **Tokens de integração**, criados na página `/pair` (só no PC): nome, data de criação, último uso e botão para revogar. O token é mostrado uma vez, guardado como SHA-256 e só permite leitura. Uso: `Authorization: Bearer fp_<token>`.
2. **`GET /metrics`** no formato do Prometheus: `fanpilot_temperature_celsius{sensor=…}`, `fanpilot_fan_rpm{fan=…}`, `fanpilot_fan_duty_percent{fan=…}`, `fanpilot_gpu_*` e `fanpilot_up`.
3. **`GET /api/v1/sensors`**: JSON estável e versionado, separado do snapshot interno, que pode mudar.
4. A validação de `Host` (item 01) e o firewall restrito ao perfil Privado valem do mesmo jeito.
5. Documentação com exemplos: `scrape_config` do Prometheus e sensor REST do Home Assistant.

**Fase 2: MQTT com descoberta do Home Assistant**

6. Cliente MQTT (MQTTnet 4.x, que roda em `net472`), configurado no painel: broker, porta, TLS, usuário e senha (senha guardada com DPAPI).
7. Descoberta automática (`homeassistant/sensor/fanpilot_<pc>_<sensor>/config`), estado a cada 5 s e disponibilidade com LWT.
8. Só leitura nesta versão. Controlar os perfis pelo Home Assistant fica como próximo passo.

### Como ficou (25/10/2026)

- **Tokens** (`IntegrationTokens`, `src/Integrations.cs`): criados e revogados na página `/pair` (só no PC), `fp_` +
  256 bits, mostrados uma vez e guardados como SHA-256 em `data\integrations.json`, com nome, criação e último uso
  (gravado a cada poucos minutos); no máximo 20. `Authorization: Bearer fp_…` abre só o `/metrics` e o
  `/api/v1/sensors`; qualquer outra rota responde 403 (`err.token.scope`), as de escrita inclusive. Um token inválido
  ou revogado recebe 401 com `WWW-Authenticate`, até vindo do próprio PC, para a integração mostrar que ele parou de
  valer. O PC e os aparelhos pareados também leem as duas rotas.
- **`/metrics`**: o formato de texto do Prometheus (0.0.4): `fanpilot_up`, `fanpilot_info`, CPU (temperatura e
  potência), cada temperatura e potência (`sensor`, `name`), RPM e duty de cada fan (`fan` = o nome do header,
  `name` = o nome dado no painel), GPU e alertas ativos por gravidade. O duty é o que a curva pede na temperatura
  atual, com a fonte da curva (item 25); a lista de fans vem de um cache de 30 s (`FanListCache`), o mesmo que os
  alertas já mantinham.
- **`/api/v1/sensors`**: JSON versionado (`"version": 1`, só ganha campos), separado do snapshot do painel.
- **MQTT** (`MqttPublisher`, `src/Mqtt.cs`, MQTTnet 4.3.7): configurado no card "Home Assistant (MQTT)" do painel
  (broker, porta, TLS, usuário, senha e prefixo de descoberta); a senha fica cifrada com a DPAPI em `data\mqtt.json`
  e nunca volta ao painel. Conectado, publica a descoberta (`homeassistant/sensor/fanpilot_<pc>_<sensor>/config`,
  retida, com os nomes no idioma do painel), o estado a cada 5 s em `fanpilot/<pc>/state` (o JSON do
  `/api/v1/sensors`) e `online` em `fanpilot/<pc>/availability`, com `offline` como última vontade e ao parar. Uma
  entidade vista fica (uma GPU cujo driver reinicia não some do Home Assistant); renomear um fan republica só a dele.
  Sem conexão, tenta de novo em 5, 10, 30 e 60 s, e o painel diz o motivo (senha recusada, broker fora do ar).
- Só leitura nesta versão: controlar os perfis pelo Home Assistant fica como próximo passo.

## Critérios de aceite

- [x] O Prometheus coleta `/metrics` com o token; sem token, ou com token revogado → `401` (nos testes, pela rede na
  CI; falta um Prometheus de verdade).
- [x] Um sensor REST do Home Assistant lê `/api/v1/sensors` (o formato está testado; falta conferir num Home
  Assistant).
- [x] Com o MQTT configurado, as entidades aparecem sozinhas no Home Assistant (a descoberta conferida contra um
  broker de verdade e um mínimo; falta um Home Assistant com o Mosquitto).
- [x] Um token de integração não consegue chamar nenhuma rota de escrita.

## Testes

- Automatizados: `IntegrationTests` (tokens, o JSON e o texto exato do `/metrics`); `WebServerTests` (as rotas pelo
  PC e, só com Administrador, pela rede: 401, 403 e nada muda); `MqttTests`, contra o servidor do próprio MQTTnet
  (descoberta, estado, `online`/`offline`, a última vontade com a conexão derrubada, senha errada, sem broker,
  configurações e senha cifrada); `tests/e2e/integrations.spec.mjs` (tokens no `/pair`, `/metrics` com e sem token, o
  card do MQTT contra um broker mínimo em Node, `tests/e2e/mqtt-broker.mjs`).
- Manuais: Prometheus e Home Assistant locais (na seção de verificação pendente do roadmap).
