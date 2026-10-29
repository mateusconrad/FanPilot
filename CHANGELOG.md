# Changelog

Mudanças visíveis do FanPilot, da mais nova para a mais antiga. O formato segue o
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem o
[Versionamento Semântico](https://semver.org/lang/pt-BR/). Enquanto a versão começar com `0.`, cada release sai como
pre-release.

Todo commit que muda algo visível acrescenta uma linha em **Não lançado**. Na release, essa seção ganha o número e a
data (`## [0.1.0] - AAAA-MM-DD`), e as notas da release no GitHub são tiradas dela.

## [Não lançado]

Primeira versão publicada (será a 0.1.0).

### Adicionado

- Painel web servido pelo próprio PC, feito para o celular: temperaturas, potência, clocks, gráfico dos últimos
  10 minutos, editor de curvas dos fans da placa-mãe (arrastando os pontos), modo fixo, Turbo e o fan da GPU.
- Acesso pela rede só depois de parear o aparelho com um QR code exibido no PC.
- Placas Gigabyte pelo Gigabyte Control Center, com as curvas gravadas no chip; as outras placas (ASUS, MSI,
  ASRock…) pelo LibreHardwareMonitor e o driver PawnIO, que o FanPilot oferece instalar pelo `winget`.
- GPU NVIDIA pelo driver (NVML); AMD e Intel Arc pelo LibreHardwareMonitor.
- Na Gigabyte com o PawnIO instalado, o card e o gráfico da CPU mostram a temperatura dos núcleos (pacote) em vez da
  do socket da placa; sem o PawnIO, o card diz que é o socket e como ver os núcleos.
- Nas placas em que o FanPilot executa as curvas (LibreHardwareMonitor), a resposta de cada curva: Rápida,
  Equilibrada ou Suave. A temperatura é suavizada, o fan só desacelera depois de o PC esfriar alguns graus e a
  velocidade muda aos poucos, sem o sobe e desce a cada pico de carga. Curvas salvas antes disso ficam com a
  Equilibrada. No último ponto da curva, o fan vai a 100% na hora.
- `FanPilot.exe --diagnose`: relatório só de leitura para placas ainda não testadas, com a versão do GCC e se ela é
  compatível. Sai sem números de série nem UUIDs, pronto para anexar numa issue, e começa com o link do formulário
  de relato já preenchido.
- Modo serviço do Windows (`--service`), com os dados em outra pasta (`--data`) e a importação de uma instalação
  portátil (`--import-data`): a base do instalador. Como serviço, a pasta de dados fica só para o sistema e os
  Administradores, e avisos e erros vão também para o log de eventos.
- Nas placas em que o FanPilot executa as curvas, a partida devolve todos os fans à BIOS antes de reaplicar as curvas
  salvas: um FanPilot que caiu não deixa uma velocidade velha para trás.
- Alertas de fan parado (bomba parada é crítico), temperatura crítica da CPU ou da GPU, placa sem responder e fan
  devolvido à BIOS por segurança: no painel e, se configurado, no ntfy ou no Telegram, no máximo uma vez a cada
  10 min por alerta. Nada sai do PC sem um destino configurado.
- Perfis globais Silencioso, Padrão e Performance, com volta exata à configuração pessoal; o Turbo funciona por cima
  de qualquer perfil, e as bombas ficam de fora. O `turbo.json` vira `global-mode.json` sozinho.
- Um nome seu para cada fan, mostrado no painel e nos gráficos com o original ao lado.
- Editor de curvas pelo teclado e pelo leitor de tela: cada ponto é anunciado com a temperatura e a velocidade e anda
  pelas setas (com Shift, de 5 em 5). Tocar num ponto abre o ajuste fino, de 1 °C ou 1% por toque (segurar repete).
  Esc fecha o editor e o foco volta ao fan; fechar com uma mudança não aplicada pergunta antes. Botões e pontos do
  editor com pelo menos 44 px para o dedo.
- Bombas de watercooler reconhecidas pelo nome (ou marcadas no editor) e protegidas: sempre 100% ou uma velocidade
  mínima, valendo para o editor, o Turbo e as curvas salvas.
- Formulários de relato de compatibilidade e de problemas no GitHub, e a lista das placas testadas
  ([docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)).
- `FanPilot.exe --hardware-test [--fan N]`: roteiro automático de verificação na placa real, que grava
  `data\hwtest-<data>.md` e devolve tudo como estava.
- `FanPilot.exe --version`; a versão também aparece na primeira linha do log, no `--diagnose` e no rodapé do painel.
- Releases no GitHub com o zip, o `SHA256SUMS.txt` e estas notas; CI compilando e testando cada push.
- Texto secundário do painel com contraste de pelo menos 4,5:1 nos temas claro e escuro (conferido pelo axe-core).
- Nas placas em que o FanPilot executa as curvas, um fan pode seguir a GPU, ou a maior entre a CPU e a GPU: os fans
  do gabinete aceleram quando a GPU esquenta num jogo, mesmo com a CPU folgada.
- Modo painel (`/panel`): uma tela só de leitura e em tela cheia para um celular ou tablet ao lado do monitor ou uma
  telinha de gabinete, legível de longe, com opção de manter a tela ligada e proteção para telas OLED.
- Histórico em disco: os gráficos mostram também a última hora, as últimas 24 horas e os últimos 7 dias, e o que
  foi gravado continua lá depois de reiniciar o FanPilot. Nos períodos longos, a dica mostra a média e o máximo de
  cada ponto. Ocupa cerca de 1 MB por dia em `data\history\`; a retenção (7 dias por padrão) muda no `config.json`
  (`historyDays`; 0 desliga).
- Integrações só de leitura: o Prometheus lê o `/metrics` e o Home Assistant (sensor REST) lê o `/api/v1/sensors`,
  um JSON estável, com tokens criados na página `/pair` do PC e revogáveis a qualquer momento. Ou, configurado no
  painel, o FanPilot publica num broker MQTT e as entidades aparecem sozinhas no Home Assistant.
- Painel em inglês e em português: o idioma segue o do navegador e pode ser trocado no rodapé (a escolha fica no
  aparelho); números e horas seguem o idioma. Erros, avisos e alertas do FanPilot também, e os alertas enviados ao ntfy
  e ao Telegram saem no idioma do painel em que foram configurados. O console, o `--diagnose` e o `--hardware-test`
  seguem o idioma do Windows, e o log é sempre em inglês, para buscar uma mensagem em qualquer issue. README e guia de
  teste em inglês e em português, e formulários de issue em inglês com os rótulos em português.

### Interface no terminal

- A janela do FanPilot mostra as temperaturas e os coolers (RPM e porcentagem) com barras e gráficos dos últimos 10
  minutos, o endereço do painel e as teclas `o` (abrir no navegador), `t` (Turbo), `l` (log) e `q`; `--log` volta ao
  log rolando. `FanPilot --tui` mostra a mesma tela ligada a um FanPilot que já roda, sem Administrador, no Windows e
  no Linux ([#36](https://github.com/mateusconrad/FanPilot/issues/36)).

### Partida sem janela

- O `autostart.ps1` passa a instalar o FanPilot como serviço do Windows: sobe com o PC, antes do logon e sem janela,
  volta sozinho se fechar e traz a pasta `data\` da versão portátil; "FanPilot" no menu Iniciar abre a tela do
  terminal sem UAC, e o `FanPilot.exe` aberto com o serviço rodando mostra a tela dele. No Linux, o `install.sh` põe o
  `fanpilot` no PATH e "FanPilot" no menu de aplicativos. A tarefa agendada do `autostart.ps1` antigo é removida
  ([#37](https://github.com/mateusconrad/FanPilot/issues/37)).

### Linux (em teste)

- O FanPilot roda no Linux como root: placa-mãe pelos drivers hwmon do kernel, com as curvas executadas pelo FanPilot;
  CPU pelo `k10temp` ou `coretemp`; GPU pelo NVML ou pelo `amdgpu`; `--diagnose` com cada chip hwmon; e
  `--restore-fans`, que devolve à BIOS os fans de um FanPilot morto com `kill -9`
  ([#35](https://github.com/mateusconrad/FanPilot/issues/35)).
- Pacote `FanPilot-<versão>-linux-x64.tar.gz` na release, com o .NET dentro, e `install.sh`, que instala o FanPilot
  como serviço do systemd (no boot, reiniciado se morrer, devolvendo os fans ao parar) e o remove com `--remove`.

### Segurança

- O header `Host` é validado: só são atendidas requisições endereçadas a este PC (as outras recebem `421`), o que
  impede que uma página web alcance a API local por DNS rebinding. Só `localhost` e `127.0.0.1` contam como o
  próprio PC.

### Confiabilidade

- Os arquivos de dados são gravados de uma vez só, com a versão anterior em `.bak`; um arquivo ilegível é guardado
  como `.corrompido-<data>` e o painel avisa.
- As curvas são guardadas pela identidade de cada fan, não pela posição: se a ordem dos fans mudar, cada curva
  continua no seu, e a de um fan que sumiu não vai para outro (o painel oferece descartá-la). Os arquivos do formato
  antigo são convertidos sozinhos e ficam como `.v1`.
- Um FanPilot por vez; se a porta estiver ocupada, ele avisa e fecha antes de mexer em qualquer fan.
- A proteção da GPU e a transmissão ao vivo do painel não dependem mais do laço de leitura dos sensores; o painel
  avisa quando os dados estão atrasados e volta sozinho quando o FanPilot reinicia, mesmo se ele ainda estiver
  iniciando ou com painéis demais abertos.
- Log com rotação a cada 5 MB (até 3 arquivos antigos) e sem a mesma mensagem repetida milhares de vezes.
- Turbo e edição de fans não se atropelam mais em toques rápidos; o painel diz se a curva foi gravada no chip ou é
  executada pelo FanPilot; erros do pareamento aparecem na página.

### Para quem compila

- Projeto no formato SDK (`dotnet build`, .NET SDK 8 fixado pelo `global.json`), ainda rodando no .NET Framework
  4.7.2 do Windows, com os pacotes do NuGet fixados por `packages.lock.json`.
- O build não depende mais do Gigabyte Control Center: as DLLs do GCC são carregadas só na hora de rodar, e um mesmo
  zip atende todas as placas.
- Testes automatizados (`dotnet test`, `node --test`) e uma placa falsa para rodar o FanPilot inteiro sem hardware
  e sem Administrador (`--fake-board[=<cenário>]`, builds Debug).
- Testes de ponta a ponta (Playwright, no Edge) do painel contra o FanPilot com a placa falsa, e o smoke do zip da
  release antes de publicar.
- Compilar exige o .NET SDK 10 (o suporte do 8 termina em 10/11/2026); o FanPilot do Windows continua no .NET
  Framework 4.7.2.
- O mesmo projeto compila também para o Linux (.NET 10), com os testes rodando no Ubuntu na CI
  ([#35](https://github.com/mateusconrad/FanPilot/issues/35)).
