# Histórico persistente (24 h e 7 dias)

- **Item:** 24
- **Onda:** 4 · Crescimento
- **Tipo:** produto
- **Esforço:** M
- **Depende de:** —

## Problema

O histórico guarda só os últimos 10 minutos, em memória (`src/App.cs:183`), e some ao reiniciar. Perguntas comuns ficam sem resposta: "a que temperatura a GPU chegou no jogo de ontem?", "o fan parou de madrugada?".

## Objetivo

Consultar temperaturas, potências e RPM de até 7 dias, no PC e no celular, sem pesar no disco nem no painel.

## Solução

1. **Duas camadas:** a atual (1 s × 10 min, em memória) e uma em disco, com médias e máximos a cada 10 s, por 7 dias.
2. **Arquivos diários** `data\history\AAAA-MM-DD.csv`, só de acréscimo, com uma coluna por série e cabeçalho (≈ 1–2 MB por dia).
   - Se o conjunto de séries mudar no meio do dia, começa uma nova parte do arquivo.
   - Uma linha final incompleta (queda de energia) é ignorada na leitura.
   - Arquivos além da retenção são apagados.
3. **API:** `/api/history?range=10m|1h|24h|7d`, reduzida no servidor para no máximo ~600 pontos por série (o que o gráfico já desenha hoje), com o mínimo e o máximo de cada intervalo.
4. **Painel:** seletor de período nos dois gráficos; a dica mostra a média e o máximo de cada intervalo.
5. **Configuração:** retenção (padrão de 7 dias) e opção de desligar.

### Como ficou (25/10/2026)

- `HistoryStore` (`src/History.cs`) recebe cada leitura do `Sampler` (uma por segundo) e, a cada 10 s, acrescenta uma
  linha com a média e o máximo de cada série em `data\history\AAAA-MM-DD.csv` (pela data local). As séries são as dos
  gráficos e das integrações, não todos os sensores: `cpu-main` (a temperatura do gráfico da CPU, item 19),
  `cpu-power` ("CPU Package"), `gpu-temp`, `gpu-power` e `fan-K` (o RPM de cada fan, pela posição). Com 4 fans,
  ≈ 0,7 MB por dia.
- Cabeçalho `t,cpu-main,cpu-main:max,…`. Uma série que aparece no meio do dia (o driver da GPU que sobe atrasado) e
  cada partida do FanPilot começam uma nova parte do arquivo, com o seu cabeçalho; uma série sem leitura numa linha
  fica com as células vazias. Na leitura só contam as linhas terminadas em quebra de linha e com as colunas do
  cabeçalho: a linha cortada por uma queda de energia (ou sendo gravada naquele instante) fica de fora. O texto é
  percorrido sem ser quebrado em strings: 7 dias (≈ 60 mil linhas, 4,9 MB) em ≈ 90 ms neste PC.
- A retenção apaga, na partida e na virada do dia, os arquivos de antes de hoje − `historyDays` (7 por padrão, de 0 a
  90 no `config.json`). Com 0 nada é gravado: o painel fica só com os 10 minutos, sem o seletor, e
  `/api/history?range=24h` responde 503 (`err.historyOff`).
- `/api/history?range=10m|1h|24h|7d`: `10m` (o padrão) é o de sempre, da memória, um ponto por segundo; os outros leem
  os arquivos numa grade de no máximo 600 pontos (de 10 s, 150 s e ≈ 17 min), com a média (`series`), o máximo (`max`)
  e o mínimo (`min`) das linhas de cada ponto e `null` onde não há linha, para o gráfico interromper a linha onde o
  FanPilot estava fechado. Período inválido → 400 (`err.historyRange`). `/api/info` diz se o histórico está ligado
  (`history`).
- No painel, o seletor 10 min · 1 h · 24 h · 7 dias aparece nos dois gráficos e vale para os dois; a escolha fica
  guardada no aparelho. O título e a descrição para o leitor de tela dizem o período; o eixo conta minutos, horas ou
  dias, mais espaçado no celular; nos períodos longos a dica mostra o dia da semana e, quando o ponto junta várias
  linhas, o máximo ao lado da média. Os 10 min continuam ao vivo; os outros períodos são relidos a cada minuto.
- As potências ficam no arquivo e na API (para as integrações do item 23), mas não ganharam gráfico.

## Critérios de aceite

- [x] Depois de reiniciar o FanPilot, o gráfico de 24 h continua mostrando o período anterior.
- [x] O período de 7 dias carrega em menos de 1 s no PC (≈ 90 ms na API) e de 2 s no celular (falta conferir num
  celular de verdade).
- [x] Uma linha truncada no fim do arquivo não quebra a leitura.
- [x] O espaço em disco respeita a retenção.

## Testes

- Automatizados (`HistoryTests`): 10 s viram uma linha com a média e o máximo; uma série nova começa uma parte nova do
  arquivo; a linha cortada e as linhas fora do cabeçalho são ignoradas; retenção; grade de até 600 pontos com mínimo,
  máximo e lacunas; reinício mantendo o que veio antes; 7 dias lidos rápido.
- Ponta a ponta (`tests/e2e/history.spec.mjs`, com uma semana semeada em `data-fake\history\`): 7 dias com o pico de
  três dias atrás na dica, 24 h e 1 h nos dois gráficos, o período lembrado ao recarregar, a semana na API e no painel
  em menos de 1 s, o seletor num celular de 360 px, o que foi gravado continua lá depois de reiniciar, e o histórico
  desligado (sem seletor, nada em disco, 503).
