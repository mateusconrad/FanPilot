# CI, versionamento e releases

- **Item:** 11
- **Onda:** 2 · Entrega
- **Tipo:** infra
- **Esforço:** P–M
- **Depende de:** 08, 09, 10

## Problema

- Não há CI: nada compila nem testa cada mudança.
- Não há versão: o exe é sempre `1.0.0.0` (`app.manifest:3`), e o zip leva data e commit no nome (`scripts/package.ps1:5-7`). Um bug report não diz qual versão a pessoa usa.
- A release é feita à mão, num PC específico.

## Objetivo

Cada push na `main` é compilado e testado automaticamente, e cada tag gera uma release versionada, com checksum e notas.

## Solução

1. **Versão semântica** num lugar só (`Directory.Build.props`), começando em `0.1.0`, com versão informativa que inclui o commit (`0.1.0+abc1234`). Ela aparece em:
   - `FanPilot.exe --version`, na primeira linha do log e no cabeçalho do `--diagnose`;
   - `/api/info` (`version`, `backend`, `board`) e no rodapé do painel;
   - no nome do zip: `FanPilot-0.1.0-win-x64.zip`.
2. **`CHANGELOG.md`** no formato Keep a Changelog. Todo commit que muda algo visível atualiza a seção "Não lançado".
3. **`.github/workflows/ci.yml`** (push na `main`, `windows-latest`): checkout → setup-dotnet 8 → `dotnet restore --locked-mode` → build Release → `dotnet test` → setup-node → `node --test` → testes de ponta a ponta (item 26, quando existir) → empacota e publica o zip como artefato da execução.
4. **`.github/workflows/release.yml`** (tag `v*.*.*`): confere que a tag bate com a versão → build e testes → zip → `SHA256SUMS.txt` → assinatura (item 12) → `gh release create`, com as notas tiradas da seção do CHANGELOG. Versões `0.x` saem como pre-release.
5. **`.github/dependabot.yml`**: NuGet e GitHub Actions, toda semana. Uma atualização do LibreHardwareMonitor só entra depois do roteiro de verificação em hardware (regra no `docs/ROADMAP.md`).
6. **Build vermelho na `main` tem prioridade:** o trabalho é direto na `main` (decisão de 24/10/2026), então a CI roda depois do push, sem PR para bloquear. Uma falha é corrigida antes do próximo item.
7. **Checagem de encoding:** a CI falha se algum `.ps1` com caractere não-ASCII estiver sem BOM (o Windows PowerShell 5.1 leria o arquivo como ANSI e quebraria os acentos, como aconteceu antes do item 09).

## Critérios de aceite

- [ ] Cada push na `main` mostra o check da CI e o zip como artefato.
- [ ] Um `.ps1` com acento e sem BOM faz a CI falhar.
- [ ] A tag `v0.1.0` gera uma release com o zip, o `SHA256SUMS.txt` e as notas.
- [ ] `FanPilot.exe --version`, o log, o `--diagnose` e o painel mostram a mesma versão.
- [ ] Uma tag diferente da versão do projeto faz a release falhar.

## Testes

- A própria CI roda tudo a cada push: build, `dotnet test`, `node --test` e, a partir do item 26, os testes de ponta a ponta.
- O workflow de release confere automaticamente que a tag bate com a versão do projeto.
