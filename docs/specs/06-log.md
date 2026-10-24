# Rotação e deduplicação do log

- **Item:** 06
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** confiabilidade
- **Esforço:** P
- **Depende de:** —

## Problema

- `Log.Write` só acrescenta linhas (`src/App.cs:19-27`); o `fanpilot.log` cresce sem limite.
- Numa falha persistente do LibreHardwareMonitor, o laço de controle grava duas linhas por segundo, para sempre (`src/LhmBoard.cs:152-159`): cerca de 170 mil por dia.
- Mensagens repetidas escondem o que importa quando alguém manda o log numa issue.

## Objetivo

O log fica pequeno, legível e útil para diagnóstico, mesmo com falhas repetidas.

## Solução

1. **Rotação por tamanho:** ao passar de 5 MB, `fanpilot.log` vira `fanpilot.1.log` (e assim por diante), mantendo 3 gerações.
2. **Deduplicação:** a mesma mensagem (nível + texto) repetida em sequência é suprimida. Quando chega outra mensagem, ou a cada 60 s, o log grava "(última mensagem repetida N vezes em Xs)".
3. **Laços registram mudança de estado:** o `LhmBoard` registra a entrada em falha e a recuperação, não cada falha.
4. **Nível `ERROR`** para falhas que tiram o controle dos fans (devolução à BIOS por falha, serviço da Gigabyte indisponível).
5. Relógio e limite de tamanho injetáveis no `Log`, para teste.

## Critérios de aceite

- [ ] Simular 1 erro por segundo durante 10 min gera no máximo ~20 linhas.
- [ ] O conjunto de arquivos de log nunca passa de ~20 MB.
- [ ] O log continua no mesmo lugar (`data\fanpilot.log`) e no mesmo formato de linha.

## Testes

- Automatizados: deduplicação e rotação, com relógio e limite de tamanho injetados.
