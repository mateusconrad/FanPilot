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

## Critérios de aceite

- [ ] Navegador em inglês → painel inteiro em inglês. Em português → como hoje.
- [ ] Nenhum texto visível fixo no `app.js`, no `index.html`, nos arquivos `pair.*` nem no `locked.html`.
- [ ] Erros do backend aparecem traduzidos pelo código.

## Testes

- Automatizados: as duas línguas têm exatamente as mesmas chaves (teste JS), e todo código de erro do backend tem tradução.
- Ponta a ponta (item 26): os fluxos principais nos dois idiomas.
- Manuais: `tools/ui-mock` nos dois idiomas, em 320 px (os textos em inglês têm outro comprimento).
