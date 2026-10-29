# Interface no terminal

- **Item:** 29
- **Onda:** 6 · Uso no PC
- **Tipo:** produto
- **Esforço:** M
- **Depende de:** 21, 28a
- **Integra com:** 22, 24, 30

## Problema

No PC, o FanPilot só tem duas caras: o navegador (o painel) e a janela do console, que mostra o log rolando. A janela
abre a cada logon (veja o item 30) e não mostra nada útil de relance: nem as temperaturas, nem como os coolers estão.
Para ver qualquer número é preciso abrir o navegador.

## Objetivo

Uma interface mínima no próprio terminal, igual no Windows e no Linux, que mostra de relance as temperaturas e quanto
cada cooler está trabalhando, com gráficos em caracteres, e tem o link para abrir o painel completo no navegador.

Fica de fora: editar curvas, parear celulares, alertas e integrações. Isso continua no painel do navegador, que a
interface abre com uma tecla.

## Como fica

```
 FanPilot 0.1.0 · Gigabyte Control Center · Silencioso            http://localhost:8787  [o] abrir
 ─ Temperaturas ─────────────────────────────────────────────────────────────────────────────────
  CPU · pacote   62 °C  ██████████████░░░░░░░░  ▁▂▂▃▅▆▅▄▃▃▂▂   máx 10 min 71 °C
  GPU            48 °C  ███████████░░░░░░░░░░░  ▁▁▁▁▂▂▂▁▁▁▁▁
  Placa          36 °C  ████████░░░░░░░░░░░░░░
 ─ Coolers ──────────────────────────────────────────────────────────────────────────────────────
  CPU            1.240 RPM  ███████████░░░░░░░░░  55 %  curva
  System 1         810 RPM  ███████░░░░░░░░░░░░░  38 %  curva
  Bomba          2.900 RPM  ████████████████████ 100 %  bomba
  GPU              0 RPM    ░░░░░░░░░░░░░░░░░░░░   0 %  automático
 ─────────────────────────────────────────────────────────────────────────────────────────────────
  [o] painel no navegador  [t] Turbo  [l] log  [q] sair            atualizado há 1 s · 0 alertas
```

- **Temperaturas:** CPU (o papel `cpu-main` do item 19), GPU e os sensores da placa que alguma curva segue. Barra de 0 a
  100 °C com a cor da faixa (verde, amarelo, vermelho, nos limites que os alertas do item 16 já usam) e um sparkline
  dos últimos 10 minutos (`/api/history`).
- **Coolers:** RPM, a porcentagem que o FanPilot está aplicando agora e o modo (curva, fixo, BIOS, Turbo, bomba). A
  barra é a porcentagem. A GPU entra como um cooler a mais.
- **Cabeçalho:** versão, como a placa é alcançada (`backend`), o perfil (item 18) e o endereço do painel, como link
  clicável (OSC 8) onde o terminal suportar.
- **Rodapé:** as teclas, a idade da última leitura e os alertas ativos. Um alerta muda a cor da linha e aparece por
  extenso.
- **Teclas:** `o` abre o painel no navegador; `t` liga e desliga o Turbo (a única ação, por ser a de emergência); `l`
  alterna para o log de antes; `q` ou Ctrl+C sai (fechando o FanPilot quando ele roda nesta janela; só a interface
  quando ela se conectou a outro).
- **Idioma:** o do sistema, pelos dicionários do item 21 (`I18n.SystemLanguage`).
- **Terminal pequeno:** abaixo de 60 colunas, as barras somem e ficam os números; abaixo de 10 linhas, só temperaturas.

## Solução

1. **Dois jeitos de abrir, um código só:**
   - `FanPilot` no console, com uma pessoa na janela (o modo `Interactive` de hoje): o FanPilot sobe como hoje e a
     janela mostra a interface no lugar do log rolando. O log continua no arquivo, e `l` o mostra. `--log` mantém o
     comportamento antigo (útil para scripts e para quem prefere).
   - `FanPilot --tui`: só a interface, ligada a um FanPilot que já roda (o serviço do Windows, item 14a, ou o do
     systemd, item 28c). Não precisa de Administrador nem de root. Sem FanPilot rodando, diz isso e como iniciá-lo.
2. **Os dados vêm da API local**, como no painel: `/api/info` e `/api/fans` na abertura, `/api/stream` (SSE) a cada
   segundo, `/api/history` para os sparklines. Pelo loopback, a API já atende sem pareamento. A interface no modo
   console usa o mesmo cliente HTTP contra o próprio FanPilot, então os dois modos testam o mesmo caminho.
3. **A porcentagem de cada cooler** entra no snapshot (`fanDuty`): nas placas em que o FanPilot executa as curvas
   (`LhmBoard`, `HwmonBoard`), o duty que ele acabou de escrever; na Gigabyte, onde a curva roda no chip, o valor da
   curva na temperatura atual (`FanCurve.PwmAt`), marcado como estimado. O painel do navegador pode passar a mostrá-la
   também.
4. **Renderização própria em ANSI**, sem biblioteca: uma tela inteira redesenhada a cada segundo só onde mudou (buffer
   de linhas), com as cores de 16 tons, que todo terminal tem. No Windows, o modo VT do console é ligado por
   `SetConsoleMode` (`ENABLE_VIRTUAL_TERMINAL_PROCESSING`), que existe do Windows 10 1607 em diante; o conhost antigo e
   o Windows Terminal funcionam. A saída é UTF-8 (`Console.OutputEncoding`, como já é hoje). Sem VT (saída redirecionada
   para arquivo, terminal "dumb"), cai no `--log`.
5. **Abrir o navegador:** no Windows, `Process.Start(url)`. No Linux, `xdg-open`, mas só quando a interface não roda como
   root (o `--tui` de um usuário comum): como root, mostra o endereço, como já faz hoje (`Platform.OpenInBrowser`).
6. **Turbo pela interface:** `POST /api/fans/turbo`, o mesmo do painel, com o `X-FanPilot` que o servidor exige nos POSTs.

### Alternativas consideradas

- **Spectre.Console ou Terminal.Gui:** prontas e bonitas, mas trazem dependências para o `net472`, ao lado das DLLs do
  GCC (os binding redirects do item 09), e a tela aqui é pequena. Se a renderização própria passar de ~400 linhas,
  reavaliar a Spectre.Console (MIT, `netstandard2.0`).
- **Uma janela gráfica (WinForms, Avalonia):** fugiria do "visual de terminal" pedido e traria um toolkit inteiro. A
  bandeja do Windows (item 14b) continua sendo a ponte para quem quer um ícone.

### Como ficou (29/10/2026)

- `TerminalFeed` (os dados, pela API local: `/api/info`, `/api/fans` a cada 30 s e depois do Turbo,
  `/api/alerts/settings` para os limites das cores, `/api/history` para encher os sparklines e `/api/stream` a cada
  segundo, reconectando sozinho), `TerminalScreen` (a tela como linhas, com ou sem ANSI: é o que os testes conferem) e
  `TerminalApp` (tela alternativa, cursor escondido, teclas, redesenho só das linhas que mudaram, redimensionamento).
  Uns 740 linhas no total, sem biblioteca; a renderização (`TerminalScreen`) tem 230, abaixo das ~400 em que a spec
  manda reavaliar a Spectre.Console.
- `FanPilot` num terminal mostra a interface depois de abrir a placa; o log vai só para o arquivo enquanto ela está na
  tela, e `l` o mostra. Saída redirecionada, `--log` ou console sem VT (`Platform.EnableTerminal`: `SetConsoleMode` no
  Windows, `TERM` no Linux) mantêm o log de antes; é o que os e2e e o serviço continuam vendo.
- `FanPilot --tui`: liga-se pela porta do `config.json` da pasta de dados (só lida, nada é escrito; 8787 sem ele), sem
  Administrador nem root, antes de qualquer outra coisa do `App.Run`. Sem FanPilot respondendo, sai com 4 e a mensagem;
  sem terminal, com 1.
- O cliente fala com `localhost`: o `http.sys` do Windows recusa outro Host no modo "só este PC", e no Linux o cliente
  do .NET tenta o `::1` e depois o `127.0.0.1`.
- `fanDuty` e `fanDutyEstimated` no snapshot: exato no `LhmBoard` e no `HwmonBoard` (`IReportsDuty`, o duty que eles
  acabaram de escrever); nas outras placas, `FanListCache.DutyNow` (a curva na temperatura que ela segue), mostrado com
  "~". O `FanListCache` passou a ser um só para o sampler e as integrações.
- Ctrl+C é tratado como `q` (`TreatControlCAsInput`), para a tela sempre voltar ao normal; `SIGTERM` e o fechamento da
  janela continuam passando pelo `Exit`.
- `o`: no Linux, o `xdg-open` só quando a interface não roda como root (o `--tui` de um usuário comum).
- O cabeçalho, sem espaço, tira o nome do backend antes do endereço do painel.
- Testes: `TerminalScreenTests` (larguras de 100 e 50, altura 8, inglês e português, cores, link, alertas, sparkline,
  log), `TerminalFeedTests` (contra o FanPilot com a placa falsa: leitura, Turbo, nenhum FanPilot, o duty exato,
  a porta sem escrever nada), o duty no `HwmonBoardTests`. Num pseudo-terminal (`script`), no container e na CI do
  Ubuntu: o FanPilot desenha a tela e `q` o fecha; o `--tui` liga o Turbo com `t` e `q` deixa o FanPilot rodando.
- Falta conferir à mão, com um print de cada na issue: o Windows Terminal e o conhost do Windows 10/11 (o modo VT e as
  fontes), e um terminal gráfico do Linux. A CI do Windows não tem um console de verdade para isso.

## Critérios de aceite

- [ ] `FanPilot` no console abre a interface: temperaturas, coolers com RPM e porcentagem, link do painel; `o` abre o
  navegador, `t` alterna o Turbo, `l` mostra o log, `q` fecha o FanPilot devolvendo os fans como hoje.
- [ ] `FanPilot --tui`, sem Administrador nem root, liga-se ao serviço do Windows e ao do systemd e mostra o mesmo; `q`
  fecha só a interface. Sem FanPilot rodando, diz como iniciá-lo.
- [ ] Igual no Windows Terminal, no conhost do Windows 10 e num terminal do Linux (GNOME Terminal, Konsole), em inglês e
  em português.
- [ ] Redimensionar a janela redesenha sem lixo; abaixo de 60 colunas ficam os números.
- [ ] Saída redirecionada (`FanPilot > log.txt`) e `--log` se comportam como hoje.
- [ ] O consumo de CPU da interface fica abaixo de 1% de um núcleo.

## Testes

- Automatizados: o desenho da tela como texto (sem ANSI) para um snapshot fixo, em várias larguras, nos dois idiomas
  (`TerminalUiTests`); o cliente contra o servidor com a placa falsa (abertura, SSE, Turbo, FanPilot ausente); o
  `fanDuty` em cada placa (`FanManagerTests`, `HwmonBoardTests`); a escolha entre interface e log (`AppOptionsTests`).
- Ponta a ponta: `FanPilot --tui` contra a placa falsa num pseudo-terminal (`node-pty` no Playwright, ou `script` no
  Linux), conferindo os números e as teclas `t` e `q`.
- Manual: Windows Terminal, conhost e um terminal do Linux; um print de cada na issue.
