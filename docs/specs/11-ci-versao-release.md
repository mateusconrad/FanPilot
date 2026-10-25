# CI, versionamento e releases

- **Item:** 11
- **Onda:** 2 · Entrega
- **Tipo:** infra
- **Esforço:** P–M
- **Depende de:** 08, 09, 10

## Problema

- Não há CI: nada compila nem testa cada mudança.
- Não há versão: o exe é sempre `1.0.0.0`, e o zip leva data e commit no nome (`FanPilot-20261024-24c22f1.zip`). Um bug report não diz qual versão a pessoa usa.
- A release é feita à mão, num PC específico.

## Objetivo

Cada push na `main` é compilado e testado automaticamente, e cada tag gera uma release versionada, com checksum e notas.

## Solução

1. **Versão semântica** num lugar só (`VersionPrefix` no `Directory.Build.props`), começando em `0.1.0`. O SDK acrescenta o commit à versão informativa, e `src/AppVersion.cs` a encurta para 7 caracteres (`0.1.0+1a2b3c4`; um build sem git mostra só `0.1.0`). Ela aparece em:
   - `FanPilot.exe --version`, na primeira linha do log, no cabeçalho do `--diagnose` e no relatório do `--hardware-test`;
   - `GET /api/info` (`version`, `backend`, `board`; exige pareamento como o resto da API) e no rodapé do painel;
   - no nome do zip: `FanPilot-0.1.0-win-x64.zip` (o `scripts/package.ps1` lê a versão do exe compilado).
2. **`CHANGELOG.md`** no formato Keep a Changelog, em português, com a seção **Não lançado**. Todo commit que muda algo visível acrescenta uma linha nela; na release, ela ganha o número e a data. O zip leva o CHANGELOG.
3. **`.github/workflows/ci.yml`** (push na `main`, pull requests, como os do Dependabot, e manual), `windows-latest`: checkout → setup-dotnet pelo `global.json` → setup-node 22 → checagem de encoding → `dotnet restore --locked-mode` → build Release → `FanPilot.exe --version` conferido contra o `Directory.Build.props` e o commit → `dotnet test` → `node --test` → empacota e publica o zip como artefato da execução (upload direto, sem zip dentro de zip) → testes de ponta a ponta e smoke do zip (item 26), com o relatório e os screenshots também como artefatos.
4. **`.github/workflows/release.yml`** (tag `v*.*.*`): antes de compilar, confere que a tag bate com a versão e que o CHANGELOG tem a seção dela (`scripts/release-notes.ps1`) → build e testes → zip → testes de ponta a ponta e smoke do zip (item 26) → `SHA256SUMS.txt` (formato do `sha256sum`, com LF) → assinatura (item 12) → `gh release create`, com as notas tiradas do CHANGELOG. Versões `0.x` saem como pre-release. Rodado à mão (`gh workflow run release.yml`) é um **ensaio**: os mesmos passos, as notas tiradas de "Não lançado" e os arquivos guardados como artefato, sem publicar nada.
5. **`.github/dependabot.yml`**: NuGet e GitHub Actions, toda semana; a CI roda em cada PR dele. Uma atualização do LibreHardwareMonitor só entra depois do roteiro de verificação em hardware (regra no `docs/ROADMAP.md`).
6. **Build vermelho na `main` tem prioridade:** o trabalho é direto na `main` (decisão de 24/10/2026), então a CI roda depois do push, sem PR para bloquear. Uma falha é corrigida antes do próximo item.
7. **Checagem de encoding** (`scripts/check-encoding.ps1`): a CI falha se algum `.ps1` com caractere não-ASCII estiver sem BOM (o Windows PowerShell 5.1 leria o arquivo como ANSI e quebraria os acentos, como aconteceu antes do item 09).
8. **Build reprodutível entre o PC e a CI:**
   - o `global.json` fixa o SDK 8.0 (a feature band mais nova instalada): a imagem do runner também tem o SDK 10, que seria usado sem ele, com outro C#;
   - o `Directory.Build.props` referencia `Microsoft.NETFramework.ReferenceAssemblies` explicitamente: o SDK só adiciona esse pacote onde o targeting pack do .NET Framework falta (um PC sem Visual Studio) e não onde ele está instalado (o runner), e os `packages.lock.json` divergiam.
9. **Zip com `/`:** o `package.ps1` monta o zip com `ZipArchive`, porque o `Compress-Archive` do Windows PowerShell 5.1 grava as entradas com `\`.

## Limitações

- `FanPilot.exe --version` precisa de um terminal como Administrador: o manifesto exige elevação, e a janela elevada aberta a partir de um terminal comum fecha antes de dar para ler. A versão também está no rodapé do painel, no log e em Propriedades › Detalhes do exe. O item 14 (serviço e bandeja) pode rever isso.
- O suporte do SDK 8 termina em 10/11/2026. O FanPilot roda no .NET Framework 4.7.2, que não muda; trocar para o SDK 10 é atualizar o `global.json` e instalar o SDK 10 no PC que compila.

## Critérios de aceite

- [x] Cada push na `main` mostra o check da CI e o zip como artefato.
- [x] Um `.ps1` com acento e sem BOM faz a CI falhar (o `check-encoding.ps1` sai com 1 e lista o arquivo).
- [ ] A tag `v0.1.0` gera uma release com o zip, o `SHA256SUMS.txt` e as notas. O ensaio do workflow passa; a tag espera a verificação em hardware dos itens pendentes.
- [x] `FanPilot.exe --version`, o log, o `--diagnose` e o painel mostram a mesma versão (todos usam `AppVersion.Text`; a CI confere o `--version`).
- [x] Uma tag diferente da versão do projeto faz a release falhar (antes de compilar).

## Testes

- A própria CI roda tudo a cada push: build, `--version`, `dotnet test` (como Administrador, então os testes que exigem elevação também rodam), `node --test` e, a partir do item 26, os testes de ponta a ponta.
- `AppVersionTests`: a versão é a do `Directory.Build.props` mais o commit, encurtado para 7 caracteres.
- `WebServerTests.InfoTellsTheVersionAndTheBackend`: `/api/info` devolve a versão e o backend.
- O workflow de release confere automaticamente que a tag bate com a versão do projeto e que o CHANGELOG tem a seção dela.
