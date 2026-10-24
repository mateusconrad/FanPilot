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

Cada PR é compilado e testado automaticamente, e cada tag gera uma release versionada, com checksum e notas.

## Solução

1. **Versão semântica** num lugar só (`Directory.Build.props`), começando em `0.1.0`, com versão informativa que inclui o commit (`0.1.0+abc1234`). Ela aparece em:
   - `FanPilot.exe --version`, na primeira linha do log e no cabeçalho do `--diagnose`;
   - `/api/info` (`version`, `backend`, `board`) e no rodapé do painel;
   - no nome do zip: `FanPilot-0.1.0-win-x64.zip`.
2. **`CHANGELOG.md`** no formato Keep a Changelog. Todo PR que muda algo visível atualiza a seção "Não lançado".
3. **`.github/workflows/ci.yml`** (push e PR na `main`, `windows-latest`): checkout → setup-dotnet 8 → `dotnet restore --locked-mode` → build Release → `dotnet test` → setup-node → `node --test` → testes de ponta a ponta (item 26, quando existir) → empacota e publica o zip como artefato do PR.
4. **`.github/workflows/release.yml`** (tag `v*.*.*`): confere que a tag bate com a versão → build e testes → zip → `SHA256SUMS.txt` → assinatura (item 12) → `gh release create`, com as notas tiradas da seção do CHANGELOG. Versões `0.x` saem como pre-release.
5. **`.github/dependabot.yml`**: NuGet e GitHub Actions, toda semana. Uma atualização do LibreHardwareMonitor só entra depois do roteiro de verificação em hardware (nota no template de PR).
6. **Template de PR** com checklist: testes, CHANGELOG e roteiro de hardware quando mexer em fans ou GPU.
7. **Proteção da branch `main`** exigindo a CI. A configuração é feita no GitHub pelo dono do repositório (passo documentado).

## Critérios de aceite

- [ ] Um PR mostra o check da CI e o zip como artefato.
- [ ] A tag `v0.1.0` gera uma release com o zip, o `SHA256SUMS.txt` e as notas.
- [ ] `FanPilot.exe --version`, o log, o `--diagnose` e o painel mostram a mesma versão.
- [ ] Uma tag diferente da versão do projeto faz a release falhar.

## Testes

- A própria CI roda tudo em cada PR: build, `dotnet test`, `node --test` e, a partir do item 26, os testes de ponta a ponta.
- O workflow de release confere automaticamente que a tag bate com a versão do projeto.
