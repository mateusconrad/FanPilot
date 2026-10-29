# Serviço do Windows, ícone na bandeja e instalador

- **Item:** 14
- **Onda:** 3 · Produto
- **Tipo:** produto
- **Esforço:** G (três etapas: 14a, 14b e 14c)
- **Depende de:** 03, 05, 06
- **Integra com:** 12, 16, 18

## Problema

- Com o autostart, o FanPilot abre uma janela de console a cada logon (`scripts/autostart.ps1:10-14`). Nas placas sem GCC, fechar essa janela devolve os fans à BIOS: um clique distraído desliga o controle.
- Só roda depois do logon e depende de uma tarefa agendada para evitar o UAC.
- Se o processo for morto, o fan fica na última velocidade até alguém reabrir o programa (README, "Limite").
- Firewall, autostart e PawnIO dependem de scripts e de perguntas no console: é uma experiência de desenvolvedor, não de produto.

## Objetivo

Instalar e esquecer: o controle de fans roda em segundo plano desde o boot e se recupera sozinho de falhas, e o usuário interage pela bandeja e pelo celular.

## Arquitetura

```
FanPilot.exe --service  (LocalSystem, sessão 0)              FanPilotTray.exe (usuário, sem elevação)
  ponte da placa, GPU, servidor web, alertas   <-- HTTP -->   ícone, menu, notificações
  dados em %ProgramData%\FanPilot                 loopback
```

O modo console continua existindo, para desenvolvimento, para a versão portátil e para o `--diagnose`.

## 14a · Serviço

1. `FanPilot.exe --service` hospeda o app num `ServiceBase` (`CanShutdown`, `CanHandlePowerEvent`).
2. `--data <pasta>`: a versão instalada usa `%ProgramData%\FanPilot`, com ACL restrita a SYSTEM e Administradores. Sem `--data`, continua `.\data` (portátil).
3. `OnStop` e `OnShutdown` executam a mesma limpeza de hoje (`Exit.Cleanup`). A devolução à BIOS precisa terminar em menos de 1 s.
4. `OnPowerEvent(ResumeSuspend)` reaplica as curvas na hora (hoje o watchdog leva até 15 s).
5. Recuperação do Windows: reiniciar após 1 s, 5 s e 60 s, zerando a contagem depois de 1 dia.
6. Na partida, as placas sem GCC devolvem todos os headers à BIOS antes de reaplicar as curvas salvas (estado limpo depois de uma queda).
7. Log de eventos do Windows (fonte "FanPilot") para início, parada, erro fatal e devoluções à BIOS por segurança.
8. Sem console: o serviço não pergunta sobre o PawnIO (o instalador cuida dele), e abrir o navegador sai do serviço e passa para a bandeja.
9. **Ponto de parada:** antes de seguir para 14b, verificar no hardware o IPC do GCC rodando como LocalSystem, o NVML na sessão 0, o LibreHardwareMonitor com PawnIO como serviço, a devolução à BIOS no desligamento, e a suspensão e o retorno.

### Andamento da 14a (25/10/2026)

Feita, menos o ponto de parada, que depende da placa real:

- `--service` (`FanPilotService`, um `ServiceBase` com `CanShutdown` e `CanHandlePowerEvent`): responde ao Windows na hora e abre a placa numa thread, porque o GCC pode levar segundos. `OnStop`/`OnShutdown` chamam o mesmo `FanPilotHost.Stop()` do console, que registra quanto levou ("Stopped in N ms"). `OnPowerEvent` (volta da suspensão) chama o watchdog na hora. Avisos e erros também vão para o log de eventos (Aplicativo, fonte "FanPilot"), e o `AutoLog` registra partida e parada.
- `--data <pasta>` (variáveis de ambiente expandidas). Como serviço, a pasta fica só para SYSTEM e Administradores, sem herança: o serviço confia nos pareamentos e no `config.json` dela.
- `--import-data <pasta>` (item 6 da 14c, adiantado): copia `config.json`, `machine.json`, `sessions.json`, as curvas, o Turbo e o `fan-meta.json`, passa pelo `MachineData.Check` e recusa com o FanPilot rodando.
- `LhmBoard` devolve todos os headers à BIOS na partida, antes de o `FanManager` reaplicar as curvas salvas.
- `FanPilotHost` reúne o que o `App.Run` fazia (a porta antes da placa, a ordem de partida, a limpeza), com a placa e a GPU por fábrica.
- Testes: `ServiceTests` (opções, pasta e ACL, importação, o host com a placa falsa) e `tests/e2e/service.spec.mjs`, que na CI instala um serviço de verdade com a placa falsa: ele sobe sem console com a pasta protegida, o `FanPilot.exe` de console diz "já está rodando", o Windows o reinicia em segundos depois de morto (a recuperação de 1 s, 5 s e 60 s) e a parada leva menos de 1 s.

### Ponto de parada: como verificar na B760M

Num PowerShell como Administrador, com o FanPilot fechado (os dois disputariam os fans), na pasta do repositório, depois do `.\build.ps1`:

```powershell
.\bin\FanPilot.exe --import-data .\bin\data --data C:\ProgramData\FanPilot
New-Service -Name FanPilot -StartupType Manual -BinaryPathName ('"' + (Resolve-Path .\bin\FanPilot.exe) + '" --service --data C:\ProgramData\FanPilot')
sc.exe failure FanPilot reset= 86400 actions= restart/1000/restart/5000/restart/60000
Start-Service FanPilot
```

1. `C:\ProgramData\FanPilot\fanpilot.log` mostra "as a service" e "Motherboard via Gigabyte Control Center": o IPC do GCC funciona como LocalSystem. A GPU aparece no painel: o NVML funciona na sessão 0.
2. O painel no PC (`http://localhost:8787`) e o celular pareado funcionam, e as curvas são as de antes.
3. Suspender e voltar: o log mostra "Back from sleep: curves checked now."
4. `Stop-Service FanPilot`: o log mostra "Stopped in N ms", com N abaixo de 1000.
5. `Set-Service FanPilot -StartupType Automatic` e reiniciar o PC: o log mostra a partida antes do logon.
6. Caminho LHM: `"backend": "lhm"` no `C:\ProgramData\FanPilot\config.json` (editor como Administrador), `Restart-Service FanPilot`: os fans respondem às curvas; `Stop-Service FanPilot`: voltam à BIOS.
7. Para desfazer: `Stop-Service FanPilot; sc.exe delete FanPilot` (os dados ficam em `C:\ProgramData\FanPilot`).

Passando, seguem a 14b (bandeja) e a 14c (instalador).

## 14b · Bandeja (`FanPilotTray.exe`)

1. Projeto WinForms separado (`net472`), sem elevação, iniciado no logon pela chave `Run` em HKLM (roda para cada usuário).
2. O ícone mostra o estado: ok, alerta, serviço parado.
3. Menu: Abrir painel · Parear celular · Turbo · Perfis (item 18) · Ver log · Sair da bandeja (sem parar o serviço).
4. Notificações do Windows para os alertas do item 16.
5. Conversa com o serviço por `http://localhost:<porta>`, usando as rotas atuais e `/api/info`.

## 14c · Instalador (Inno Setup)

1. `installer/FanPilot.iss`, compilado na CI (item 11); sai sem assinatura enquanto o item 12 for débito técnico.
2. Instala em `Program Files\FanPilot`. Depois:
   - cria o serviço (`sc create … start= auto`, `sc failure`, descrição);
   - cria a regra de firewall, só no perfil Privado;
   - registra a bandeja;
   - oferece instalar o PawnIO, com a caixa marcada só quando não houver Gigabyte com GCC;
   - inicia o serviço e, no fim, abre a página de pareamento.
3. Atualização: para o serviço, troca os arquivos e inicia de novo, preservando os dados.
4. Desinstalação: para e remove o serviço, a regra de firewall e a bandeja, e pergunta se apaga os dados.
5. A versão portátil (zip) continua existindo, e o `scripts/autostart.ps1` passa a valer só para ela.
6. Migração da versão portátil: `FanPilot.exe --import-data <pasta data antiga>` copia o estado (com a mesma checagem de máquina do `MachineData`), com instruções no README.

## Critérios de aceite

- [ ] Instalação num Windows 11 limpo: serviço rodando, nenhuma janela de console, celular pareando.
- [ ] Depois de reiniciar o PC, as curvas já estão ativas antes do logon.
- [ ] Suspender e voltar reaplica as curvas na hora (conferir no log).
- [ ] Matar o processo do serviço no Gerenciador de Tarefas: o Windows o reinicia em segundos e os fans voltam ao controle.
- [ ] Placa sem GCC: parar o serviço devolve os fans à BIOS.
- [ ] Desinstalar não deixa serviço, regra de firewall nem entrada de inicialização.
- [ ] Rodar `FanPilot.exe` com o serviço ativo mostra "já está rodando" (item 05) e abre o painel.

## Testes

- Automatizados: leitura dos argumentos (`--service`, `--data`, `--import-data`), resolução da pasta de dados e importação com a checagem de máquina.
- Smoke do instalador na CI: instalação silenciosa no runner; conferência do serviço, da regra de firewall e da entrada da bandeja; `GET /api/state` respondendo; parada e partida; desinstalação sem sobras.
- Em hardware: `--hardware-test` (item 27) com o FanPilot rodando como serviço, mais os critérios acima (o ponto de parada do 14a).
