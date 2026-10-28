# Linux

- **Item:** 28
- **Onda:** 5 · Plataformas
- **Tipo:** produto
- **Esforço:** G + M (três etapas: 28a, 28b e 28c)
- **Depende de:** 09, 11, 14a, 15
- **Integra com:** 13, 19, 23, 25, 26, 27

## Problema

O FanPilot só roda no Windows, e não por um detalhe: quase cada camada depende dele (levantamento de 28/10/2026).

| Onde | O que é do Windows |
|---|---|
| `src/FanPilot.csproj`, `Directory.Build.props` | `net472` e `RuntimeIdentifier` `win-x64`. O .NET Framework só existe no Windows |
| `GigabyteApi.cs`, `GigabyteBridge.cs`, `Program.cs` | .NET Remoting (só no .NET Framework), DLLs do GCC, caminho do GCC lido no registro |
| `LhmBoard.cs`, `LhmGpu.cs`, `CpuTemperature.cs`, `PawnIoSetup.cs` | LibreHardwareMonitor com o driver PawnIO, instalado pelo `winget` |
| `Config.cs` (`Json`) | `JavaScriptSerializer` do `System.Web.Extensions`, que não existe no .NET moderno |
| `DataFile.cs` (`Secrets`) | DPAPI (`ProtectedData`) para os segredos dos alertas e do MQTT |
| `MachineData.cs` | `MachineGuid` e a placa lidos do registro |
| `App.cs` | Administrador por `WindowsPrincipal`; instância única por mutex `Global\`; encerramento por `SetConsoleCtrlHandler` (`kernel32.dll`) |
| `AppOptions.cs` (`DataDirectory.Restrict`) | ACL do NTFS com SIDs de SYSTEM e Administradores |
| `FanPilotService.cs` | `ServiceBase` e o log de eventos |
| `Nvml.cs` | `nvml.dll` |
| `Diagnose.cs`, `FanPilotHost.cs`, `App.cs` | `Process.Start` de URL e do `notepad.exe` |
| `I18n.Windows` | o nome; o comportamento (`CurrentUICulture`) já serve |
| `build.ps1`, `scripts/*.ps1`, CI, e2e | PowerShell, `windows-latest`, Edge (`channel: 'msedge'`) |

O resto (servidor web, pareamento, curvas por software, alertas, histórico, integrações, painel) é .NET comum e
deve funcionar no Linux sem mudança de comportamento.

## Objetivo

O FanPilot roda num PC com Linux x64 com as mesmas funções do caminho por software (o "caminho LHM" de hoje):
sensores, curvas executadas pelo FanPilot (item 15) com a fonte combinada (item 25), GPU, alertas, histórico,
integrações, painel e pareamento, como serviço do systemd. A versão do Windows não muda.

Fica só no Windows:

- **O caminho Gigabyte:** o GCC não existe no Linux, e o IPC dele é .NET Remoting. Numa Gigabyte com Linux, o
  FanPilot usa o hwmon como em qualquer outra placa, e as curvas deixam de ficar no chip.
- **PawnIO, `winget`, bandeja (14b), instalador Inno Setup (14c), `firewall.ps1` e `autostart.ps1`:** no Linux, o
  driver é um módulo do kernel, a partida é o systemd e o firewall fica no README.

Distribuições de referência: Ubuntu 24.04 LTS e Fedora (a atual). Outras com systemd e kernel ≥ 6.1 devem funcionar.

## Arquitetura

- **Dois alvos num projeto:** `net472` (`win-x64`, como hoje) e `net10.0` (`linux-x64`). O Windows continua no
  .NET Framework por causa do Remoting; o Linux vai para o .NET 10 (LTS). O .NET 8 sai do suporte em 10/11/2026,
  e por isso este item depende da troca do SDK para o 10 já registrada nas Decisões do roadmap.
- **O que é de uma plataforma só fica fora do outro alvo pelo `.csproj`** (`<Compile Remove>` por
  `TargetFramework`), não por `#if` espalhado: `Gigabyte*.cs`, `Lhm*.cs`, `PawnIoSetup.cs` e `FanPilotService.cs`
  só no `net472`; `Hwmon*.cs` e `SystemdService.cs` só no `net10.0`.
- **O que muda de implementação passa por um ponto só:** uma classe `Platform` (`Platform.Windows.cs` e
  `Platform.Linux.cs`) com o mesmo contrato: `IsElevated`, `MachineId`, `BoardName`, `RestrictDirectory`,
  `SingleInstance`, `RegisterExitHandlers`, `OpenInBrowser`, `OpenFile`, `ProtectSecret` e `UnprotectSecret`. O
  código que já chama essas coisas passa a chamar a `Platform`.
- **`Boards.Create`:** no Windows, como hoje. No Linux, sempre `HwmonBoard`; `"backend"` aceita só `auto` ou `hwmon`
  e recusa `gigabyte` e `lhm` com o motivo.
- **`GpuMonitor.Create`:** NVIDIA pelo NVML nos dois sistemas; no Linux, AMD e Intel pelo hwmon do driver
  (`amdgpu`, `xe`, `i915`) no lugar do LibreHardwareMonitor.

## 28a · .NET 10 e a placa falsa no Linux

1. `global.json` no SDK 10; `TargetFrameworks` `net472;net10.0` no `FanPilot.csproj` e nos testes. Um
   `RuntimeIdentifier` por alvo (`win-x64` e `linux-x64`) e `packages.lock.json` com os dois. `PlatformTarget`
   continua `x64`.
2. **JSON:** o `Json` deixa de usar o `JavaScriptSerializer` e passa a usar o `System.Text.Json` nos dois alvos
   (pacote no `net472`), devolvendo os mesmos tipos de hoje (`Dictionary<string, object>`, lista, `int`, `decimal`,
   `string`, `bool`), para nada fora do `Json` mudar. Os arquivos gravados continuam lidos e saem iguais: um teste
   compara a serialização antiga e a nova com os `data\` de exemplo e as fixtures.
3. **`Platform.Linux`:**
   - `IsElevated`: `geteuid() == 0`. A mensagem `console.admin` ganha a variante do Linux ("rode com `sudo`").
   - `SingleInstance`: um arquivo em `/run/fanpilot/fanpilot.lock` (`/run/user/<uid>/` para a placa falsa sem root)
     aberto com `FileShare.None` e mantido aberto. `IsRunning` tenta abri-lo. Um arquivo com o PID de um processo
     que não existe mais é o equivalente do `AbandonedMutexException` e gera o mesmo aviso.
   - `RegisterExitHandlers`: `PosixSignalRegistration` para `SIGINT` e `SIGQUIT` (o equivalente do Ctrl+C: avisa o
     `Main`), `SIGTERM` e `SIGHUP` (limpeza na hora, como o close/logoff/shutdown do Windows). `ProcessExit` e
     `UnhandledException` como hoje. `SIGKILL` não se intercepta, como o "Finalizar tarefa".
   - `MachineId`: `/etc/machine-id`. `BoardName`: `/sys/class/dmi/id/board_vendor` e `board_name`. O
     `machine.json` de um dual boot difere entre os sistemas; não importa, porque cada um tem a própria pasta de dados.
   - `RestrictDirectory`: dono root, modo `0700`.
   - `OpenInBrowser` e `OpenFile`: no Linux não abrem nada (o FanPilot roda como root, e abrir um navegador como root
     é o que não se quer); escrevem o endereço ou o caminho no console.
   - `ProtectSecret` e `UnprotectSecret`: AES-GCM com uma chave de 32 bytes em `/etc/fanpilot/secret.key` (root,
     `0600`), criada no primeiro uso e **fora** da pasta de dados, para uma cópia da pasta não levar os segredos,
     como o DPAPI garante hoje. O prefixo passa a ser `aesgcm:`; o `dpapi:` continua lido no Windows. Sem a chave,
     o segredo conta como ilegível, como já acontece com o DPAPI de outro PC.
4. `I18n.Windows` passa a se chamar `I18n.System` (só o nome). No Linux, a `CurrentUICulture` vem do `LANG`. O
   pacote leva o ICU como dependência (`libicu`, presente nas distribuições de referência); sem ele o .NET falha na
   partida, e o README diz como instalá-lo.
5. Com isso, `--fake-board`, `--version`, `--data` e `--import-data` funcionam no Linux, e os testes que não tocam
   o Windows passam no `net10.0`. Os que tocam (`GigabyteApi`, ACL, serviço do Windows, DPAPI) ficam só no `net472`
   ou viram `SkippableFact` por sistema.

### Andamento da 28a (28/10/2026)

Feita. No Windows nada muda: 301 testes no `net472` (os 294 de antes e os 7 do `JsonTests`), o e2e no Edge e o zip
iguais (48 arquivos, nada do `net10.0`). No Linux (container `dotnet/sdk:10.0`), restore travado, build, 292 testes
como root, 289 como usuário comum (os 3 que pedem Administrador ficam de fora, como num PC) e a placa falsa de ponta a
ponta: `/pair` e `/api/fans` respondem, uma segunda instância diz que já está rodando e o `SIGTERM` para limpo.

O que mudou em relação ao plano:

- **JSON sem pacote novo.** Em vez do `System.Text.Json` (que traria dependências e binding redirects para o `net472`,
  ao lado das DLLs do GCC), o `Json` (`src/Json.cs`) ganhou leitor e escritor próprios, iguais nos dois alvos. Eles
  reproduzem o `JavaScriptSerializer`: `int`, `long`, `decimal` e, com expoente, `double`; `object[]`; o mesmo escape
  (`<`, `>`, `&`, `'`, U+2028); números no formato `"r"`; `NaN` escrito como antes. O `JsonTests` compara os dois no
  Windows, inclusive com as fixtures e os dicionários do painel. JSON inválido vira `FormatException` (400 no servidor).
- **Arquivos por sistema** em vez de uma classe `Platform` com tudo: `Platform`, `Boards`, `SingleInstance`, `Secrets`,
  `GpuMonitor.Create`, `Diagnose.Run` e `FanPilotService` têm um `*.Windows.cs` e um `*.Linux.cs` com os mesmos membros,
  e o `.csproj` escolhe pelo alvo. O GCC saiu do `Program.cs` para o `GigabyteInstall.cs`; o `CpuRoles` saiu do
  `CpuTemperature.cs` (só Windows) para o `CpuRoles.cs`. `I18n.Windows` virou `I18n.SystemLanguage` (`System` esconderia
  o namespace dentro da classe). O `FanPilotHost` fala com o `IFollowsGpu` em vez do `LhmBoard`.
- **Instância única sem PID.** O arquivo `/run/fanpilot/fanpilot.lock` (com `FileShare.None`, um `flock`) cai com o
  processo, morto ou não, então não há o que limpar; e um PID gravado acusaria "não terminou limpo" depois de toda saída
  normal. Fica como no Windows, onde o mutex some com o processo.
- **"Só este PC" no Linux.** O `HttpListener` do .NET no Linux escuta `localhost` só no primeiro endereço que o nome
  resolve (em geral o `::1`), e o `127.0.0.1` era recusado. Lá o FanPilot sem root escuta em todos os endereços IPv4
  (`Platform.LocalhostPrefixHost`) e o `WebServer` responde 403, sem corpo, a quem não vem do próprio PC; o listener do
  Linux não consegue derrubar a conexão sem responder. No Windows, nada muda.
- **Segredos:** a chave fica em `/etc/fanpilot/secret.key` como root e em `~/.config/fanpilot/secret.key` para a placa
  falsa e os testes, sempre `0600` numa pasta `0700`.
- **Lock files:** o `net10.0` restaura sempre para `linux-x64` (`RuntimeIdentifiers`) e sem apphost no build portátil,
  para o `packages.lock.json` sair igual no Windows e no Linux; o build portátil roda com `dotnet FanPilot.dll`. O
  `build.ps1` compila só o `net472`, para o zip do Windows não levar o build do Linux.
- **A placa real no Linux ainda não existe:** o FanPilot sobe, diz que a placa vem com a 28b (`startup.linuxBoard`), não
  toca em nenhum fan e sai com o código 3; o `--diagnose` avisa o mesmo e sai com o 8. A GPU NVIDIA espera o nome do
  `libnvidia-ml.so.1` (28b) e aparece como indisponível.
- **CI:** o job `linux` (`ubuntu-latest`) faz restore travado, build do `net10.0`, `--version`, os testes C# e os do
  painel. O e2e no Linux fica para a 28c, junto com o pacote. O `dotnet test` no Windows deixa o `net10.0` de fora.
- Ficou para depois: o `WebRequest` dos alertas é obsoleto no .NET 10 (aviso SYSLIB0014), mas funciona; trocar pelo
  `HttpClient` pode vir com a 28b.

## 28b · Placa real: hwmon e GPU

1. **`HwmonBoard : IBoard`**, com `HardwareCurves = false` e as curvas pelo `SoftwareCurveEngine`, como o `LhmBoard`.
   A raiz (`/sys/class/hwmon`) é injetável, para os testes rodarem num sysfs falso.
   - **Descoberta:** cada `hwmonN` com `name`. Fans e PWMs vêm só dos chips de placa-mãe (`nct6775`, `nct6683`,
     `it87`, `asus_ec_sensors`, `gigabyte_wmi`, `dell_smm` e semelhantes); GPU, NVMe e bateria ficam de fora, e a lista
     fica no código, com o `--diagnose` mostrando o que foi ignorado.
   - **Sensores:** `tempN_input` e `tempN_label`, `fanN_input`, `inN_input`, `powerN_*`. Temperatura da CPU pelo
     `k10temp` (`Tctl`, `Tccd*`) e pelo `coretemp` (`Package id 0`), com os papéis `cpu-main` e `cpu-socket` do item 19.
   - **Chave estável** (item 4): `hwmonN` muda entre partidas. A chave é o caminho real do `device` (ex.:
     `platform/nct6775.656`) com o `name` e o índice do PWM, nunca o `N`.
   - **Controle:** `pwmN_enable = 1` (manual) e `pwmN` em 0..255, a escala que o `FanCurve` já usa. Na primeira vez
     que um header é tomado, o `pwmN_enable` original é guardado; "voltar à BIOS" (`FanCurve.BiosMode`, saída e falha)
     grava esse valor de volta. Um `pwmN_enable` que não aceita `1` deixa o header só de leitura, com o motivo.
   - **Sem controle:** módulo ausente, ou chip ocupado pela ACPI ("resource conflict" no `dmesg`, comum no `nct6775`).
     O `Notice` diz o que fazer: o módulo a carregar e o `acpi_enforce_resources=lax` quando for o caso. O FanPilot não
     carrega módulos nem mexe no boot sozinho, como já não instala o PawnIO sem perguntar.
   - **Morte sem limpeza (`SIGKILL`, queda do runtime):** os `pwmN_enable` originais também vão para
     `/run/fanpilot/hwmon-restore.json`. `FanPilot --restore-fans` lê esse arquivo e devolve os headers; o
     `ExecStopPost` da unit (28c) o chama, o que o Windows não tem.
2. **NVIDIA:** o `Nvml.cs` não muda de assinatura; um `NativeLibrary.SetDllImportResolver` resolve `nvml.dll` para
   `libnvidia-ml.so.1` no Linux. Mudar a velocidade do fan pelo NVML exige root, e o FanPilot já roda como root.
   Conferir num driver atual se funciona sem servidor gráfico (o serviço sobe antes dele).
3. **AMD e Intel:** um `HwmonGpu : GpuMonitor` no hwmon do `amdgpu` (`temp1_input` edge, `temp2_input` junction,
   `pwm1` e `pwm1_enable`, `power1_average`, `freq1_input`) e, só leitura, do `xe` e do `i915`. Mesma proteção da GPU
   (item 2) e mesma volta ao automático na saída.
4. **`--diagnose` no Linux:** lista cada `hwmonN` (nome, `device`, arquivos `temp*`, `fan*` e `pwm*`, com o
   `pwmN_enable` atual), os módulos carregados (`/proc/modules`) que interessam, e se o kernel registrou um conflito
   da ACPI. Sem números de série, como hoje (item 13). Um `--diagnose` de PC com Linux é o que se pede num relato de
   compatibilidade.
5. **`--hardware-test`** não muda: ele fala com o `IBoard`. O relatório vai para `<data>/hwtest-<data>.md`.
6. **Ponto de parada:** antes da 28c, rodar o `--hardware-test` num PC de verdade com Linux. Na B760M (IT8689E), a
   placa de referência, o `it87` do kernel talvez não reconheça o chip (conferir; há um `it87` fora da árvore que o
   suporta). Se não reconhecer, o ponto de parada precisa de outra placa (uma `nct67xx` de qualquer marca serve), e o
   README diz isso.

## 28c · Serviço do systemd, pacote e CI

1. **`--service` no Linux:** o mesmo `FanPilotHost` sem console, parando no `SIGTERM`. O log continua em
   `<data>/fanpilot.log`; avisos e erros também vão para a saída padrão, que o journal guarda (o equivalente do log
   de eventos). Volta da suspensão: um `systemd-inhibit`, ou um sinal do logind pelo D-Bus, chama o watchdog na hora,
   como o `OnPowerEvent`. O mais simples é o watchdog, que já reaplica em segundos; o ponto de parada confere se
   isso basta.
2. **`packaging/linux/fanpilot.service`:**
   ```ini
   [Unit]
   Description=FanPilot (temperaturas e fans pelo celular)
   After=network-online.target
   Wants=network-online.target

   [Service]
   ExecStart=/opt/fanpilot/FanPilot --service --data /var/lib/fanpilot
   ExecStopPost=/opt/fanpilot/FanPilot --restore-fans
   Restart=on-failure
   RestartSec=1
   TimeoutStopSec=10
   # O FanPilot escreve no /sys: sem ProtectKernelTunables, que deixa o /sys só de leitura.
   ProtectSystem=full
   ProtectHome=true
   PrivateTmp=true
   NoNewPrivileges=true

   [Install]
   WantedBy=multi-user.target
   ```
3. **Pacote:** `scripts/package.sh` gera `dist/FanPilot-<versão>-linux-x64.tar.gz` com o executável publicado
   self-contained e em arquivo único (`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true`), sem o
   .NET instalado no PC, mais o `wwwroot/`, a unit e um `install.sh`. O `install.sh` copia para `/opt/fanpilot`,
   instala a unit, importa uma pasta de dados existente (`--import-data`), habilita e inicia o serviço, e diz como abrir
   a porta no `ufw` e no `firewalld`, só para a rede local. `.deb` e `.rpm` ficam para depois, se houver pedido.
4. **CI:** um job `linux` no `ubuntu-latest`, ao lado do de hoje: restore em locked mode, build do `net10.0`,
   `--version`, testes C# do `net10.0`, testes do painel (`node --test`), pacote e e2e no Chromium do Playwright
   contra a placa falsa, com o smoke do `.tar.gz`. O `service.spec.mjs` ganha uma versão com o systemd num container
   ou fica só no Windows (decidir na implementação). A release (`release.yml`) publica o `.tar.gz` e o inclui no
   `SHA256SUMS.txt`.
5. **Documentação:** README e `README.pt-BR.md` com uma seção "Linux": requisitos (kernel, módulo do chip, ICU),
   instalação, `sudo ./FanPilot` para rodar no terminal, o serviço, o firewall e o que não existe no Linux. O
   `TESTING-OTHER-BOARDS.md` e o `TESTANDO-OUTRAS-PLACAS.md` explicam o `--diagnose` no Linux; o formulário de
   compatibilidade (item 13) ganha o campo "Sistema".

## Critérios de aceite

- [ ] No Windows, nada muda: os testes de hoje passam no `net472`, o zip é o mesmo e o `--hardware-test` passa na B760M.
- [ ] No Linux, `--version`, `--fake-board`, `--data` e `--import-data` funcionam; os testes C# do `net10.0`, os do
  painel e os e2e passam na CI do Ubuntu.
- [ ] Os arquivos de dados gravados por uma versão anterior são lidos sem mudança (JSON, segredos `dpapi:` no Windows).
- [ ] Numa placa com `nct67xx` ou `it87` reconhecido pelo kernel: sensores e fans no painel, uma curva com a resposta
  Equilibrada controla o fan, e "Restaurar original", a saída por `SIGTERM` e o `kill -9` seguido do `--restore-fans`
  devolvem o `pwmN_enable` original.
- [ ] Chip sem módulo ou com conflito da ACPI: o painel mostra o aviso com o que fazer, e nenhum fan é tocado.
- [ ] GPU NVIDIA pelo `libnvidia-ml.so.1` e AMD pelo `amdgpu`: temperatura, fan manual e volta ao automático na saída.
- [ ] Como serviço do systemd: sobe no boot, para em menos de 1 s, é reiniciado depois de morto, e o celular pareado
  continua funcionando.
- [ ] Um segredo gravado no Linux não é lido com a pasta de dados copiada para outro PC.

## Testes

- Automatizados: `HwmonBoardTests` num sysfs falso montado em pasta temporária, com fixtures copiadas de placas reais
  (`nct6798`, `it8689`, `k10temp`, `coretemp`, `amdgpu`): descoberta, chave estável com o `N` trocado, leitura,
  tomada e devolução do `pwmN_enable`, `--restore-fans`, módulo ausente. `JsonTests` comparando a serialização
  antiga e a nova. `PlatformTests` para o arquivo de instância única, a chave dos segredos e os sinais (um processo
  filho recebendo `SIGTERM`). Os de hoje rodando nos dois alvos.
- Ponta a ponta: os mesmos de hoje no Chromium do Ubuntu, com a placa falsa e o smoke do `.tar.gz`.
- Hardware: o `--hardware-test` num PC com Linux (o ponto de parada da 28b), com o relatório anexado na issue.
