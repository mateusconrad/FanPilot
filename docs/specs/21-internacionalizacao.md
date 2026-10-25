# Inglês e internacionalização

- **Item:** 21
- **Onda:** 4 · Crescimento
- **Tipo:** crescimento
- **Esforço:** M
- **Depende de:** —

## Problema

- Painel, mensagens, README e guias só existem em português. O repositório é público, e a maior parte do público de hardware de PC lê em inglês.
- O código tem formatos de pt-BR fixos (`toLocaleTimeString('pt-BR')` em `wwwroot/app.js:265`, vírgula decimal em `wwwroot/app.js:64`).
- Os logs misturam inglês e português.

## Objetivo

Painel e documentação em inglês e português, sem nenhuma regressão para quem usa em português.

## Solução

1. **Painel:** dicionários `wwwroot/i18n/pt-BR.json` e `en.json`, uma função `t(chave, parâmetros)` e HTML estático com `data-i18n`. O idioma segue o navegador por padrão, com troca manual guardada no aparelho. Números e horas passam pelo `Intl`.
2. **Erros e avisos do backend** com código e parâmetros (`{"error": "…", "code": "curve.points_count", "params": {"n": 8}}`). O painel traduz pelo código e usa a mensagem como reserva.
3. **Console:** as mensagens para o usuário (PawnIO, Administrador) seguem o idioma do Windows (português ou inglês).
4. **Logs em inglês**, com frases estáveis, fáceis de buscar em issues de qualquer país. *Decisão proposta.*
5. **Documentação:** `README.md` em inglês como principal, mais um `README.pt-BR.md`; guia de testes nos dois idiomas. *Decisão proposta.*
6. Formulários de issue (item 13) em inglês, com rótulos bilíngues.

### Etapas

- **21a (feita):** `wwwroot/js/i18n.mjs` (`t`, escolha do idioma, `Intl` para números e horas, `data-i18n` e
  `data-i18n-attr` no HTML), os dicionários `wwwroot/i18n/pt-BR.json` e `en.json` com chaves planas e parâmetros
  `{nome}`, e todo o texto do painel, do pareamento e da página de aparelho bloqueado vindo deles. O idioma segue o
  navegador (português para qualquer `pt-*`, inglês para o resto) e pode ser trocado no rodapé; a escolha fica no
  aparelho (`localStorage`) e vale também para o pareamento. No `app.js` a função se chama `tr`, porque `t` já é
  temperatura e tempo no arquivo inteiro.
- **21b (feita):** `src/I18n.cs` lê os mesmos dicionários, embutidos no exe. Uma mensagem é uma `Msg` (chave e
  parâmetros; `Text` é o inglês, que vai para o log); `UserError` (400) e `Unavailable` (503) carregam a sua. A API
  responde `{"error": "<inglês>", "code": "err.curve.points", "params": {"n": 8}}`, e o painel traduz pelo código, com o
  texto como reserva; uma mensagem pode ser parâmetro de outra ("GPU indisponível: {error}"). Avisos, alertas (título e
  detalhe), o aviso da placa, os erros e a nota de segurança da GPU vão como `{code, params, text}`; as leituras que o
  próprio FanPilot nomeia ("CPU · pacote") levam `titleKey` e `hintKey`. As páginas que o servidor escreve sozinho
  (iniciando, código inválido, pareado) seguem o `Accept-Language`. Os alertas enviados ao ntfy e ao Telegram saem no
  idioma do painel em que os avisos foram salvos (`language` no `alerts.json`; antes de salvar, o do Windows). A placa e
  a GPU falsas ganharam nomes em inglês ("Test board", "Test GPU").
- **21c (feita):** o log é todo em inglês, com frases estáveis (a linha "repetida" também). O que é para a pessoa na
  janela do console (pareamento, porta ocupada, sem Administrador, PawnIO, dados de outro PC, avisos) usa
  `Log.Info/Warn/Error(Msg)`: a janela mostra no idioma do Windows e o arquivo de log guarda o inglês. As mensagens de
  console soltas (já está rodando, opções inválidas, importação) e os relatórios do `--diagnose` (inclusive o marcador
  "(omitido)") e do `--hardware-test` seguem o idioma do Windows; os testes passam o idioma, já que o deste PC e o da CI
  são diferentes. Os nomes de arquivos e pastas em disco (`.corrompido-`, `outra-maquina-`, `diagnostico-`) não mudam.
- **21d (feita):** `README.md` em inglês, com o `README.pt-BR.md` e a troca de idioma no topo de cada um; o guia de
  teste também nos dois idiomas (`docs/TESTING-OTHER-BOARDS.md` e `docs/TESTANDO-OUTRAS-PLACAS.md`). A lista de
  placas virou uma só, `docs/COMPATIBILITY.md` (a tabela é quase só dados), com um resumo em português. Os formulários
  de issue estão em inglês com os rótulos em português entre parênteses; os nomes dos arquivos, os ids dos campos (o
  link do `--diagnose` os preenche) e os labels do repositório não mudaram, e o título preenchido virou
  "[Compatibility] <placa>".

Ficaram em português, de propósito: o `CHANGELOG.md` (de onde saem as notas da release; decidir antes da `v0.1.0`),
o `docs/REVERSE_ENGINEERING.md`, o roadmap e as specs (o planejamento do dono), e os nomes de arquivos e pastas em
disco.

## Critérios de aceite

- [x] Navegador em inglês → painel inteiro em inglês. Em português → como hoje (a suíte de ponta a ponta, toda em
  português, passa sem mudar o que confere).
- [x] Nenhum texto visível fixo no `app.js`, no `index.html`, nos arquivos `pair.*` nem no `locked.html` (o teste JS
  confere o HTML; os nomes de idioma no seletor ficam cada um na própria língua).
- [x] Erros do backend aparecem traduzidos pelo código.

## Testes

- Automatizados: as duas línguas têm exatamente as mesmas chaves e os mesmos parâmetros, toda chave usada pelo painel e
  pelo C# existe nas duas, e o HTML não tem texto fixo (`tests/js/i18n.test.mjs`); `I18nTests` (C#) cobre o
  `Accept-Language`, parâmetros, plurais, mensagens dentro de mensagens e o formato da API; os testes que olhavam
  textos agora conferem a chave e o texto nos dois idiomas, passando o idioma (o Windows deste PC e o da CI diferem).
- Ponta a ponta (`tests/e2e/i18n.spec.mjs`): o painel, o editor, o pareamento e a página de bloqueio com o navegador em
  inglês, sem nenhuma letra acentuada do português na tela; erros da API, um alerta de verdade, a dica do PawnIO e a
  GPU indisponível em inglês; a troca manual guardada no aparelho. Screenshots em inglês a 320 px nas placas `demo` e
  `worst`, sem nada mais largo que a tela.
- Manuais: nenhum além do que já está no roteiro de hardware.
