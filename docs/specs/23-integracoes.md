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

## Critérios de aceite

- [ ] O Prometheus coleta `/metrics` com o token; sem token, ou com token revogado → `401`.
- [ ] Um sensor REST do Home Assistant lê `/api/v1/sensors`.
- [ ] Com o MQTT configurado, as entidades aparecem sozinhas no Home Assistant.
- [ ] Um token de integração não consegue chamar nenhuma rota de escrita.

## Testes

- Automatizados: formato do `/metrics` (comparado com a saída esperada), autenticação por token e escopo só de leitura.
- Manuais: Prometheus e Home Assistant locais.
