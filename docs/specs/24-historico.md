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

## Critérios de aceite

- [ ] Depois de reiniciar o FanPilot, o gráfico de 24 h continua mostrando o período anterior.
- [ ] O período de 7 dias carrega em menos de 1 s no PC e de 2 s no celular.
- [ ] Uma linha truncada no fim do arquivo não quebra a leitura.
- [ ] O espaço em disco respeita a retenção.

## Testes

- Automatizados: agregação, leitura com linha truncada, retenção e redução de pontos.
