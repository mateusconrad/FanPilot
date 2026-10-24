# Roadmap do FanPilot

Os 27 itens de melhoria aprovados em 24/10/2026: os 25 da análise inicial e mais dois de testes automatizados (26 e 27). Cada item tem uma spec em [`docs/specs/`](specs/) e uma issue no GitHub.

Este arquivo é a fonte de verdade do andamento. As issues mostram o mesmo estado por labels (veja "Como o andamento é registrado").

- **Status:** `a fazer` · `em andamento` · `concluído` · `bloqueado` (motivo em Notas)
- **Testes:** testes automatizados do item: `a fazer` · `ok` (commitados com o item; rodam na CI quando ela existir)
- **Esforço**, em dias de trabalho focado: P ≈ 1 · P–M ≈ 2 · M ≈ 3 · G ≈ 10

**Concluídos: 7 de 27** · verificação em hardware pendente: 01, 02, 04, 05, 09 (veja a seção própria)

## Andamento

| # | Item | Onda | Esforço | Depende de | Status | Testes | Issue | Notas |
|---|---|---|---|---|---|---|---|---|
| 01 | [Validar o header Host](specs/01-validar-host.md) | 1 | P | — | concluído | ok | [#1](https://github.com/mateusconrad/FanPilot/issues/1) | falta a verificação em hardware; testes de regressão no 08 |
| 02 | [SSE e proteção da GPU fora do laço de amostragem](specs/02-sse-e-protecao-gpu.md) | 1 | P–M | — | concluído | ok | [#2](https://github.com/mateusconrad/FanPilot/issues/2) | falta a verificação em hardware |
| 03 | [Leitura tolerante e gravação atômica](specs/03-arquivos-atomicos.md) | 1 | P | — | concluído | ok | [#3](https://github.com/mateusconrad/FanPilot/issues/3) | faixa de avisos nova no painel (`AppNotices`) |
| 04 | [Chave estável para cada header](specs/04-identidade-dos-headers.md) | 1 | P–M | 03 | concluído | ok | [#4](https://github.com/mateusconrad/FanPilot/issues/4) | migração v1→v2 testada numa cópia dos dados reais da B760M; falta a verificação em hardware |
| 05 | [Instância única e porta ocupada](specs/05-instancia-unica.md) | 1 | P | — | concluído | ok | [#5](https://github.com/mateusconrad/FanPilot/issues/5) | falta conferir como Administrador |
| 06 | [Rotação e deduplicação do log](specs/06-log.md) | 1 | P | — | a fazer | a fazer | [#6](https://github.com/mateusconrad/FanPilot/issues/6) | |
| 07 | [Correções pequenas](specs/07-correcoes-pequenas.md) | 1 | P | — | a fazer | a fazer | [#7](https://github.com/mateusconrad/FanPilot/issues/7) | |
| 08 | [Testes automatizados (unitários e integração)](specs/08-testes.md) | 1 | M | 09 | concluído | ok | [#8](https://github.com/mateusconrad/FanPilot/issues/8) | 65 testes C# + 4 JS; 2 de integração só rodam como Administrador (CI); painel conferido num navegador headless |
| 09 | [Projeto SDK-style (net472)](specs/09-projeto-sdk.md) | 1 | M | — | concluído | ok | [#9](https://github.com/mateusconrad/FanPilot/issues/9) | falta a verificação em hardware |
| 10 | [Ponte Gigabyte sem dependência de compilação](specs/10-gigabyte-sem-compilacao.md) | 2 | M | 09 | a fazer | a fazer | [#10](https://github.com/mateusconrad/FanPilot/issues/10) | |
| 11 | [CI, versão e releases](specs/11-ci-versao-release.md) | 2 | P–M | 08, 09, 10 | a fazer | a fazer | [#11](https://github.com/mateusconrad/FanPilot/issues/11) | Incluir na CI a checagem de BOM nos `.ps1` com acento (achado no item 09) |
| 12 | [Assinatura de código](specs/12-assinatura.md) | 2 | P + aprovação | 11 | a fazer | a fazer | [#12](https://github.com/mateusconrad/FanPilot/issues/12) | |
| 13 | [Relatos de compatibilidade](specs/13-compatibilidade.md) | 2 | P | — | a fazer | a fazer | [#13](https://github.com/mateusconrad/FanPilot/issues/13) | |
| 14 | [Serviço, bandeja e instalador](specs/14-servico-bandeja-instalador.md) | 3 | G | 03, 05, 06 | a fazer | a fazer | [#14](https://github.com/mateusconrad/FanPilot/issues/14) | |
| 15 | [Curvas por software suaves](specs/15-curvas-suaves.md) | 3 | M | 08 | a fazer | a fazer | [#15](https://github.com/mateusconrad/FanPilot/issues/15) | |
| 16 | [Alertas de fan parado e temperatura](specs/16-alertas.md) | 3 | M | 02 | a fazer | a fazer | [#16](https://github.com/mateusconrad/FanPilot/issues/16) | |
| 17 | [Nomes dos fans e papel "bomba"](specs/17-nomes-e-bomba.md) | 3 | P–M | 04 | a fazer | a fazer | [#17](https://github.com/mateusconrad/FanPilot/issues/17) | |
| 18 | [Perfis globais](specs/18-perfis.md) | 3 | P–M | 07, 17 | a fazer | a fazer | [#18](https://github.com/mateusconrad/FanPilot/issues/18) | |
| 19 | [Temperatura dos núcleos na Gigabyte](specs/19-temperatura-cpu-gigabyte.md) | 3 | P–M | — | a fazer | a fazer | [#19](https://github.com/mateusconrad/FanPilot/issues/19) | |
| 20 | [Editor acessível e com ajuste fino](specs/20-acessibilidade-editor.md) | 3 | P | — | a fazer | a fazer | [#20](https://github.com/mateusconrad/FanPilot/issues/20) | |
| 21 | [Inglês e internacionalização](specs/21-internacionalizacao.md) | 4 | M | — | a fazer | a fazer | [#21](https://github.com/mateusconrad/FanPilot/issues/21) | |
| 22 | [Modo painel](specs/22-modo-painel.md) | 4 | P–M | — | a fazer | a fazer | [#22](https://github.com/mateusconrad/FanPilot/issues/22) | |
| 23 | [Integrações (Prometheus, Home Assistant)](specs/23-integracoes.md) | 4 | M | 01, 09 | a fazer | a fazer | [#23](https://github.com/mateusconrad/FanPilot/issues/23) | |
| 24 | [Histórico persistente](specs/24-historico.md) | 4 | M | — | a fazer | a fazer | [#24](https://github.com/mateusconrad/FanPilot/issues/24) | |
| 25 | [Fonte de temperatura combinada](specs/25-fonte-combinada.md) | 4 | P–M | 15 | a fazer | a fazer | [#25](https://github.com/mateusconrad/FanPilot/issues/25) | |
| 26 | [Testes de ponta a ponta (painel e executável)](specs/26-testes-ponta-a-ponta.md) | 2 | M | 08, 11 | a fazer | a fazer | [#26](https://github.com/mateusconrad/FanPilot/issues/26) | |
| 27 | [Teste automatizado na placa real](specs/27-teste-em-hardware.md) | 2 | P–M | 08 | a fazer | a fazer | [#27](https://github.com/mateusconrad/FanPilot/issues/27) | |

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
- Pedir a assinatura (12) logo no início da fase: a aprovação é externa e pode demorar.

### Fase C · Onda 3 · Produto (≈ 4,5 semanas)

`15 → 17 → 14 (14a → 14b → 14c) → 16 → 18 → 19 → 20`

- O 15 vem cedo: é o maior ganho nas placas que não são Gigabyte e prepara o motor de curvas para o 25.
- O 17 vem antes do 14, do 16 e do 18: bomba e nomes aparecem na bandeja, nos alertas e nos perfis.
- O 14a tem um ponto de parada: confirmar que GCC, NVML e PawnIO funcionam rodando como serviço antes de seguir para o 14b e o 14c.
- O 16 vem depois do 14: a notificação no Windows sai pela bandeja.

### Fase D · Onda 4 · Crescimento (≈ 2,5 semanas)

`21 → 22 → 25 → 24 → 23`

- O 21 vem primeiro: o que vier depois já nasce nos dois idiomas.

**Total estimado:** ≈ 63 dias de trabalho focado (≈ 13 semanas).

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
- Antes de cada push: build e `dotnet test` passando localmente. A partir do 11, a CI confere de novo a cada push, e um build vermelho é corrigido antes do próximo item.
- Código, testes automatizados e documentação quando o usuário percebe a mudança (README e, a partir do 11, CHANGELOG).
- Item que mexe em fans, GPU, ponte da placa ou build: rodar o teste em hardware (item 27; até ele existir, o roteiro manual abaixo) e registrar o resultado na issue. Enquanto isso não acontecer, o item entra em "Verificação em hardware pendente".

## Como o andamento é registrado

- **Issue:** ao começar, label `status: em andamento` e um comentário; o commit final fecha a issue pelo `Closes #N`, com um comentário do que foi feito; impedimento → `status: bloqueado` e um comentário com o motivo; concluído sem a verificação em hardware → label `verificar no hardware`.
- **Este arquivo:** Status, Testes, Notas e o contador são atualizados no mesmo commit que conclui o item.
- **Spec:** se a implementação mudar a solução, a spec (e o corpo da issue) é atualizada junto.

## Verificação em hardware pendente

Itens já na `main` cujo comportamento na placa real ainda não foi conferido. Para conferir: fechar o FanPilot aberto, rodar `.\build.ps1` e abrir o `bin\FanPilot.exe` como Administrador. Com o item 27 pronto, `FanPilot.exe --hardware-test` cobre a maior parte disso de uma vez.

| Item | O que conferir |
|---|---|
| 01 | `curl.exe -H "Host: exemplo.invalid:8787" http://127.0.0.1:8787/api/fans` → `421`; sem o header → `200`; POST com `X-FanPilot: 1` e `Origin: http://exemplo.invalid:8787` em `/api/logout` → `403`; `/pair`, o painel no PC e o celular pareado funcionam como antes; o log registra a recusa uma vez só |
| 02 | GPU: Fixo 50% → Automático pelo painel. Painel aberto no celular por 10 min, com a tela apagando e voltando: continua atualizando, sem "dados atrasados". O log não mostra "Leitura dos sensores lenta" |
| 04 | Primeira execução depois do build: o log mostra a conversão para o formato com chave; os 4 fans continuam com o selo "FanPilot" e as mesmas curvas; nenhum aviso de configuração órfã; `data\` tem os `.v1`. Abrir de novo: nenhuma conversão repetida |
| 05 | Abrir o FanPilot duas vezes: a segunda diz que já está rodando, abre o painel e sai, e o log da primeira não registra mudança de fan. Com a porta ocupada (`python -m http.server 8787`): mensagem clara, saída com código 5, nenhum fan tocado. `--diagnose` com o FanPilot aberto avisa no relatório |
| 09 | Os 9 passos do roteiro abaixo com o exe gerado pelo build novo |

## Roteiro de verificação em hardware

Placa de referência: B760M GAMING X AX com Gigabyte Control Center 26.03.26.01. O item 27 automatiza este roteiro.

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
| 24/10/2026 | Primeira versão com tag: `v0.1.0` | 11 | proposta |
| 24/10/2026 | Assinatura pelo SignPath Foundation (Azure Trusted Signing como alternativa) | 12 | proposta |
| 24/10/2026 | Instalador com Inno Setup; dados da versão instalada em `%ProgramData%\FanPilot` | 14 | proposta |
| 24/10/2026 | Resposta "Equilibrada" como padrão, inclusive nas curvas por software já salvas | 15 | proposta |
| 24/10/2026 | Logs em inglês; README principal em inglês, com versão em português | 21 | proposta |

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
