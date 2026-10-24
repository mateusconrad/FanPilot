# Impedir duas instâncias e falhar cedo com a porta ocupada

- **Item:** 05
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** bug
- **Esforço:** P
- **Depende de:** —

## Problema

- Nada impede duas instâncias ao mesmo tempo. Ao abrir o exe com o autostart já rodando, a segunda instância abre a própria conexão com a placa. Nas placas sem GCC ela ainda reaplica as curvas (`fans.Start()`, `src/App.cs:68`) e só depois falha ao registrar a porta (`server.Start()`, `src/App.cs:70`). Ao cair, o encerramento (`Exit.Cleanup`) devolve à BIOS os fans que a **primeira** instância controla, até ela reaplicar.
- Se outro programa estiver usando a porta, o erro só aparece depois de mexer nos fans, e como stack trace.

## Objetivo

Só um FanPilot controla os fans por vez, e os erros de inicialização acontecem antes de qualquer acesso ao hardware.

## Solução

1. **Mutex nomeado** `Global\FanPilot-<GUID fixo>`, adquirido logo depois da checagem de Administrador. O `Global\` faz o mutex valer entre sessões, o que o serviço do item 14 vai precisar. Se outra instância já tiver o mutex: mensagem "O FanPilot já está rodando", abre `http://localhost:<porta>/` (se houver alguém no console) e sai com código 4. `AbandonedMutexException` (a instância anterior morreu) conta como adquirido.
2. **`--diagnose`** não exige o mutex (só lê), mas avisa no relatório se outra instância estiver ativa.
3. **Ordem de inicialização:** registrar a porta (`HttpListener.Start`) **antes** de criar a ponte da placa e aplicar curvas. As rotas respondem `503 Iniciando…` até tudo estar pronto. Porta ocupada → "A porta 8787 está em uso por outro programa. Feche-o ou mude `port` no config.json." e saída sem tocar em nenhum fan.

## Critérios de aceite

- [ ] Duas execuções seguidas: a segunda sai em menos de 1 s com a mensagem e abre o painel; o log da primeira não registra nenhuma mudança de fan.
- [ ] Porta ocupada por outro processo (ex.: `python -m http.server 8787`): mensagem clara e nenhum fan tocado.
- [ ] `--diagnose` com o FanPilot aberto funciona e registra o aviso.

## Testes

- Automatizados: `SingleInstance` (a segunda aquisição falha; mutex abandonado conta como adquirido) e porta ocupada detectada antes de qualquer acesso aos fans (com a `FakeBoard` do item 08, nenhum `SetCurve` é chamado).
- Manuais, registrados na issue: os três critérios acima.
