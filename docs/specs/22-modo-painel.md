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

### Como ficou (25/10/2026)

- `/panel` (e `/painel`) serve `wwwroot/panel.html` com a mesma exigência de pareamento do painel; pela rede, sem
  pareamento, vem a página de bloqueio. Os dados chegam pelo mesmo `/api/stream`; o histórico, por `/api/history`; os
  nomes dos fans, por `/api/fans` a cada 30 s. Nenhuma chamada da página muda algo.
- Layout por formato em `panel.css`: dois cards (CPU e GPU) em cima, o minigráfico no meio e os fans embaixo; em telas
  largas e baixas (proporção acima de 12:5, como 1280×400) tudo numa linha; em retrato, uma coluna. Os tamanhos seguem
  a tela (`vmin`) vezes a escala. O card da CPU usa o `cpu-main` (item 19), com o rótulo traduzido.
- O minigráfico cobre até os últimos 10 min; com menos histórico (o FanPilot acabou de abrir), espalha o que há em
  pelo menos 1 min, em vez de desenhar um ponto na borda.
- Os botões (tela cheia, manter a tela ligada, voltar ao painel) aparecem por 5 s depois de um toque, um clique ou uma
  tecla, e ficam enquanto um deles tem o foco. O iPhone não tem tela cheia para páginas: lá o botão some.
- Manter a tela ligada: a Wake Lock API quando o navegador oferece (HTTPS ou o próprio PC); senão, o
  `wwwroot/media/keepawake.mp4` (2 s, 16×16, com uma trilha de áudio silenciosa, 2,3 KB, gerado com o `ffmpeg`) tocando
  mudo em loop, a técnica do NoSleep.js. A CSP não muda: o vídeo vem do próprio FanPilot.
- Parâmetros: `?scale=1.3&theme=light`, ou `?escala=1.3&tema=claro` (`auto`/`sistema` segue o aparelho; o padrão é
  escuro); a escala fica entre 0,5 e 2,5 (`wwwroot/js/dash.mjs`).
- OLED: tudo se desloca até 6 px a cada 3 minutos.
- Nos dois idiomas, como o resto do painel (item 21).

## Critérios de aceite

- [ ] Android (Chrome) em paisagem: a tela fica ligada por 30 min e os dados continuam atualizando (falta conferir num
  celular de verdade: tabela de verificação do ROADMAP).
- [x] Legível nas resoluções listadas: 800×480, 1280×400, celular em paisagem e em retrato e tablet, sem rolagem, com os
  números de pelo menos 40 px e nenhum texto saindo do card (E2E, com screenshots).
- [x] Nenhum controle de fan fica acessível nesse modo (E2E: nenhum campo, só os botões da barra, nenhum POST).

## Testes

- Unitários: `tests/js/dash.test.mjs` (parâmetros da URL nos dois idiomas, cores, faixa e linha do minigráfico).
- Ponta a ponta (`tests/e2e/panel-mode.spec.mjs`): screenshots e caber na tela nas resoluções listadas, nenhum controle
  de fan nem POST, dados ao vivo, tema e escala pela URL, a barra que some e volta, tela cheia, manter a tela ligada
  pelo vídeo quando não há Wake Lock, o link no painel e o endereço `/painel`, e o axe nos dois temas. Pela rede, sem
  pareamento, `/panel` dá a página de bloqueio (`WebServerTests`, como Administrador).
