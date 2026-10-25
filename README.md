<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/banner-dark.png">
    <img alt="FanPilot — temperaturas e fans da sua placa-mãe, no celular" src="assets/brand/banner-light.png" width="560">
  </picture>
</p>

# FanPilot

Monitor de temperatura e controle de fans pelo celular.

Painel web servido pelo próprio PC: temperaturas de CPU/placa-mãe/GPU, potência, clocks, gráfico dos últimos
10 minutos, curvas de fan da placa-mãe (editor com arrastar pontos) e fan da GPU. Acesso pela LAN só
depois de parear o celular escaneando um QR code exibido no PC.

Como funciona por baixo: veja [docs/REVERSE_ENGINEERING.md](docs/REVERSE_ENGINEERING.md).

> **Aviso:** projeto independente, **sem relação com GIGABYTE, ASUS, NVIDIA** ou outros fabricantes, e não
> endossado por eles. "GIGABYTE", "Control Center", "EasyTune", "ASUS" e "NVIDIA" são marcas de seus respectivos
> donos. O FanPilot não distribui arquivos da Gigabyte: usa o GCC já instalado no seu PC. Ele altera o
> comportamento dos fans do seu hardware;
> **use por sua conta e risco** (veja as [proteções](#proteções-de-hardware)). Sem garantia, conforme a
> [licença](LICENSE).

## Requisitos

- Windows 10/11 64-bit (o .NET Framework 4.x necessário já vem no Windows).
- Executar como **Administrador** (o `.exe` já pede elevação).
- Uma das duas formas de acesso à placa-mãe (o FanPilot escolhe sozinho):
  - **Placa Gigabyte com o Gigabyte Control Center** instalado e o serviço **EasyTune Engine Service** rodando.
    As curvas ficam gravadas no chip da placa. O FanPilot usa as DLLs do GCC já instalado, não as redistribui.
    Opcional: com o driver [PawnIO](https://pawnio.eu) instalado, o card da CPU mostra a temperatura dos núcleos
    (lida pelo LibreHardwareMonitor só na CPU, sem disputar o chip com o GCC); sem ele, mostra o sensor do socket.
  - **Qualquer outra placa (ASUS, MSI, ASRock… ou Gigabyte sem GCC)** via
    [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), que precisa do driver
    **[PawnIO](https://pawnio.eu)**. Se ele faltar, o FanPilot oferece instalar na hora pelo `winget` (é só
    responder `S` na janela). Nessas placas o FanPilot executa as curvas (veja
    [Placas sem GCC](#placas-sem-gcc-curvas-executadas-pelo-fanpilot)). **Em teste.**
- GPU: NVIDIA pelo `nvml.dll` do driver; AMD, Intel Arc (e NVIDIA sem NVML) pelo LibreHardwareMonitor. Sem GPU
  dedicada, o resto funciona.

## Compilar e rodar

```powershell
.\build.ps1                     # .NET SDK 8; pacotes do NuGet fixados (com hash) pelo src\packages.lock.json
dotnet test                     # testes automatizados (sem hardware nem Administrador)
node --test "tests/js/*.test.mjs"  # testes do painel (Node 22+)
dotnet build src\FanPilot.csproj -c Debug; cd tests\e2e; npm ci; npx playwright test   # ponta a ponta, no Edge
.\bin\FanPilot.exe              # pede UAC, abre http://localhost:8787/pair
.\scripts\firewall.ps1          # (admin, uma vez) libera a porta 8787 no perfil de rede Privado
.\scripts\autostart.ps1         # (admin, opcional) inicia elevado no logon, sem UAC
.\scripts\package.ps1           # gera dist\FanPilot-<versão>-win-x64.zip para distribuir (sem data\)
.\bin\FanPilot.exe --version    # versão e commit, ex.: FanPilot 0.1.0+1a2b3c4
.\bin\FanPilot.exe --diagnose   # relatório só de leitura da placa (para placas ainda não testadas)
.\bin\FanPilot.exe --hardware-test [--fan N]   # (FanPilot fechado) roteiro de verificação na placa real, ~2 min
.\bin\FanPilot.exe --data <pasta>              # dados em outra pasta (padrão: data\ ao lado do exe)
.\bin\FanPilot.exe --service --data <pasta>    # modo serviço do Windows; o instalador (em preparação) o registra
.\bin\FanPilot.exe --import-data <pasta antiga> --data <pasta>   # (FanPilot fechado) traz curvas, pareamentos e nomes
```

Compilar exige o [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0): o `global.json` fixa a versão, para
este PC e a CI compilarem igual (com outro SDK, o lock file e a versão do C# mudariam). Para **rodar**, basta o Windows: o
FanPilot usa o .NET Framework 4.7.2 que já vem nele.

O build não depende do Gigabyte Control Center: o FanPilot carrega as DLLs do GCC instalado só na hora de rodar, então
um mesmo build (ou zip) atende as placas Gigabyte e as outras.

O `--hardware-test` passa por velocidade fixa, uma curva vigiada pelo watchdog, Turbo, o fan da GPU e um "fechar e
abrir de novo", e grava `data\hwtest-<data>.md`. Ele só mexe no fan escolhido (e em todos no Turbo, a 100%), nunca
abaixo de 40%, recusa o fan da CPU e as bombas (pelo nome ou marcadas no painel), para se a CPU ou a GPU passarem
de 80 °C e devolve tudo como estava,
inclusive com Ctrl+C (fechar a janela no meio é a única saída que ele não intercepta). Código de saída: 0 passou,
6 falhou, 7 interrompido, 8 não executado.

Builds Debug aceitam `--fake-board`: o FanPilot inteiro roda com uma placa e uma GPU falsas, guardando os dados em
`data-fake\` (nunca no `data\` da placa real). `--fake-board=<cenário>` escolhe a placa: `demo` (o padrão, a B760M),
`worst` (8 fans, nomes longos, sensor desconectado), `unsupported` (sem controle de fans nem GPU), `down` (serviço da
placa fora do ar), `lhm` (curvas executadas pelo FanPilot) e `slow` (demora 4 s para abrir, como o GCC às vezes). A
placa falsa não precisa de Administrador: sem ele, o painel só abre em `localhost`. Para mexer só no visual, use o
`tools/ui-mock`.

Os testes de ponta a ponta (`tests/e2e`, Playwright) abrem o painel no Edge contra esse FanPilot, um cenário de cada
vez: dados, editor de curvas (arraste com as restrições, predefinições, aplicar, restaurar), Turbo, GPU, reconexão do
painel quando o FanPilot reinicia, acessibilidade (axe-core) e screenshots em 320, 390 e 1024 px nos dois temas. Sem
Administrador, os testes que fazem o papel do celular na rede são pulados. A CI roda tudo como Administrador, mais o
smoke do zip da release (o exe do zip sobe, responde, `--version` e `--diagnose`), e guarda o relatório e os
screenshots como artefatos de cada execução.

No celular (mesma rede Wi-Fi): escaneie o QR da página `/pair`. O código vale ~3 min e é de uso único;
o aparelho fica pareado por 30 dias de inatividade. A página `/pair` (só acessível no próprio PC) lista e
despareia aparelhos.

## Versões e releases

- **Baixar:** cada versão sai na página [Releases](https://github.com/mateusconrad/FanPilot/releases) como
  `FanPilot-<versão>-win-x64.zip`, com um `SHA256SUMS.txt` para conferir o download
  (`Get-FileHash .\FanPilot-0.1.0-win-x64.zip` deve dar o mesmo hash). Versões `0.x` saem como pre-release.
- **Qual versão está rodando:** no rodapé do painel, na primeira linha do log, no `--diagnose` e em
  `FanPilot.exe --version` (num terminal como Administrador: o exe pede elevação e, fora dele, a janela fecha antes de
  dar para ler). O número vem do `Directory.Build.props` e o sufixo é o commit do build.
- **O que mudou:** [CHANGELOG.md](CHANGELOG.md).
- **CI:** cada push na `main` compila, roda os testes e guarda o zip como artefato da execução
  ([Actions](https://github.com/mateusconrad/FanPilot/actions)).
- **Lançar uma versão:** ajuste `VersionPrefix` no `Directory.Build.props`, troque `## [Não lançado]` no
  `CHANGELOG.md` por `## [<versão>] - <data>`, faça o commit e crie a tag: `git tag v0.1.0; git push origin v0.1.0`.
  O workflow `Release` confere que a tag bate com a versão, compila, testa e publica a release. Para ensaiar sem
  publicar nada: `gh workflow run release.yml` (os arquivos ficam como artefato da execução).

## Por que o Windows avisa ao abrir

O `FanPilot.exe` ainda não é assinado: a assinatura gratuita para projetos open source está sendo pedida ao
[SignPath Foundation](https://signpath.org). Um programa novo, sem editor verificável, que pede Administrador e fala
com o hardware é o perfil que o SmartScreen ("O Windows protegeu o computador") e alguns antivírus barram.

Para conferir que o zip é exatamente o publicado aqui (gerado pela CI a partir deste código, nunca num PC pessoal):

1. Baixe o zip e o `SHA256SUMS.txt` da mesma [release](https://github.com/mateusconrad/FanPilot/releases).
2. No PowerShell, na pasta do download:

   ```powershell
   (Get-FileHash .\FanPilot-0.1.0-win-x64.zip).Hash -eq (-split (Get-Content .\SHA256SUMS.txt))[0]
   ```

   `True` quer dizer que o arquivo é o da release. `False`: não use, baixe de novo.
3. Com o hash conferido, no aviso do SmartScreen: "Mais informações" › "Executar assim mesmo".

## Segurança

- Pareamento por QR: código aleatório de 144 bits, uso único, expira; tentativas erradas limitadas por IP.
- Sessão: cookie `HttpOnly; SameSite=Strict` com token de 256 bits; no disco só o SHA-256 (`bin\data\sessions.json`).
- `/pair` e `/api/pair/*` só respondem para `localhost`; requisições do próprio PC endereçadas a `localhost` ou
  `127.0.0.1` são consideradas confiáveis.
- Só são atendidas requisições endereçadas a este PC: o header `Host` precisa ser `localhost`, um IP das interfaces
  de rede, o nome do PC ou o `publicHost`, na porta configurada (o resto recebe `421`). Isso impede que uma página
  web alcance a API local por DNS rebinding. Para acessar por outro nome (ex.: o DNS do roteador), use `publicHost`.
- POSTs exigem o header `X-FanPilot` e `Origin` igual ao host (bloqueia CSRF). CSP restringe scripts a `'self'`.
- **Limitação:** é HTTP puro na LAN. Quem estiver na mesma rede e capturar o tráfego pode copiar o cookie.
  Use só em rede doméstica confiável (o script de firewall abre a porta apenas no perfil Privado).

## Compatibilidade

| Recurso | Gigabyte com GCC | Outras placas (LibreHardwareMonitor + PawnIO) |
|---|---|---|
| Monitoramento (temperaturas, RPM, tensões, clocks, potência) | Placas suportadas pelo GCC | Placas cujo chip de sensores o LibreHardwareMonitor reconhece (Nuvoton, ITE, Fintek…) |
| Controle de fans | Placas em que o GCC usa o caminho de "3ª geração" | Headers que o LibreHardwareMonitor consegue controlar |
| Onde a curva roda | No chip (continua sem o FanPilot) | No FanPilot (ao fechar, os fans voltam à BIOS) |
| Fan e sensores da GPU | NVIDIA (NVML); AMD e Intel Arc via LibreHardwareMonitor | Igual |

Testado em: **B760M GAMING X AX** (GCC 26.03.26.01). ASUS e outras placas: **em teste**, ainda sem placa
confirmada. A lista das placas relatadas está em [docs/COMPATIBILIDADE.md](docs/COMPATIBILIDADE.md). Se testar,
siga [docs/TESTANDO-OUTRAS-PLACAS.md](docs/TESTANDO-OUTRAS-PLACAS.md) e relate pelo
[formulário de compatibilidade](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml):
o arquivo do `--diagnose` traz o link já preenchido e sai sem números de série nem UUIDs.

Para forçar um caminho, use `"backend"` no `config.json`: `"auto"` (padrão: GCC se instalado, senão
LibreHardwareMonitor), `"gigabyte"` ou `"lhm"`. Se uma atualização do GCC mudar a API interna que o FanPilot usa,
ele avisa no painel o que mudou e segue com o LibreHardwareMonitor (com `"gigabyte"`, ele para com a mensagem). O
`--diagnose` mostra a versão do GCC e se ela é compatível.

## Proteções de hardware

- **Gigabyte com GCC:** as curvas rodam **no chip ITE** — continuam valendo se o FanPilot fechar.
  Nas outras placas, veja [Placas sem GCC](#placas-sem-gcc-curvas-executadas-pelo-fanpilot).
- Toda curva: o mesmo número de pontos que a placa usa (8 na B760M), temperaturas crescentes, PWM nunca diminui com a temperatura, mínimo de 20%,
  100% obrigatório no último ponto (≤ 90 °C). O ponto 0 fica em 0 °C (o chip nunca desliga o fan).
- Modo "Fixo": velocidade constante, mas o chip ainda vai a 100% acima de 85 °C.
- "Restaurar original" volta à curva salva no primeiro uso (`bin\data\backup-curves.json`).
- Watchdog (15 s) reaplica sua curva se algo a sobrescrever (resume do sleep, GCC, BIOS).
  Se quiser voltar a usar o GCC para fans, clique em "Restaurar original" antes.
- **Turbo** (botão no card dos fans): coloca todos os fans da placa-mãe em 100% sem tocar no fan da GPU.
  Enquanto ligado, a edição individual fica bloqueada; ao desligar, cada fan volta exatamente ao que estava
  (curva/fixo do FanPilot ou curva original). O estado sobrevive a um reinício (`bin\data\turbo.json`).
- **Perfis** (seletor no card dos fans): **Silencioso**, **Padrão** e **Performance** põem a curva da predefinição em
  todos os fans de uma vez (menos as bombas), no sensor de referência que cada um já usa; **Personalizado** devolve
  exatamente a sua configuração de antes. O Turbo funciona por cima de qualquer modo e, ao desligar, volta a ele.
  Editar um fan com um perfil ativo desliga o perfil: os outros fans ficam com a curva dele como configuração
  pessoal (o editor avisa antes). O modo sobrevive a um reinício (`bin\data\global-mode.json`, que substituiu o
  `turbo.json`).
- Fan da GPU em modo fixo volta sozinho para automático se a GPU chegar a 83 °C ou quando o FanPilot fecha.
- **Bomba do watercooler:** um header com "pump", "bomba" ou "AIO" no nome já aparece como bomba, e qualquer header
  pode ser marcado assim no editor ("Nome e função"). Com a proteção **Sempre 100%** (o padrão), qualquer
  configuração aplicada a ela vira 100%; com **Velocidade mínima**, nada fica abaixo do mínimo escolhido (50 a
  100%). Vale para o editor, o Turbo e a reaplicação das curvas salvas. "Restaurar original" continua permitido.
- **Nomes:** cada fan pode ter um nome seu (até 32 caracteres), que aparece no card e nos gráficos com o original ao
  lado. Nomes e funções ficam em `bin\data\fan-meta.json`, pela identidade do fan, e sobrevivem a reordenações.
- **Alertas:** o painel avisa, numa faixa no topo e no card "Alertas", quando um fan que girava para (lê menos de 100
  RPM por 5 s com pelo menos 30% comandado; bomba parada é crítico), quando a CPU passa de 95 °C ou a GPU de 87 °C
  por 10 s (limites ajustáveis; o alerta só termina 5 °C abaixo, depois de 30 s), quando a placa não responde por 1
  minuto e quando um fan foi devolvido à BIOS por segurança. Fans sem tacômetro nunca geram alerta.
  - **Fora do painel**, opcional: um tópico do [ntfy](https://ntfy.sh) (o botão "Gerar tópico" sugere um aleatório,
    difícil de adivinhar; assine o mesmo tópico no app do ntfy) ou um bot do Telegram (token e chat id). Cada alerta
    avisa no máximo uma vez a cada 10 min, e avisa de novo quando termina. Nada sai do PC sem um destino
    configurado, e as mensagens levam só o alerta e o nome do PC. O endereço do tópico e o token ficam cifrados com a
    DPAPI em `bin\data\alerts.json` (outro PC não consegue lê-los).

### Placas sem GCC: curvas executadas pelo FanPilot

O LibreHardwareMonitor só consegue fixar uma porcentagem num header ou devolvê-lo à BIOS; o chip não guarda a
curva. Por isso o FanPilot lê a temperatura e ajusta cada fan **uma vez por segundo**.

Para o fan não ficar subindo e descendo a cada pico de carga da CPU, cada curva tem uma **resposta** (no editor):
**Rápida**, **Equilibrada** (o padrão, também para as curvas salvas antes dela existir) ou **Suave**. Ela define
quanto a temperatura é suavizada antes de virar velocidade, quantos graus o PC precisa esfriar para o fan
desacelerar (1, 2 ou 4 °C) e a velocidade máxima de subida e de descida. O modo Fixo não é suavizado. As proteções
valem em qualquer resposta:

- **Último ponto da curva:** com a temperatura (sem suavização) no último ponto ou acima dele, o fan vai a 100% na
  hora.
- **Fans que você não alterou ficam com a BIOS** ("Controle da BIOS" no painel). "Devolver à BIOS" desfaz uma
  curva sua.
- **Ao fechar** (janela, Ctrl+C, logoff, desligar o PC) ou **se o FanPilot der erro fatal**, todos os fans voltam
  ao controle da BIOS.
- **Sensor de referência sem leitura** (ou com valor impossível, como 127 °C de uma entrada desconectada): aquele
  fan volta à BIOS até a leitura voltar.
- **Falhas seguidas** do LibreHardwareMonitor (5 em sequência): todos os fans voltam à BIOS.
- A velocidade é reaplicada a cada segundo, desfazendo o que a BIOS ou um app do fabricante tenha mudado.
- **Limite:** se o processo for **encerrado à força** (Gerenciador de Tarefas > Finalizar tarefa, `taskkill /f`)
  ou o PC travar, não há como reagir: o fan fica na última velocidade até a BIOS ou o FanPilot assumir de novo.
  Por isso o piso de 20% e o 100% obrigatório até 90 °C valem aqui também, e o `autostart.ps1` configura a
  tarefa agendada para tentar reiniciar o FanPilot.
- Não use junto com o controle de fans do fabricante (**ASUS Armoury Crate / AI Suite "Fan Xpert"**, MSI Center
  etc.): os dois vão disputar os fans. Desative o controle de fans de lá.

Limites ajustáveis em `bin\data\config.json` (`minDutyPercent`, `fixedModeSafetyTemp`, `maxFullSpeedTemp`,
`gpuSafetyTemp`, `port`, `publicHost`, `pairCodeTtlSeconds`, `sessionDays`, `backend`).

## Operação

- **Parar:** feche a janela do FanPilot ou pressione Ctrl+C nela. Ao sair, o fan da GPU volta para automático.
  Na Gigabyte com GCC as curvas ficam gravadas no chip; nas outras placas os fans voltam ao controle da BIOS.
- **Uma instância por vez:** abrir o FanPilot com ele já rodando só abre o painel. Se outro programa estiver na
  porta, o FanPilot avisa e fecha antes de mexer em qualquer fan.
- **Log:** `bin\data\fanpilot.log` (pareamentos, mudanças de fan, reaplicações do watchdog, erros). Ao passar de
  5 MB ele vira `fanpilot.1.log` (até 3 arquivos antigos). Uma mensagem repetida em sequência aparece uma vez, com um
  resumo a cada minuto, e as falhas viram registros de mudança de estado (falhou, voltou).
- **Dados:** `bin\data\` guarda `config.json`, `sessions.json`, `backup-curves.json` (curvas originais, gravado
  só na primeira execução), `desired-curves.json` (curvas que o FanPilot mantém) e `machine.json` (identifica o PC
  e a placa-mãe). Cada arquivo é gravado de uma vez só (nunca fica pela metade) e guarda a versão anterior em
  `.bak`. Um arquivo ilegível é guardado como `<nome>.corrompido-<data>`, e o FanPilot segue com a versão anterior
  ou sem ele, avisando no painel. Um `config.json` inválido nunca é alterado: o FanPilot usa os valores padrão até
  ele ser corrigido. As curvas são guardadas pela identidade de cada fan, não pela posição: se uma atualização da
  BIOS ou do LibreHardwareMonitor mudar a ordem dos fans, cada curva continua no seu; a de um fan que sumiu não é
  aplicada a nenhum outro, e o painel oferece descartá-la. Versões do FanPilot anteriores a essa mudança não leem o
  formato novo; os arquivos antigos ficam como `.v1`.
- **Outro PC / troca de placa-mãe:** se `bin\data\` vier de outro PC ou de outra placa, o FanPilot não aplica as
  curvas, o backup nem os pareamentos antigos: eles são movidos para `bin\data\outra-maquina-<data>\` e tudo
  recomeça do zero (o `config.json` é mantido). Para levar o programa a outro PC, use o zip gerado por
  `scripts\package.ps1` (ou copie a pasta `bin\` sem a `data\`).
- **GCC e FanPilot juntos:** não use os dois para controlar fans ao mesmo tempo — um sobrescreve o outro, e o
  watchdog do FanPilot vai reaplicar a curva dele a cada 15 s. Para voltar ao GCC, use "Restaurar original" em
  cada fan alterado (isso também tira o fan do watchdog). Só monitorar com os dois abertos não tem problema.

## Problemas comuns

| Sintoma | Causa provável / solução |
|---|---|
| Celular não abre a página | Firewall: rode `scripts\firewall.ps1`. A regra vale só para rede **Privada** — se o Wi-Fi estiver como Pública, mude em Configurações › Rede › Propriedades. |
| QR aponta para IP errado | Escolha outro endereço na caixa da página `/pair` (ex.: o do Wi-Fi, não o `vEthernet`), ou fixe `publicHost` no `config.json`. |
| "Código inválido ou expirado" | O código é de uso único e dura ~3 min; escaneie o QR atual de novo. Muitas tentativas erradas bloqueiam o IP por 1 min. |
| Celular volta para a tela de bloqueio | Sessão revogada em `/pair`, cookies apagados ou 30 dias sem uso: pareie de novo. |
| Status "sensores da placa indisponíveis" | Gigabyte: `EasyTuneEngineService` parado (`services.msc`) ou GCC atualizado/removido. A GPU continua funcionando. |
| "Falta o driver PawnIO" | Placa sem GCC: feche e abra o FanPilot pela janela e responda `S` para instalar (ou instale pelo [site](https://pawnio.eu)). Com o autostart (`--no-browser`) ele não pergunta. |
| Placa sem GCC não mostra fans | O LibreHardwareMonitor não reconheceu ou não controla o chip dessa placa. Rode `FanPilot.exe --diagnose` e relate pelo link que vem no arquivo gerado. |
| FanPilot fecha logo ao abrir | Não está como Administrador, ou o GCC não está instalado. Veja o log. |
| "Esta placa-mãe não oferece controle de fans" | A placa não usa o caminho de controle que o FanPilot implementa; só o monitoramento funciona. |
| Fan da GPU não aceita modo fixo | Driver recusou o controle manual (mensagem com código NVML); o fan fica em automático. |
| Curva "volta sozinha" | Algo (GCC, BIOS, resume do sleep) a sobrescreveu; o watchdog reaplica em até 15 s e registra no log. |

## Exemplo: observações da placa de teste (B760M GAMING X AX)

- O GCC estava no modo global **FullSpeed** (`Control Center\Fan\setupmode.xml`): todas as curvas originais
  são 100% em qualquer temperatura. É isso que "Restaurar original" devolve.
- O header **CPU** marca 0 RPM e **System 3** também: nada com tacômetro está ligado neles. Não há
  controlador USB de watercooler (o único dispositivo USB de placa é o controlador RGB ITE da própria
  placa-mãe), então o AIO não reporta nada por software. Hipóteses: a bomba do watercooler está num header
  System (bombas costumam girar ~2000–2800 RPM constantes, como o **System 1** a ~2350) ou alimentada direto
  pela fonte (SATA/Molex), sem fio de RPM; os fans do radiador provavelmente estão no **System 2** (~1680 RPM,
  que usa o sensor CPU como referência). Confira fisicamente. **Se um header for a bomba, marque-o como bomba** no
  editor ("Nome e função"): bomba lenta reduz muito a refrigeração, e com a proteção "Sempre 100%" nada que o
  FanPilot aplique a deixa mais lenta. O gráfico de RPM omite headers que leem 0.
- Sensores de temperatura disponíveis para referência das curvas: System 1, PCH, CPU, PCIEX16, VRM MOS, System 2.
  A temperatura "CPU" do GCC é a do sensor da placa (socket), não a dos núcleos: o painel a chama de "CPU · socket" e,
  com o PawnIO instalado, mostra no card a temperatura dos núcleos ("CPU · pacote"). As curvas continuam no sensor da
  placa, porque é o que o chip lê.

## Ferramentas de engenharia reversa (`tools/`)

Programas de teste usados para validar a análise; precisam rodar como Administrador e referenciam as DLLs do GCC.
Não fazem parte do projeto: compile cada um com o `csc.exe` do .NET Framework, passando as DLLs do GCC em `/r:`.

| Arquivo | O que faz |
|---|---|
| `Probe.cs` | Primeira leitura via remoting: sensores do HardwareMonitor e tentativa nos módulos SmartGuardian. |
| `Probe2.cs` | Experimento que mostrou a falha da primeira chamada IPC (resolvida com retry) e os módulos "não suportados". |
| `Probe3.cs` | Leitura (somente) das curvas pelo caminho de 3ª geração (`SmartGuardianFanControlCoordinateSystemModule`). |
| `Probe4.cs` | Teste de escrita: aplica 40% no System 1 por 8 s, mede o RPM e **restaura a curva original** automaticamente. |
| `ui-mock/` | Servidor de dados falsos para mexer no painel **sem placa Gigabyte nem Administrador**: `python tools/ui-mock/mock.py` e abra `http://127.0.0.1:8799/__mock`. Alterna entre Demo, Pior caso (placa com 8 fans, nomes longos, sensores desconectados), Sem controle, Serviço fora e Turbo, em 320/390/1024 px e tema claro/escuro. |

## API

| Método | Rota | |
|---|---|---|
| GET | `/api/state` | snapshot atual (sensores, rpm, GPU) |
| GET | `/api/stream` | Server-Sent Events, 1 snapshot/s |
| GET | `/api/history` | últimos 10 min (temperaturas, potência, rpm) |
| GET | `/api/fans` | curvas atuais, sensores de referência, limites |
| GET | `/api/info` | versão do FanPilot, backend e placa-mãe |
| GET | `/api/alerts` | alertas ativos e recentes (os ativos também vêm em cada snapshot, no campo `alerts`) |
| POST | `/api/alerts/ack` | `{"id":"…"}` tira o alerta da faixa (ele continua na lista enquanto durar) |
| GET/POST | `/api/alerts/settings` | limites, nome do PC e destinos; os segredos voltam só mascarados; `""` remove um destino |
| POST | `/api/alerts/test` | envia uma mensagem de teste para os destinos configurados |
| POST | `/api/fans/{i}` | `{"mode":"fixed","percent":40}` ou `{"mode":"curve","style":"SmartFanMode","points":[[0,30],…N pontos em %, N = pointCount do fan],"tempInput":2}` |
| POST | `/api/fans/{i}/restore` | volta à curva original |
| POST | `/api/fans/{i}/meta` | `{"label":"Frente","role":"pump","pumpPolicy":"min","pumpMinPercent":70}` nome (até 32 caracteres; vazio volta ao original) e função; `role` `fan` ou `pump`, `pumpPolicy` `full` (sempre 100%) ou `min` (piso de 50 a 100%) |
| POST | `/api/fans/turbo` | `{"on":true}` todos os fans da placa-mãe em 100%; `{"on":false}` devolve cada fan ao estado anterior |
| POST | `/api/fans/mode` | `{"mode":"profile:quiet"}` (ou `profile:standard`, `profile:performance`) põe o perfil em todos os fans menos as bombas; `{"mode":"custom"}` devolve a configuração pessoal |
| POST | `/api/gpu/fan` | `{"mode":"auto"}` ou `{"mode":"fixed","percent":50}` |

## Estrutura

```
src/        C# (.NET Framework 4.7.2, FanPilot.csproj): IBoard + pontes Gigabyte (GCC) e LibreHardwareMonitor,
            NVML, servidor HTTP/SSE, auth, política de fans, --diagnose
tests/      testes automatizados: xUnit (dotnet test), painel (tests/js, node --test) e ponta a ponta (tests/e2e, Playwright)
wwwroot/    painel (index.html/app.js), pareamento (pair.html/pair.js), qrcode-generator (MIT)
tools/      probes usados na engenharia reversa
assets/     identidade visual: SVGs-fonte, ícone do .exe, banners (regenerar: python assets/brand/build.py)
scripts/    firewall, auto-início, empacotamento da release, notas da release e checagem de encoding
.github/    CI, release por tag e Dependabot
docs/       relatório da engenharia reversa, guia para testar outras placas, roadmap e specs
```

## Licença

[MIT](LICENSE). Componentes de terceiros: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
