# Alertas de fan parado e temperatura crítica

- **Item:** 16
- **Onda:** 3 · Produto
- **Tipo:** produto, segurança
- **Esforço:** M
- **Depende de:** 02
- **Integra com:** 14, 17

## Problema

O FanPilot mostra os dados, mas não avisa quando algo dá errado. Um fan travado, uma bomba parada ou uma temperatura crítica só são notados se alguém estiver olhando o painel, e as devoluções à BIOS por segurança só aparecem no log.

## Objetivo

O usuário fica sabendo, no painel e fora dele, quando o hardware precisa de atenção.

## Solução

1. **`AlertMonitor`** num laço próprio (1 Hz), alimentado pelo último snapshot e fora do caminho das requisições HTTP. Regras:
   - **Fan parado:** um header que girava (≥ 300 RPM por 30 s) passa a ler menos de 100 RPM por 5 s com velocidade comandada de pelo menos 30%. Headers que nunca giraram (sem tacômetro, como o CPU e o System 3 da B760M) são ignorados. Se for a bomba (item 17), o alerta é crítico.
   - **Temperatura crítica:** CPU ≥ 95 °C ou GPU ≥ 87 °C por 10 s (limites ajustáveis). O alerta some quando a temperatura fica 5 °C abaixo do limite por 30 s.
   - **Controle degradado:** fan devolvido à BIOS por sensor perdido ou falhas repetidas; serviço da Gigabyte indisponível por mais de 60 s.
2. **No painel:** faixa no topo (crítico em vermelho, aviso em âmbar) e a lista dos alertas recentes. O campo `alerts` entra no snapshot do SSE, além de `GET /api/alerts` e `POST /api/alerts/ack` com `{"id"}` no corpo (o id no corpo, e não na rota, porque as chaves dos fans têm `/`). Dispensado ("OK"), o alerta sai da faixa e continua na lista enquanto durar.
   - **Implementação:** `src/Alerts.cs`. `AlertRules` é lógica pura (o relógio vem da entrada, os testes alimentam sequências); o `AlertMonitor` monta a entrada a cada segundo a partir do último snapshot do `Sampler` (os fans, com nome, função e curva, são relidos do `FanManager` a cada 30 s, e a velocidade comandada sai da curva na temperatura do momento; um header com a BIOS fica sem velocidade conhecida e não alerta); a placa informa o que devolveu à BIOS por `IBoard.ControlProblems()`.
3. **Fora do painel, opcional e desligado por padrão:**
   - ntfy: URL do tópico (o FanPilot sugere um tópico aleatório, difícil de adivinhar);
   - Telegram: token do bot e chat id;
   - uma seção "Alertas" no painel, com os limites, os destinos e o botão "Enviar teste";
   - segredos guardados com DPAPI.
4. **No Windows:** notificação pela bandeja, quando o item 14 existir. Fica para a 14b: a bandeja lê `GET /api/alerts`.
5. **Sem spam:** cada alerta notifica no máximo uma vez a cada 10 min, e avisa quando se resolve.
6. **Privacidade:** nada sai do PC sem um destino configurado. A mensagem leva só o alerta e o nome que o usuário deu ao PC, sem IPs.

## Critérios de aceite

- [x] Placa falsa com um fan que para: alerta no painel em até 10 s e a notificação ntfy de teste chega (cenário `stall` da placa falsa, de ponta a ponta, com um servidor local no lugar do ntfy).
- [x] Um header sem tacômetro nunca gera alerta.
- [x] Temperatura oscilando em volta do limite não gera mais de uma notificação a cada 10 min (o aviso de que terminou só vai quando o início foi avisado).
- [x] Com os destinos vazios, nenhuma requisição sai do PC.

## Testes

- Automatizados: regras com sequências sintéticas (`AlertRulesTests`), limite de frequência e nada enviado sem destino (`AlertMonitorTests`), segredos cifrados e mascarados e validação (`AlertSettingsTests`), envio para ntfy e Telegram contra um servidor local (`AlertSenderTests`); de ponta a ponta, o cenário `stall` (faixa, lista, "OK", ntfy recebendo o alerta e o teste).
- Manuais: ntfy no celular com a placa falsa; alerta de temperatura com um limite baixo temporário na B760M (verificação em hardware, `docs/ROADMAP.md`).
