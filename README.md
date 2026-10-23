<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/banner-dark.png">
    <img alt="FanPilot — temperaturas e fans da sua placa-mãe Gigabyte, no celular" src="assets/brand/banner-light.png" width="560">
  </picture>
</p>

# FanPilot

Monitor de temperatura e controle de fans pelo celular.

Painel web servido pelo próprio PC: temperaturas de CPU/placa-mãe/GPU, potência, clocks, gráfico dos últimos
10 minutos, curvas de fan da placa-mãe (editor com arrastar pontos) e fan da GPU NVIDIA. Acesso pela LAN só
depois de parear o celular escaneando um QR code exibido no PC.

Como funciona por baixo: veja [docs/REVERSE_ENGINEERING.md](docs/REVERSE_ENGINEERING.md).

> **Aviso:** projeto independente, **sem relação com a GIGABYTE** nem endossado por ela. "GIGABYTE", "Control
> Center", "EasyTune" e "NVIDIA" são marcas de seus respectivos donos. O FanPilot não distribui arquivos da
> Gigabyte: usa o GCC já instalado no seu PC. Ele altera o comportamento dos fans do seu hardware;
> **use por sua conta e risco** (veja as [proteções](#proteções-de-hardware)). Sem garantia, conforme a
> [licença](LICENSE).

## Requisitos

- **Placa-mãe Gigabyte** com o **Gigabyte Control Center** instalado e o serviço **EasyTune Engine Service** rodando
  (o FanPilot usa as DLLs cliente do GCC a partir da instalação, não as redistribui). A pasta do GCC é encontrada
  pelo registro do instalador, então não precisa estar no local padrão.
- Windows 10/11 64-bit (o .NET Framework 4.x necessário já vem no Windows).
- GPU NVIDIA com driver (usa `nvml.dll`). Sem GPU NVIDIA, o resto funciona.
- Executar como **Administrador** (exigência do serviço da Gigabyte; o `.exe` já pede elevação).

## Compilar e rodar

```powershell
.\build.ps1                 # usa o csc.exe do .NET Framework que vem no Windows
.\bin\FanPilot.exe            # pede UAC, abre http://localhost:8787/pair
.\scripts\firewall.ps1      # (admin, uma vez) libera a porta 8787 no perfil de rede Privado
.\scripts\autostart.ps1     # (admin, opcional) inicia elevado no logon, sem UAC
```

No celular (mesma rede Wi-Fi): escaneie o QR da página `/pair`. O código vale ~3 min e é de uso único;
o aparelho fica pareado por 30 dias de inatividade. A página `/pair` (só acessível no próprio PC) lista e
despareia aparelhos.

## Segurança

- Pareamento por QR: código aleatório de 144 bits, uso único, expira; tentativas erradas limitadas por IP.
- Sessão: cookie `HttpOnly; SameSite=Strict` com token de 256 bits; no disco só o SHA-256 (`bin\data\sessions.json`).
- `/pair` e `/api/pair/*` só respondem para `localhost`; requisições do próprio PC são consideradas confiáveis.
- POSTs exigem o header `X-FanPilot` e `Origin` igual ao host (bloqueia CSRF). CSP restringe scripts a `'self'`.
- **Limitação:** é HTTP puro na LAN. Quem estiver na mesma rede e capturar o tráfego pode copiar o cookie.
  Use só em rede doméstica confiável (o script de firewall abre a porta apenas no perfil Privado).

## Compatibilidade

| Recurso | Funciona em |
|---|---|
| Monitoramento (temperaturas, RPM, tensões, clocks, potência) | Placas Gigabyte suportadas pelo GCC |
| Controle de fans da placa-mãe | Placas em que o GCC usa o caminho de "3ª geração" (`SmartGuardianFanControlCoordinateSystem`). Nas outras, o painel avisa e mostra só o monitoramento. |
| Fan e sensores da GPU | GPUs NVIDIA |

Testado em: **B760M GAMING X AX** (GCC 26.03.26.01). O número de pontos da curva, os nomes dos headers e os
sensores de referência são lidos da própria placa. Se testar em outra placa, abra uma issue contando o modelo e o
resultado (os probes em `tools/` ajudam a diagnosticar).

Uma atualização do GCC pode mudar a API interna que o FanPilot usa e quebrar o programa até ele ser ajustado.

## Proteções de hardware

- Curvas da placa-mãe rodam **no chip ITE** — continuam valendo se o FanPilot fechar.
- Toda curva: o mesmo número de pontos que a placa usa (8 na B760M), temperaturas crescentes, PWM nunca diminui com a temperatura, mínimo de 20%,
  100% obrigatório no último ponto (≤ 90 °C). O ponto 0 fica em 0 °C (o chip nunca desliga o fan).
- Modo "Fixo": velocidade constante, mas o chip ainda vai a 100% acima de 85 °C.
- "Restaurar original" volta à curva salva no primeiro uso (`bin\data\backup-curves.json`).
- Watchdog (15 s) reaplica sua curva se algo a sobrescrever (resume do sleep, GCC, BIOS).
  Se quiser voltar a usar o GCC para fans, clique em "Restaurar original" antes.
- **Turbo** (botão no card dos fans): coloca todos os fans da placa-mãe em 100% sem tocar no fan da GPU.
  Enquanto ligado, a edição individual fica bloqueada; ao desligar, cada fan volta exatamente ao que estava
  (curva/fixo do FanPilot ou curva original). O estado sobrevive a um reinício (`bin\data\turbo.json`).
- Fan da GPU em modo fixo volta sozinho para automático se a GPU chegar a 83 °C ou quando o FanPilot fecha.

Limites ajustáveis em `bin\data\config.json` (`minDutyPercent`, `fixedModeSafetyTemp`, `maxFullSpeedTemp`,
`gpuSafetyTemp`, `port`, `publicHost`, `pairCodeTtlSeconds`, `sessionDays`).

## Operação

- **Parar:** feche a janela do FanPilot ou pressione Ctrl+C nela. Ao sair, o fan da GPU volta para automático;
  as curvas da placa-mãe ficam gravadas no chip.
- **Log:** `bin\data\fanpilot.log` (pareamentos, mudanças de fan, reaplicações do watchdog, erros).
- **Dados:** `bin\data\` guarda `config.json`, `sessions.json`, `backup-curves.json` (curvas originais, gravado
  só na primeira execução), `desired-curves.json` (curvas que o FanPilot mantém) e `machine.json` (identifica o PC
  e a placa-mãe).
- **Outro PC / troca de placa-mãe:** se `bin\data\` vier de outro PC ou de outra placa, o FanPilot não aplica as
  curvas, o backup nem os pareamentos antigos: eles são movidos para `bin\data\outra-maquina-<data>\` e tudo
  recomeça do zero (o `config.json` é mantido). Para levar o programa a outro PC, basta copiar `FanPilot.exe` e
  `wwwroot\`.
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
| Status "serviço Gigabyte indisponível" | `EasyTuneEngineService` parado (`services.msc`) ou GCC atualizado/removido. A GPU continua funcionando. |
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
  que usa o sensor CPU como referência). Confira fisicamente. **Se um header for a bomba, mantenha-o em 100%** —
  bomba lenta reduz muito a refrigeração. O gráfico de RPM omite headers que leem 0.
- Sensores de temperatura disponíveis para referência das curvas: System 1, PCH, CPU, PCIEX16, VRM MOS, System 2.
  A temperatura "CPU" é a do sensor da placa (socket), não a dos núcleos.

## Ferramentas de engenharia reversa (`tools/`)

Programas de teste usados para validar a análise; precisam rodar como Administrador e referenciam as DLLs do GCC
(compilar como no `build.ps1`).

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
| POST | `/api/fans/{i}` | `{"mode":"fixed","percent":40}` ou `{"mode":"curve","style":"SmartFanMode","points":[[0,30],…N pontos em %, N = pointCount do fan],"tempInput":2}` |
| POST | `/api/fans/{i}/restore` | volta à curva original |
| POST | `/api/fans/turbo` | `{"on":true}` todos os fans da placa-mãe em 100%; `{"on":false}` devolve cada fan ao estado anterior |
| POST | `/api/gpu/fan` | `{"mode":"auto"}` ou `{"mode":"fixed","percent":50}` |

## Estrutura

```
src/        C# 5 (.NET Framework 4.x): ponte Gigabyte, NVML, servidor HTTP/SSE, auth, política de fans
wwwroot/    painel (index.html/app.js), pareamento (pair.html/pair.js), qrcode-generator (MIT)
tools/      probes usados na engenharia reversa
assets/     identidade visual: SVGs-fonte, ícone do .exe, banners (regenerar: python assets/brand/build.py)
scripts/    firewall e auto-início
docs/       relatório da engenharia reversa
```

## Licença

[MIT](LICENSE). Componentes de terceiros: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
