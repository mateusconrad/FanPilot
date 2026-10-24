# Tirar o SSE e a proteção da GPU do laço de amostragem

- **Item:** 02
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** confiabilidade, segurança
- **Esforço:** P–M
- **Depende de:** —

## Problema

- `Sampler.Publish` chama cada assinante do `/api/stream`, um depois do outro, na thread do Sampler (`src/App.cs:258`), e cada assinante escreve de forma síncrona no socket do cliente (`src/WebServer.cs:256-262`). Um cliente que para de ler sem fechar a conexão (celular com a tela apagada, Wi-Fi fraco) pode deixar essa escrita bloqueada e, com ela, o laço inteiro.
- A proteção de temperatura da GPU (`EnforceSafety`, `src/Nvml.cs:61-70`) roda dentro desse mesmo laço (`src/App.cs:240-242`), depois das chamadas IPC ao serviço da Gigabyte (`ReadSensors`, `ReadFanTach`), que também podem demorar. A segurança da GPU fica dependendo da rede e do IPC, que não têm nada a ver com ela.
- Não há limite de conexões SSE.

## Objetivo

Nenhum cliente HTTP e nenhuma lentidão da placa-mãe atrasam a amostragem nem a proteção da GPU.

## Solução

1. **`SseHub`** (novo): cada cliente tem um espaço para o "último frame" (capacidade 1; o frame novo substitui o pendente) e uma tarefa de escrita própria, com `WriteAsync`.
   - `Publish` só troca o frame e sinaliza: O(1), nunca bloqueia.
   - Escrita que passar de 10 s → conexão abortada e cliente removido.
   - No máximo 16 clientes simultâneos; acima disso, `503` com `Retry-After`.
2. **Laço próprio da GPU:** `GpuMonitor` ganha uma thread (`gpu`, 1 Hz) que lê o driver, aplica `EnforceSafety` e guarda o último snapshot. O `Sampler` passa a usar esse snapshot em cache, sem chamar o driver.
3. **Amostra lenta visível:** o `Sampler` mede quanto cada amostra leva. Acima de 3 s, `WARN` (deduplicado pelo item 06). O snapshot ganha `service.sampleMs`, e o painel mostra "dados atrasados" quando o último snapshot tiver mais de 5 s.

## Fora de escopo

- Timeout das chamadas IPC dentro das DLLs do GCC (é delas o canal). Fica registrado como risco no item 10.

## Critérios de aceite

- [ ] Um cliente que abre `/api/stream` e nunca lê não altera a cadência de 1 Hz de `/api/state` nem a dos outros clientes (script de teste em `tools/`).
- [ ] Com o `EasyTuneEngineService` parado, a proteção da GPU continua atuando (teste com GPU falsa).
- [ ] A 17ª conexão SSE recebe `503`; quando uma fecha, a próxima entra.
- [ ] O painel continua atualizando como hoje, no PC e no celular.

## Testes

- Automatizados: `SseHub` com um escritor falso que bloqueia (`Publish` retorna na hora, o frame mais novo vence, limite de clientes); laço da GPU com GPU falsa acima do limite → `SetAuto` chamado mesmo com o Sampler parado.
- Manuais: o script do cliente "parado" e o painel aberto no celular por 10 min, com a tela apagando e voltando.
