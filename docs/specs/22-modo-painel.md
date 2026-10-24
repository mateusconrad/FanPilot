# Modo painel (sensor panel)

- **Item:** 22
- **Onda:** 4 · Crescimento
- **Tipo:** crescimento
- **Esforço:** P–M
- **Depende de:** —

## Problema e oportunidade

Usar um celular ou tablet velho como painel de sensores ao lado do monitor, ou uma telinha dentro do gabinete, é uma tendência forte entre entusiastas (os sensor panels do AIDA64, as telas de gabinete). O FanPilot já tem os dados e o servidor, mas o painel atual foi feito para mexer, não para ficar exposto.

## Objetivo

Uma tela de leitura em tela cheia, legível de longe, que fica ligada sem intervenção.

## Solução

1. **Rota `/painel`**, autenticada como o painel principal e só de leitura: temperaturas de CPU e GPU em destaque, potências, uso da GPU, RPM dos fans e um minigráfico dos últimos 10 min.
2. **Layout** por tamanho e orientação, com paisagem primeiro: 800×480, 1280×400 (telas de gabinete), celular e tablet. Tipografia escalável e tema escuro por padrão.
3. **Tela cheia** por botão (a API de tela cheia não exige HTTPS).
4. **Tela sempre ligada:** a Wake Lock API exige HTTPS, que o FanPilot não tem na LAN. A alternativa é um vídeo mudo em loop servido pelo próprio FanPilot (a técnica do NoSleep.js, compatível com o CSP atual), mais a dica de configurar o aparelho para não apagar a tela.
5. **Proteção de tela OLED:** deslocamento sutil do conteúdo a cada poucos minutos.
6. **Parâmetros na URL** (`?escala=1.2&tema=claro`) para telas fixas.
7. Botão "Modo painel" no painel principal.

## Critérios de aceite

- [ ] Android (Chrome) em paisagem: a tela fica ligada por 30 min e os dados continuam atualizando.
- [ ] Legível nas resoluções listadas (`tools/ui-mock`).
- [ ] Nenhum controle de fan fica acessível nesse modo.

## Testes

- Ponta a ponta (item 26): screenshots nas resoluções listadas e checagem de que nenhum controle de fan aparece.
