# Roadmap do FanPilot

Os 27 itens de melhoria aprovados em 24/10/2026: os 25 da análise inicial e mais dois de testes automatizados (26 e 27). O 28 (Linux) entrou em 28/10/2026; o 29 (interface no terminal) e o 30 (partida sem janela), em 29/10/2026. Cada item tem uma spec em [`docs/specs/`](specs/) e uma issue no GitHub.

Este arquivo é a fonte de verdade do andamento. As issues mostram o mesmo estado por labels (veja "Como o andamento é registrado").

- **Status:** `a fazer` · `em andamento` · `concluído` · `bloqueado` (motivo em Notas) · `adiado` (débito técnico; motivo em Notas)
- **Testes:** testes automatizados do item: `a fazer` · `ok` (commitados com o item; rodam na [CI](https://github.com/mateusconrad/FanPilot/actions) a cada push)
- **Esforço**, em dias de trabalho focado: P ≈ 1 · P–M ≈ 2 · M ≈ 3 · G ≈ 10

**Concluídos: 28 de 30** · verificação em hardware pendente: 01, 02, 04, 05, 09, 10, 11, 14a, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25, 27, 28b, 29, 30 (veja a seção própria)

## Andamento

| # | Item | Onda | Esforço | Depende de | Status | Testes | Issue | Notas |
|---|---|---|---|---|---|---|---|---|
| 01 | [Validar o header Host](specs/01-validar-host.md) | 1 | P | — | concluído | ok | [#1](https://github.com/mateusconrad/FanPilot/issues/1) | falta a verificação em hardware; testes de regressão no 08, e os 2 que exigem Administrador passam na CI |
| 02 | [SSE e proteção da GPU fora do laço de amostragem](specs/02-sse-e-protecao-gpu.md) | 1 | P–M | — | concluído | ok | [#2](https://github.com/mateusconrad/FanPilot/issues/2) | falta a verificação em hardware |
| 03 | [Leitura tolerante e gravação atômica](specs/03-arquivos-atomicos.md) | 1 | P | — | concluído | ok | [#3](https://github.com/mateusconrad/FanPilot/issues/3) | faixa de avisos nova no painel (`AppNotices`) |
| 04 | [Chave estável para cada header](specs/04-identidade-dos-headers.md) | 1 | P–M | 03 | concluído | ok | [#4](https://github.com/mateusconrad/FanPilot/issues/4) | migração v1→v2 testada numa cópia dos dados reais da B760M; falta a verificação em hardware |
| 05 | [Instância única e porta ocupada](specs/05-instancia-unica.md) | 1 | P | — | concluído | ok | [#5](https://github.com/mateusconrad/FanPilot/issues/5) | falta conferir como Administrador |
| 06 | [Rotação e deduplicação do log](specs/06-log.md) | 1 | P | — | concluído | ok | [#6](https://github.com/mateusconrad/FanPilot/issues/6) | nível `ERROR` novo; sensores da placa e LHM registram só as mudanças de estado |
| 07 | [Correções pequenas](specs/07-correcoes-pequenas.md) | 1 | P | — | concluído | ok | [#7](https://github.com/mateusconrad/FanPilot/issues/7) | |
| 08 | [Testes automatizados (unitários e integração)](specs/08-testes.md) | 1 | M | 09 | concluído | ok | [#8](https://github.com/mateusconrad/FanPilot/issues/8) | 65 testes C# + 4 JS; 2 de integração só rodam como Administrador (CI); painel conferido num navegador headless |
| 09 | [Projeto SDK-style (net472)](specs/09-projeto-sdk.md) | 1 | M | — | concluído | ok | [#9](https://github.com/mateusconrad/FanPilot/issues/9) | falta a verificação em hardware |
| 10 | [Ponte Gigabyte sem dependência de compilação](specs/10-gigabyte-sem-compilacao.md) | 2 | M | 09 | concluído | ok | [#10](https://github.com/mateusconrad/FanPilot/issues/10) | contrato conferido com o GCC 8.0.8.2 instalado; falta a verificação em hardware |
| 11 | [CI, versão e releases](specs/11-ci-versao-release.md) | 2 | P–M | 08, 09, 10 | concluído | ok | [#11](https://github.com/mateusconrad/FanPilot/issues/11) | CI verde (138 testes, sem ignorados) e ensaio da release aprovado; SDK fixado pelo `global.json`; a tag `v0.1.0` espera a verificação em hardware |
| 12 | [Assinatura de código](specs/12-assinatura.md) | 2 | P + aprovação | 11 | adiado | a fazer | [#12](https://github.com/mateusconrad/FanPilot/issues/12) | débito técnico (28/10/2026): o SignPath Foundation exige um site do projeto e outros requisitos ainda não definidos; as releases saem sem assinatura, com o `SHA256SUMS.txt`, e o README explica o aviso do Windows e como conferir o hash |
| 13 | [Relatos de compatibilidade](specs/13-compatibilidade.md) | 2 | P | — | concluído | ok | [#13](https://github.com/mateusconrad/FanPilot/issues/13) | `--diagnose` sem números de série nem GUIDs e com o link do formulário; falta ver o formulário no GitHub logado |
| 14 | [Serviço, bandeja e instalador](specs/14-servico-bandeja-instalador.md) | 3 | G | 03, 05, 06 | bloqueado | ok (14a) | [#14](https://github.com/mateusconrad/FanPilot/issues/14) | 14a feita (`--service`, `--data`, `--import-data`, testada na CI com um serviço de verdade); parada no ponto de verificação do serviço na B760M antes da 14b e da 14c; 14b começada antes dele, a pedido do dono: o ícone na bandeja (estado, tooltip com CPU, GPU e alertas, clique abre o `--tui`, menu com o painel) no logon pelo `autostart.ps1`; faltam Parear, Turbo, Perfis, Ver log e as notificações |
| 15 | [Curvas por software suaves](specs/15-curvas-suaves.md) | 3 | M | 08 | concluído | ok | [#15](https://github.com/mateusconrad/FanPilot/issues/15) | `SoftwareCurveEngine` com resposta Rápida/Equilibrada/Suave; falta a carga real numa placa no caminho LHM |
| 16 | [Alertas de fan parado e temperatura](specs/16-alertas.md) | 3 | M | 02 | concluído | ok | [#16](https://github.com/mateusconrad/FanPilot/issues/16) | painel, ntfy e Telegram; a notificação do Windows fica para a bandeja (14b); falta o ntfy no celular e um limite baixo na B760M |
| 17 | [Nomes dos fans e papel "bomba"](specs/17-nomes-e-bomba.md) | 3 | P–M | 04 | concluído | ok | [#17](https://github.com/mateusconrad/FanPilot/issues/17) | política da bomba no `BuildCurve`, por onde passam editor, Turbo, reaplicação e os perfis do 18; falta conferir na B760M |
| 18 | [Perfis globais](specs/18-perfis.md) | 3 | P–M | 07, 17 | concluído | ok | [#18](https://github.com/mateusconrad/FanPilot/issues/18) | modo global com o Turbo por cima, predefinições no backend; o menu da bandeja vem com a 14b; falta conferir na B760M |
| 19 | [Temperatura dos núcleos na Gigabyte](specs/19-temperatura-cpu-gigabyte.md) | 3 | P–M | — | concluído | ok | [#19](https://github.com/mateusconrad/FanPilot/issues/19) | papel `cpu-main` nas leituras; LibreHardwareMonitor só com a CPU ao lado do GCC; falta conferir na B760M com o PawnIO |
| 20 | [Editor acessível e com ajuste fino](specs/20-acessibilidade-editor.md) | 3 | P | — | concluído | ok | [#20](https://github.com/mateusconrad/FanPilot/issues/20) | pontos como sliders, barra de ajuste fino, folha com foco, Esc, `inert` e Tab em volta, alvos de 44 px (`movePoint` compartilhada); falta TalkBack e VoiceOver num celular |
| 21 | [Inglês e internacionalização](specs/21-internacionalizacao.md) | 4 | M | — | concluído | ok | [#21](https://github.com/mateusconrad/FanPilot/issues/21) | painel, mensagens do backend (por código), console e relatórios nos dois idiomas; log em inglês; README principal em inglês com o `README.pt-BR.md`; CHANGELOG ainda em português (decidir antes da `v0.1.0`) |
| 22 | [Modo painel](specs/22-modo-painel.md) | 4 | P–M | — | concluído | ok | [#22](https://github.com/mateusconrad/FanPilot/issues/22) | `/panel` só de leitura com layout por formato de tela, tela cheia, tela ligada por Wake Lock ou vídeo mudo, proteção OLED; falta conferir num Android por 30 min |
| 23 | [Integrações (Prometheus, Home Assistant)](specs/23-integracoes.md) | 4 | M | 01, 09 | concluído | ok | [#23](https://github.com/mateusconrad/FanPilot/issues/23) | tokens no `/pair`, `/metrics`, `/api/v1/sensors` e MQTT com descoberta do Home Assistant (MQTTnet 4.3.7), configurado no painel; falta conferir num Prometheus e num Home Assistant |
| 24 | [Histórico persistente](specs/24-historico.md) | 4 | M | — | concluído | ok | [#24](https://github.com/mateusconrad/FanPilot/issues/24) | linhas de 10 s (média e máximo) em `data\history\AAAA-MM-DD.csv`, 7 dias por padrão (`historyDays`); 1 h, 24 h e 7 dias na API em até 600 pontos e no seletor dos gráficos; falta conferir no celular |
| 25 | [Fonte de temperatura combinada](specs/25-fonte-combinada.md) | 4 | P–M | 15 | concluído | ok | [#25](https://github.com/mateusconrad/FanPilot/issues/25) | `source` = sensor, `gpu` ou `max` nas curvas por software; a Gigabyte recusa; falta uma placa no caminho LHM com GPU |
| 26 | [Testes de ponta a ponta (painel e executável)](specs/26-testes-ponta-a-ponta.md) | 2 | M | 08, 11 | concluído | ok | [#26](https://github.com/mateusconrad/FanPilot/issues/26) | 36 testes no Edge (~1 min na CI) e smoke do zip; acharam e corrigiram a reconexão do SSE depois de um 503 e o contraste do texto secundário |
| 27 | [Teste automatizado na placa real](specs/27-teste-em-hardware.md) | 2 | P–M | 08 | concluído | ok | [#27](https://github.com/mateusconrad/FanPilot/issues/27) | falta a primeira execução na B760M |
| 28 | [Linux](specs/28-linux.md) | 5 | G + M | 09, 11, 14a, 15 | concluído | ok | [#35](https://github.com/mateusconrad/FanPilot/issues/35) | `net472` e `net10.0`, placa pelo hwmon do kernel, GPU pelo NVML e pelo `amdgpu`, `--restore-fans`, `--diagnose` do Linux, serviço do systemd e `.tar.gz` com `install.sh` (testado na CI num runner com systemd); a 28c foi feita antes do ponto de parada da 28b, que segue pendente num PC com Linux; o caminho Gigabyte fica só no Windows |
| 29 | [Interface no terminal](specs/29-interface-terminal.md) | 6 | M | 21, 28a | concluído | ok | [#36](https://github.com/mateusconrad/FanPilot/issues/36) | `FanPilot` num terminal mostra temperaturas e coolers (RPM e %, `fanDuty` no snapshot) com barras e sparklines, o link do painel e o Turbo; `--tui` liga-se ao serviço sem elevação; `--log` mantém o log; testada num pseudo-terminal na CI do Ubuntu; visual aprovado no Windows Terminal e no conhost em 29/10/2026; faltam as teclas no Windows (no Linux, `t` e `q` passam no pseudo-terminal da CI) e o terminal gráfico do Linux, junto com a 28b |
| 30 | [Partida sem janela](specs/30-partida-sem-janela.md) | 6 | P–M | 14a, 28c, 29 | concluído | ok | [#37](https://github.com/mateusconrad/FanPilot/issues/37) | `autostart.ps1` instala o serviço do Windows (sem janela, antes do logon) e o atalho do menu Iniciar abre o `--tui` sem UAC; `FanPilot.exe` com o serviço rodando mostra a tela dele; no Linux, `fanpilot` no PATH e no menu; feito antes do ponto de parada da 14a; falta reiniciar a B760M e um PC com Linux |

## Ordem de execução

### Fase A · Onda 1 · Estabilidade e testes (≈ 3 semanas)

`01 → 09 → 08 → 03 → 05 → 02 → 04 → 06 → 07`

- O 01 vem primeiro: é a única falha de segurança alcançável de fora do PC, e a correção é pequena. O teste de regressão dele é escrito no 08.
- A infraestrutura de testes (09 → 08) vem logo em seguida, para que todos os outros itens já saiam com testes automatizados.
- O 03 vem antes do 04: o 04 muda o formato dos arquivos de estado e usa o `DataFile` do 03.

### Fase B · Onda 2 · Entrega (≈ 3 semanas, mais a aprovação da assinatura)

`27 → 10 → 11 → 26 → 13`, com o 12 em paralelo

- O 27 vem primeiro: automatiza o roteiro de hardware antes do 10, a mudança mais arriscada no caminho Gigabyte.
- O 10 vem antes do 11: sem ele, a CI não gera a release completa.
- O 26 vem depois do 11, para os testes de ponta a ponta já rodarem na CI.
- A assinatura (12) ficou como débito técnico: os requisitos do SignPath (site do projeto e outros) ainda não estão definidos. As releases saem sem assinatura até lá.

### Fase C · Onda 3 · Produto (≈ 4,5 semanas)

`15 → 17 → 14 (14a → 14b → 14c) → 16 → 18 → 19 → 20`

- O 15 vem cedo: é o maior ganho nas placas que não são Gigabyte e prepara o motor de curvas para o 25.
- O 17 vem antes do 14, do 16 e do 18: bomba e nomes aparecem na bandeja, nos alertas e nos perfis.
- O 14a tem um ponto de parada: confirmar que GCC, NVML e PawnIO funcionam rodando como serviço antes de seguir para o 14b e o 14c.
- O 16 vem depois do 14: a notificação no Windows sai pela bandeja.

### Fase D · Onda 4 · Crescimento (≈ 2,5 semanas)

`21 → 22 → 25 → 24 → 23`

- O 21 vem primeiro: o que vier depois já nasce nos dois idiomas.

### Fase E · Onda 5 · Plataformas (≈ 3 semanas)

`28a → 28b → 28c`

- Começa pela troca do SDK para o 10 (veja Decisões): o Linux roda no .NET 10, e o 8 sai do suporte em 10/11/2026.
- O 28a não toca no hardware: o FanPilot sobe no Linux com a placa falsa e a CI do Ubuntu passa a rodar os testes. Os
  itens seguintes do roadmap já saem testados nos dois sistemas.
- O 28b tem um ponto de parada: o `--hardware-test` num PC de verdade com Linux, antes de investir no serviço e no
  pacote. A B760M serve se o `it87` reconhecer o IT8689E; se não, outra placa. Em 29/10/2026 o dono liberou a 28c antes
  dele; o ponto de parada continua valendo como verificação em hardware.
- Na prática, a Fase E começou antes da C terminar (14b e 14c esperam a verificação na B760M): o Linux não depende da
  bandeja nem do instalador.

### Fase F · Onda 6 · Uso no PC (≈ 1 semana)

`29 → 30`

- O 29 vem primeiro e não depende do serviço: no console, a interface substitui o log, e o `--tui` se liga a qualquer
  FanPilot rodando.
- O 30 espera o ponto de parada da 14a (o serviço na B760M) e a 28c (o systemd): ele só torna o serviço a partida
  padrão e põe o atalho da interface no menu.

**Total estimado:** ≈ 81 dias de trabalho focado (≈ 16 semanas), com o 28, o 29 e o 30.

## Estratégia de testes

| Camada | O quê | Ferramenta | Onde roda | Item |
|---|---|---|---|---|
| Unitários | Regras de curva, persistência, pareamento, motores (curvas, alertas, perfis) | xUnit e `node --test` | Localmente antes do push; CI a cada push | 08 e cada item |
| Integração | Servidor web com a placa falsa: rotas, autenticação, SSE | xUnit | Localmente antes do push; CI a cada push | 08 e cada item |
| Ponta a ponta | Painel num navegador de verdade; smoke do zip da release (e do instalador, no 14) | Playwright | CI a cada push | 26 |
| Hardware | Roteiro de verificação na placa real | `FanPilot.exe --hardware-test` | B760M, como Administrador | 27 |

**Regra:** todo item entrega testes automatizados do que muda, e só é concluído com eles passando. A única exceção é o 01, que veio antes da infraestrutura e ganha seu teste de regressão no 08.

## Como cada item é entregue

- Direto na `main`, sem branches nem PRs: um ou mais commits por item, todos com `(#N)` na mensagem; o último fecha a issue com `Closes #N`.
- Antes de cada push: build e `dotnet test` passando localmente. A CI confere de novo a cada push, e um build vermelho é corrigido antes do próximo item.
- Código, testes automatizados e documentação quando o usuário percebe a mudança: README e uma linha em "Não lançado" no `CHANGELOG.md`.
- Item que mexe em fans, GPU, ponte da placa ou build: rodar `FanPilot.exe --hardware-test` na B760M e anexar o relatório (`data\hwtest-*.md`) na issue. Enquanto isso não acontecer, o item entra em "Verificação em hardware pendente".

## Como o andamento é registrado

- **Issue:** ao começar, label `status: em andamento` e um comentário; o commit final fecha a issue pelo `Closes #N`, com um comentário do que foi feito; impedimento → `status: bloqueado` e um comentário com o motivo; adiado → label `débito técnico`, sem milestone, e um comentário com o motivo; concluído sem a verificação em hardware → label `verificar no hardware`.
- **Este arquivo:** Status, Testes, Notas e o contador são atualizados no mesmo commit que conclui o item.
- **Spec:** se a implementação mudar a solução, a spec (e o corpo da issue) é atualizada junto.

## Verificação em hardware pendente

Itens já na `main` cujo comportamento na placa real ainda não foi conferido. Para conferir: fechar o FanPilot aberto, rodar `.\build.ps1` e, como Administrador, `bin\FanPilot.exe --hardware-test`: ele cobre o roteiro abaixo e grava `data\hwtest-<data>.md` para anexar nas issues. O que ele não mede (o celular, a comparação com o HWiNFO, os casos de cada item) está na tabela.

| Item | O que conferir |
|---|---|
| 10 | `--hardware-test` passa com o exe compilado sem referência ao GCC; o log mostra "Gigabyte Control Center 8.0.8.2 at …"; `--diagnose` mostra a versão e "compatível" |
| 11 | O zip feito pela CI (artefato `FanPilot-0.1.0-win-x64.zip` da última execução em [Actions](https://github.com/mateusconrad/FanPilot/actions)), extraído numa pasta nova com uma cópia da `data\` do `bin\`, passa no `--hardware-test`; o log começa com "FanPilot 0.1.0+<commit>" e o rodapé do painel mostra a mesma versão. A `data\` do `bin\` não é tocada, e a pasta de teste pode ser apagada depois. É esse zip que a `v0.1.0` vai publicar |
| 29 | O visual já foi aprovado. Falta, no Windows Terminal e no conhost, com a placa falsa (`src\bin\Debug\FanPilot.exe --fake-board`) ou a B760M: `o` abre o painel no navegador; `t` liga e desliga o Turbo (o cabeçalho mostra TURBO, e o `--tui` da outra janela acompanha); `l` mostra o log e volta; `q` no `--tui` fecha só ele; `q` e Ctrl+C no FanPilot o fecham devolvendo os fans; redimensionar redesenha |
| 30 | Na B760M, depois do `.\scripts\autostart.ps1`: reiniciar o PC não abriu janela nenhuma (conferido em 29/10/2026), mas as curvas não voltaram no boot (o serviço subiu antes do `EasyTuneEngineService` responder, concluiu "sem controle de fans" e não perguntou de novo; um `Restart-Service FanPilot` as trouxe; corrigido depois: a ponte com o GCC pergunta de novo e o `FanManager` pega os fans quando aparecem). Falta, como Administrador, `.\scripts\autostart.ps1` com o exe novo e reiniciar de novo: o log de eventos mostra "third-generation fan control answers now" ou nenhum aviso de "match no fan", o painel e o celular respondem antes do logon e as curvas são as de antes; menu Iniciar > FanPilot abre a tela sem UAC; abrir o `bin\FanPilot.exe` mostra a tela ligada ao serviço. No PC com Linux da 28b, depois do `install.sh`: "FanPilot" no menu de aplicativos e `fanpilot --tui --port 8787` abrem a tela sem sudo |
| 14b | Na B760M, como Administrador: `.\scripts\autostart.ps1`; o ícone aparece na bandeja sem UAC, o tooltip mostra a CPU e a GPU, um clique abre a tela do terminal (no Windows Terminal e no conhost) e "Abrir o painel no navegador" abre o painel; parar o serviço deixa o ponto cinza e "não está respondendo", e iniciá-lo de novo o tira; reiniciar o PC: o ícone volta no logon, sem janela |
| 28b | O ponto de parada do item 28: o FanPilot num PC com Linux (um Ubuntu num pendrive na B760M serve, se o kernel reconhecer o IT8689E): `--diagnose`, `--hardware-test`, painel, Ctrl+C e `kill -9` com `--restore-fans` devolvendo os fans; e a interface no terminal (item 29) num terminal gráfico do Linux. Passo a passo na [spec 28](specs/28-linux.md#ponto-de-parada-como-verificar-num-pc-com-linux), agora também pelo pacote: `sudo ./install.sh` e o serviço no boot. A 28c já foi feita |
| 14a | O ponto de parada do item 14: o FanPilot como serviço na B760M (GCC como LocalSystem, NVML na sessão 0, suspensão, parada em menos de 1 s, partida antes do logon, caminho LHM). Passo a passo na [spec 14](specs/14-servico-bandeja-instalador.md#ponto-de-parada-como-verificar-na-b760m). Passando, o item segue para a bandeja e o instalador |
| 16 | Na B760M: em "Alertas", gerar um tópico do ntfy, assinar o mesmo tópico no app do ntfy no celular, salvar e "Enviar teste": a mensagem chega ao celular. Pôr o limite da CPU em 60 °C (abaixo da temperatura atual): em ~10 s aparece "CPU a … °C" na faixa e no celular; voltar o limite a 95 °C: o alerta termina 30 s depois e chega o "Resolvido" |
| 23 | Num Home Assistant com o add-on Mosquitto: no painel, "Home Assistant (MQTT)" com o endereço, o usuário e a senha do broker; o aparelho com o nome do PC aparece sozinho em Dispositivos, com CPU, GPU e fans mudando a cada 5 s; fechar o FanPilot deixa as entidades indisponíveis. Num Prometheus, o `scrape_config` do README com um token criado no `/pair`: o alvo fica UP; revogar o token: o alvo cai com 401 |
| 24 | No celular pareado, escolher "7 dias" num dos gráficos: a semana aparece em menos de 2 s. Depois de um dia de uso na B760M: fechar e abrir o FanPilot e conferir em "24 h" o dia anterior, interrompido só no tempo em que ficou fechado; `data\history\` com um arquivo por dia, de cerca de 1 MB |
| 22 | No celular pareado, em paisagem: abrir "Modo painel" no rodapé, tocar em "Manter a tela ligada" e em "Tela cheia" e deixar o celular parado por 30 min com o tempo de tela padrão (ex.: 1 min): a tela não apaga e os números continuam mudando |
| 20 | No celular pareado, com o TalkBack (Android) ou o VoiceOver (iPhone) ligado: abrir um fan; os pontos são anunciados como "Ponto 3 de 8" com a temperatura e a velocidade, e o gesto de ajuste muda a velocidade. Sem o leitor de tela: tocar num ponto não o move, o − °C + muda 1 °C por toque e segurar o + repete; fechar com uma mudança pergunta "Descartar alterações?" |
| 19 | Na B760M: sem o PawnIO, o card diz "CPU · socket" com a dica. Com o PawnIO instalado (`winget install namazso.PawnIO`) e o FanPilot reaberto, o log mostra "CPU temperature (…) from LibreHardwareMonitor", o card diz "CPU · pacote" com um valor próximo do "CPU Package" do HWiNFO, e o GCC continua respondendo (o `--hardware-test` passa) |
| 18 | Na B760M: anotar as curvas de cada fan, escolher "Silencioso" (o RPM cai em poucos segundos; a bomba não muda), ligar e desligar o Turbo (volta ao Silencioso), reiniciar o FanPilot (continua no Silencioso) e escolher "Personalizado": cada fan volta exatamente à curva anotada |
| 17 | Na B760M: marcar como bomba, em "Nome e função", o header em que ela estiver (o README desconfia do System 1): ele vai a 100%, o card mostra o selo "Bomba", e o Turbo ligado e desligado não o muda. Dar um nome a outro fan e reiniciar o FanPilot: nome e bomba continuam. O `--hardware-test` sem `--fan` não escolhe a bomba |
| 25 | Numa placa no caminho LHM, com GPU: um fan de gabinete com "Maior entre CPU e GPU" acelera num jogo que esquenta a GPU com a CPU folgada; com o driver da GPU reiniciando (ou desinstalado), o fan segue a CPU; com "GPU", volta à BIOS até a leitura voltar |
| 15 | Numa placa no caminho LHM (a B760M com `"backend": "lhm"`, se o LibreHardwareMonitor controlar o IT8689E): uma curva com resposta Equilibrada e carga intermitente (um benchmark em rajadas de poucos segundos) não faz o fan subir e descer audivelmente; com a Rápida, faz. Se precisar, ajustar τ, H e as rampas em `SoftwareCurveEngine.Responses` |
| 27 | A primeira execução do `--hardware-test` na B760M passa (`--fan 1`, o System 1) e devolve as 4 curvas suas como estavam |
| 01 | `curl.exe -H "Host: exemplo.invalid:8787" http://127.0.0.1:8787/api/fans` → `421`; sem o header → `200`; POST com `X-FanPilot: 1` e `Origin: http://exemplo.invalid:8787` em `/api/logout` → `403`; `/pair`, o painel no PC e o celular pareado funcionam como antes; o log registra a recusa uma vez só |
| 02 | GPU: Fixo 50% → Automático pelo painel. Painel aberto no celular por 10 min, com a tela apagando e voltando: continua atualizando, sem "dados atrasados". O log não mostra "Slow sensor reading" |
| 04 | Primeira execução depois do build: o log mostra a conversão para o formato com chave; os 4 fans continuam com o selo "FanPilot" e as mesmas curvas; nenhum aviso de configuração órfã; `data\` tem os `.v1`. Abrir de novo: nenhuma conversão repetida |
| 05 | Abrir o FanPilot duas vezes: a segunda diz que já está rodando, abre o painel e sai, e o log da primeira não registra mudança de fan. Com a porta ocupada (`python -m http.server 8787`): mensagem clara, saída com código 5, nenhum fan tocado. `--diagnose` com o FanPilot aberto avisa no relatório |
| 09 | Os 9 passos do roteiro abaixo com o exe gerado pelo build novo |

## Roteiro de verificação em hardware

Placa de referência: B760M GAMING X AX com Gigabyte Control Center 26.03.26.01. O `FanPilot.exe --hardware-test` (item 27) automatiza os passos 1 e 3 a 9; o 2 (celular, HWiNFO) continua à mão.

1. Abrir o FanPilot: nenhum `WARN` inesperado no log; backend e placa corretos.
2. Painel no PC e no celular: temperaturas, RPM e GPU batem com o GCC/HWiNFO.
3. System 1 → Fixo 40%: o RPM cai em poucos segundos. Restaurar original: o RPM volta.
4. System 1 com a curva "Padrão": 60 s sem "changed externally" no log.
5. Turbo ligado: todos os fans da placa em 100%. Desligado: cada um volta ao que estava.
6. GPU: Fixo 50% → Automático.
7. Fechar com Ctrl+C: mensagem de encerramento no log; na Gigabyte, as curvas continuam no chip.
8. Abrir de novo: curvas reaplicadas, sem loop no watchdog.
9. No fim, restaurar todos os fans alterados.

## Decisões

| Data | Decisão | Item | Situação |
|---|---|---|---|
| 24/10/2026 | O build vira projeto SDK-style para .NET Framework 4.7.2; compilar passa a exigir o .NET SDK | 09 | aprovada |
| 24/10/2026 | Testes automatizados obrigatórios em todo item; infraestrutura de testes (09 → 08) logo depois do 01; novos itens 26 e 27 | 08, 26, 27 | aprovada |
| 24/10/2026 | Uma branch e um PR por item; issues controladas por labels de status | todos | substituída pela de baixo |
| 24/10/2026 | Trabalho direto na `main`, sem branches nem PRs; commits com `(#N)` e o último com `Closes #N`; issues controladas por labels | todos | aprovada |
| 24/10/2026 | Primeira versão com tag: `v0.1.0`, criada depois da verificação em hardware dos itens pendentes | 11 | proposta |
| 24/10/2026 | SDK do .NET fixado pelo `global.json` (8.0), para o PC e a CI compilarem igual; passar para o SDK 10 antes do fim do suporte do 8 (10/11/2026) | 11 | aprovada; SDK 10 desde 28/10/2026 (28a) |
| 24/10/2026 | Assinatura pelo SignPath Foundation (Azure Trusted Signing como alternativa) | 12 | proposta |
| 24/10/2026 | Instalador com Inno Setup; dados da versão instalada em `%ProgramData%\FanPilot` | 14 | proposta |
| 24/10/2026 | Resposta "Equilibrada" como padrão, inclusive nas curvas por software já salvas | 15 | proposta |
| 24/10/2026 | Logs em inglês; README principal em inglês, com versão em português | 21 | proposta |
| 28/10/2026 | Suporte a Linux x64: alvo `net10.0` ao lado do `net472`, fans e sensores pelo hwmon do kernel, serviço do systemd; o caminho Gigabyte, a bandeja e o instalador ficam só no Windows | 28 | proposta |
| 28/10/2026 | No Linux, os segredos usam AES-GCM com uma chave em `/etc/fanpilot/secret.key`, fora da pasta de dados, no lugar do DPAPI | 28 | proposta |
| 29/10/2026 | Interface no terminal com renderização própria em ANSI, sem biblioteca (Spectre.Console como alternativa se crescer); no console, ela substitui o log rolando (`--log` mantém o de antes) | 29 | proposta |
| 29/10/2026 | A partida automática passa a ser o serviço (Windows e systemd), no lugar da tarefa agendada com janela; o atalho do menu abre a interface no terminal | 30 | proposta |

"Proposta" quer dizer que vale o que está escrito, a menos que você mude antes de o item ser implementado.

## Riscos

| Risco | Mitigação |
|---|---|
| Uma atualização do GCC muda a API interna | A checagem de contrato do item 10 detecta a mudança e cai para o LibreHardwareMonitor com aviso |
| Só existe uma placa de teste (B760M) | Forçar `"backend": "lhm"` nela, se o LHM controlar o IT8689E (conferir no início da Fase C), e usar os relatos estruturados do item 13 |
| GCC, NVML ou PawnIO não funcionarem rodando como serviço | Ponto de parada no 14a, antes de investir na bandeja e no instalador |
| A aprovação da assinatura demorar | Releases sem assinatura, com `SHA256SUMS.txt` e instruções de verificação |
| Chamadas IPC do GCC sem timeout | O item 02 isola a GPU e o HTTP delas; o risco fica registrado no item 10 |
| Testes em hardware exigem Administrador | O `--hardware-test` (27) roda na B760M pelo dono do repositório (ou numa sessão elevada) e o relatório vai na issue; até lá, os itens ficam em "Verificação em hardware pendente" |
| Sem PR, nada bloqueia um commit ruim na `main` | Build e testes locais antes de cada push; CI a cada push (item 11); verificação em hardware registrada |
| O kernel não controla o chip da placa (módulo ausente, conflito com a ACPI; o IT8689E da B760M talvez precise do `it87` de fora da árvore) | O `HwmonBoard` mostra o aviso com o que fazer e não toca nos fans; o `--diagnose` do Linux alimenta os relatos de compatibilidade (13); ponto de parada da 28b numa placa que o kernel controle |
| Um FanPilot morto com `kill -9` no Linux deixa os fans no último PWM manual | `--restore-fans` no `ExecStopPost` da unit, com os `pwmN_enable` originais guardados em `/run/fanpilot/` |
| Manter dois alvos (`net472` e `net10.0`) dobra o que pode quebrar | A CI compila e testa os dois a cada push; o que é de um sistema só fica fora do outro pelo `.csproj` e pela `Platform` |
| O console do Windows sem modo VT (anterior ao Windows 10 1607) ou um terminal sem cores desenha lixo na interface do 29 | Liga o VT por `SetConsoleMode` e, se não conseguir, cai no `--log`; testada no conhost e no Windows Terminal |
| O serviço como partida padrão (30) foi feito antes do ponto de parada da 14a, ainda não feito na B760M | O `autostart.ps1` é testado na CI com um serviço de verdade; se o GCC, o NVML ou o PawnIO falharem como serviço na B760M, o `autostart.ps1 -Remove` volta ao FanPilot aberto à mão até a correção |
| O suporte do SDK 8 termina em 10/11/2026 | Resolvido na 28a: o `global.json` pede o SDK 10, e o PC que compila precisa dele instalado. O FanPilot do Windows continua no .NET Framework 4.7.2 |
