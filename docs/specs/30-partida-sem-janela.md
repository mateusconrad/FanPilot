# Partida sem janela

- **Item:** 30
- **Onda:** 6 · Uso no PC
- **Tipo:** produto
- **Esforço:** P–M
- **Depende de:** 14a, 28c, 29
- **Integra com:** 14b, 14c

## Problema

O jeito documentado de o FanPilot subir com o PC é o `scripts\autostart.ps1`: uma tarefa agendada que, a cada logon,
abre o `FanPilot.exe` de console. A janela fica aberta para sempre na barra de tarefas, e fechá-la fecha o FanPilot (e
devolve os fans à BIOS). No Linux ainda não há partida automática (vem com a 28c).

## Objetivo

O FanPilot sobe com o PC sem janela nenhuma, nos dois sistemas, e a interface do item 29 abre quando a pessoa quiser,
por um atalho, sem Administrador.

## Solução

1. **O serviço é a partida automática.** O FanPilot já roda como serviço do Windows (`--service`, item 14a) e vai rodar
   como serviço do systemd (item 28c): sem console, antes do logon, reiniciado se morrer. O `autostart.ps1` deixa de
   criar a tarefa agendada e passa a registrar o serviço (o que a 14a documenta à mão no ponto de parada):
   `New-Service` com `--service --data %ProgramData%\FanPilot`, a recuperação de 1 s, 5 s e 60 s, a importação da
   pasta `data\` de antes (`--import-data`) e a partida automática. `-Remove` para e apaga o serviço e também a tarefa
   antiga, se ainda existir.
2. **Um atalho para a interface:**
   - Windows: "FanPilot" no menu Iniciar (`%AppData%\Microsoft\Windows\Start Menu\Programs`, criado pelo
     `autostart.ps1` e, depois, pelo instalador da 14c), que abre `FanPilot.exe --tui` no terminal padrão; sem elevação.
   - Linux: `fanpilot.desktop` em `/usr/share/applications` com `Terminal=true` e `Exec=/opt/fanpilot/FanPilot --tui`,
     instalado pelo `install.sh` da 28c, e um link `/usr/local/bin/fanpilot` para quem usa o terminal.
3. **O `FanPilot.exe` aberto com o serviço rodando** (um clique duplo, o atalho antigo): hoje diz "já está rodando" e
   sai. Passa a abrir a interface ligada ao serviço (`--tui`), que é o que a pessoa queria ver.
4. **Quem não quer o serviço** (a versão portátil, um teste) continua abrindo o `FanPilot.exe` à mão: a janela mostra a
   interface do item 29.
5. **A tarefa agendada antiga:** o `autostart.ps1` novo a remove ao registrar o serviço, e o FanPilot, ao subir como
   serviço, avisa no log se ela ainda existir (as duas disputariam a porta e os fans; a segunda diria "já está rodando").

### Por que não uma janela escondida

Esconder o console (um `WinExe` sem janela, ou a tarefa com a janela oculta) tiraria o incômodo, mas deixaria o
FanPilot dependente do logon e sem como ver o que está acontecendo. O serviço já existe, já é testado na CI (item 14a)
e é o que o instalador (14c) vai instalar; este item só o torna o caminho padrão.

### Como ficou (29/10/2026)

Feito antes do ponto de parada da 14a (o serviço na B760M), por decisão do dono: o item só torna o serviço a partida
padrão; o que o teste na placa mostrar entra depois.

- `scripts\autostart.ps1` (Windows PowerShell 5.1): remove a tarefa agendada antiga; troca o serviço anterior, se
  houver; importa a `data\` portátil uma vez (só quando `%ProgramData%\FanPilot` ainda não tem `machine.json`); registra
  o serviço `FanPilot` (automático, `--service --data %ProgramData%\FanPilot`, recuperação de 1 s, 5 s e 60 s) e o
  inicia; cria "FanPilot" no menu Iniciar de todos os usuários. `-Remove` tira serviço, atalho e tarefa e deixa os dados.
- O atalho não pede UAC: o manifesto do `FanPilot.exe` pede Administrador, e o atalho roda
  `cmd /c "set __COMPAT_LAYER=RunAsInvoker&& FanPilot.exe --tui --port <porta>"` (o mesmo que os e2e usam). A porta vai no
  atalho porque a pasta do serviço é só de SYSTEM e Administradores: `--port`, opção nova, diz ao `--tui` onde está o
  FanPilot. Trocar o manifesto por `asInvoker` com auto-elevação seria mais limpo e fica como alternativa se o
  `RunAsInvoker` der problema.
- `FanPilot` aberto com outro rodando (`App.ShowsRunningOne`): num terminal, com uma pessoa na janela, mostra a tela
  ligada a ele; `--log`, `--no-browser`, `--restore-fans`, o serviço e a importação continuam com "já está rodando".
- O serviço do Windows avisa no log, depois de subir, se a tarefa agendada antiga ainda existir.
- Linux (`install.sh` da 28c): `/usr/local/bin/fanpilot` aponta para o executável e
  `/usr/share/applications/fanpilot.desktop` (`Terminal=true`) abre `--tui --port <porta>`; `--remove` apaga os dois.
- Testes: `--port` e a escolha do `ShowsRunningOne` (`AppOptionsTests`). Na CI do Windows, o `autostart.ps1` de verdade
  a partir de uma tarefa agendada antiga e de uma `data\` portátil na porta 8790: a tarefa some, o serviço sobe
  automático e responde na porta importada, o atalho leva `--tui --port 8790`, e o `-Remove` limpa tudo menos os dados.
  Na do Linux, o link, o `.desktop` e o `fanpilot --tui` pelo link, e o `--remove` sem eles.
- Fica para a verificação em hardware: reiniciar a B760M e um PC com Linux e ver que nada abre; o atalho abrindo a tela.

## Critérios de aceite

- [ ] Windows: depois do `autostart.ps1`, reiniciar o PC não abre janela nenhuma; o painel e o celular funcionam antes
  do logon; o atalho "FanPilot" do menu Iniciar abre a interface sem pedir UAC.
- [ ] Windows: com a tarefa agendada antiga instalada, o `autostart.ps1` novo a remove e migra a pasta `data\`.
- [ ] Windows: o `FanPilot.exe` aberto com o serviço rodando mostra a interface ligada a ele.
- [ ] Linux: depois do `install.sh` (28c), o boot não abre terminal; "FanPilot" no menu de aplicativos e `fanpilot --tui`
  abrem a interface sem root.
- [ ] `autostart.ps1 -Remove` deixa o PC como antes (sem serviço, sem tarefa, sem atalho), com os dados mantidos.

## Testes

- Automatizados: o `autostart.ps1` num runner da CI (instala o serviço com a placa falsa, confere que não há tarefa
  agendada, que o atalho existe e que o `-Remove` limpa tudo), como o `service.spec.mjs` da 14a já faz com o serviço; a
  escolha entre "já está rodando" e abrir a interface (`ServiceTests`).
- Manual: reiniciar a B760M e um PC com Linux e conferir a ausência de janela e o atalho; vai para a verificação em
  hardware.
