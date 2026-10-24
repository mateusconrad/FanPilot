# Migrar o build para projeto SDK-style (.NET Framework 4.7.2)

- **Item:** 09
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** infra
- **Esforço:** M
- **Depende de:** —
- **Decisão:** aprovada em 24/10/2026

## Problema

- O build usa o `csc.exe` que vem com o Windows, o que prende o código ao C# 5 (sem interpolação de strings, `nameof`, `?.`, pattern matching, tuplas…).
- As dependências do NuGet são baixadas, conferidas e extraídas à mão (`build.ps1:13-48`), e os redirecionamentos de assembly são gerados por script (`build.ps1:90-110`).
- Não há como usar um framework de testes nem `dotnet test`, o que trava os itens 08 e 11.

## Objetivo

Build e testes com as ferramentas padrão do .NET, sem mudar o runtime: o FanPilot continua rodando no .NET Framework que vem com o Windows, e o caminho Gigabyte continua usando .NET Remoting (`src/Program.cs:1-3`).

## Solução

1. **`src/FanPilot.csproj`** (SDK-style; os arquivos continuam em `src/`):
   - `OutputType=Exe`, `TargetFramework=net472`, `PlatformTarget=x64`, `LangVersion=latest`;
   - `ApplicationManifest=..\app.manifest`, `ApplicationIcon=..\assets\brand\fanpilot.ico`;
   - `PackageReference LibreHardwareMonitorLib 0.9.6` e `Reference System.Web.Extensions`;
   - `AutoGenerateBindingRedirects` e `GenerateBindingRedirectsOutputType`, no lugar do gerador do `build.ps1`;
   - `RestorePackagesWithLockFile`, com `packages.lock.json` versionado: o hash de cada pacote fica fixo, como o SHA-256 manual de hoje. Na CI, `--locked-mode`;
   - `wwwroot\**` copiado para a saída.
2. **GCC até o item 10:** o `build.ps1` detecta o GCC como hoje e passa `-p:GccLibDir=…`. Sem GCC, o projeto exclui `GigabyteBridge.cs` e define `NO_GIGABYTE`. O item 10 remove esse arranjo.
3. **`Directory.Build.props`** com as propriedades comuns (a versão entra no item 11).
4. **`FanPilot.sln`** com o projeto principal (os testes entram no item 08).
5. **`build.ps1`** vira um invólucro: `dotnet build -c Release` com saída em `bin\` (preservando `bin\data\`) e o mesmo parâmetro `-OutDir`. O `scripts\package.ps1` continua funcionando.
6. **Migração mecânica:** nenhuma mudança de comportamento neste item. O código é modernizado aos poucos, quando cada arquivo for mexido.
7. README, "Compilar e rodar": compilar passa a exigir o .NET SDK 8+; **rodar** continua exigindo só o Windows.
8. Os `tools/Probe*.cs` ficam fora do projeto (são scripts da engenharia reversa), com uma nota no README.
9. **Esqueleto de testes:** `tests/FanPilot.Tests` (xUnit, `net472`) entra na solução com um teste de fumaça: o assembly carrega e o `FanPilot.exe.config` gerado redireciona todas as DLLs do LibreHardwareMonitor. O item 08 preenche a cobertura.

## Riscos

- Diferenças nos redirecionamentos gerados → comparar o `FanPilot.exe.config` antigo com o novo no PR.
- `bin\` também guarda o `data\` do usuário → a limpeza do build não pode apagar nada além do que ela mesma gerou (conferir o `dotnet clean`).

## Critérios de aceite

- [ ] `.\build.ps1` gera `bin\FanPilot.exe` e um `FanPilot.exe.config` com os mesmos redirecionamentos de hoje.
- [ ] Na B760M, o roteiro de verificação em hardware dá o mesmo resultado do build antigo (log comparado).
- [ ] O exe roda num Windows sem .NET SDK.
- [ ] `scripts\package.ps1` gera um zip que funciona.
- [ ] `dotnet clean` não toca em `bin\data\`.
- [ ] `dotnet test` roda o teste de fumaça.
