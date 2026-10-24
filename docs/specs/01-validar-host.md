# Validar o header Host nas requisições

- **Item:** 01
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** segurança
- **Esforço:** P
- **Depende de:** —

## Problema

O servidor HTTP aceita qualquer valor no header `Host`:

- escuta em `http://+:<porta>/` (`src/WebServer.cs:32`) e não confere o `Host` em nenhum ponto;
- requisições vindas do próprio PC são consideradas autenticadas sem sessão (`src/WebServer.cs:57`, `src/WebServer.cs:68`);
- a checagem de origem dos POSTs compara `Origin` com `Request.Url.Authority`, que vem do próprio `Host` da requisição (`src/WebServer.cs:158-165`).

Somadas, essas três coisas deixam a API local exposta a DNS rebinding: uma página web aberta no PC consegue fazer o navegador falar com `127.0.0.1:<porta>` usando um domínio dela, e o FanPilot trata a requisição como vinda do próprio PC. Navegadores recentes vêm adicionando proteções contra acesso à rede local, mas não todos e não em todas as versões. A defesa precisa estar no servidor.

## Objetivo

Só atender requisições endereçadas a um nome que o FanPilot reconhece como seu.

## Solução

1. **`HostGuard`** (novo, `src/HostGuard.cs`) mantém o conjunto de hosts permitidos:
   - nomes de loopback: `localhost`, `127.0.0.1`, `::1`;
   - todos os endereços unicast IPv4/IPv6 das interfaces ativas, inclusive as virtuais (a página `/pair` deixa o usuário escolher qualquer uma);
   - `publicHost` do `config.json`;
   - `Environment.MachineName` (acesso pelo nome do PC na rede).

   A comparação ignora maiúsculas, ponto final (`localhost.`) e colchetes de IPv6. A porta do `Host` precisa ser a `port` configurada. O conjunto fica em cache e é recalculado em `NetworkChange.NetworkAddressChanged` (o DHCP trocou o IP, o Wi-Fi mudou).
2. **Em `WebServer.Handle`, antes de qualquer rota**, inclusive arquivos estáticos: `Host` fora da lista → `421 Misdirected Request` com corpo curto, sem detalhes. Log `WARN` com o host recusado, no máximo uma vez por host por hora.
3. **Confiança de loopback só com nome de loopback:** `Loopback` passa a exigir endereço remoto de loopback **e** `Host` igual a `localhost`, `127.0.0.1` ou `[::1]`. É defesa em profundidade caso a lista de hosts falhe.
4. **`SameOriginPost`:** continua exigindo `X-FanPilot: 1`. Quando houver `Origin`, o host:porta dele também precisa estar na lista, e não apenas ser igual ao `Host`.
5. README, seção Segurança: uma linha sobre a validação de `Host`.

## Fora de escopo

- Processos locais que não são navegador continuam confiáveis via loopback (modelo atual, documentado). Restringir isso exige outro mecanismo de autenticação local, a avaliar junto com a bandeja (item 14).
- HTTPS.

## Critérios de aceite

- [ ] `curl -H "Host: exemplo.invalid:8787" http://127.0.0.1:8787/api/fans` → `421`.
- [ ] `curl -H "Host: exemplo.invalid:8787" http://127.0.0.1:8787/` → `421` (arquivos estáticos também).
- [ ] `http://localhost:8787/`, `http://127.0.0.1:8787/` e `/pair` no PC funcionam como hoje.
- [ ] Celular pareado pelo IP da LAN e pelo `publicHost` continua funcionando.
- [ ] Depois de trocar de rede (novo IP), o novo endereço é aceito sem reiniciar o FanPilot.
- [ ] POST com `Origin` de host desconhecido → `403`.

## Testes

- Automatizados: o 01 vem antes da infraestrutura de testes, então os testes dele são escritos no item 08. Tabela de casos do `HostGuard` (permitidos, porta errada, IPv6 com colchetes, ponto final, maiúsculas, host vazio).
- Manuais: os critérios acima, com `curl` e com o celular.
