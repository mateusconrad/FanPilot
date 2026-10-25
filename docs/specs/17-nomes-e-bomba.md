# Nome próprio para cada fan e papel "bomba"

- **Item:** 17
- **Onda:** 3 · Produto
- **Tipo:** produto, segurança
- **Esforço:** P–M
- **Depende de:** 04

## Problema

- Os nomes vêm do chip ("System 1", "SYS_FAN2") e não dizem onde o fan está.
- O README pede que o usuário descubra sozinho qual header é a bomba do watercooler e lembre de mantê-la em 100%, porque "bomba lenta reduz muito a refrigeração" (README, observações da B760M). Nada no FanPilot impede aplicar 25% numa bomba, nem impede o Turbo ou um perfil de mexer nela.

## Objetivo

O usuário reconhece cada fan pelo nome que deu, e a bomba fica protegida de configurações perigosas.

## Solução

1. **Metadados por header**, guardados pela chave estável do item 04 em `fan-meta.json`: `label`, `role` (`fan` ou `pump`), `pumpPolicy` (`full` ou `min`) e `pumpMinPercent` (padrão 70).
2. **Detecção automática:** título com "pump", "bomba" ou "AIO" → `pump` por padrão (o usuário pode trocar).
3. **Política da bomba aplicada no `FanManager`**, valendo para o editor, o Turbo, os perfis e a reaplicação:
   - `full`: curva plana em 100%, qualquer que seja o pedido;
   - `min`: piso igual a `pumpMinPercent`.

   "Restaurar original" continua permitido (é a curva da BIOS/GCC). A política entra no `BuildCurve`, por onde passa toda escrita, então vale também para o que vier depois (perfis, item 18). O Turbo continua pondo todos os headers em 100%, bombas inclusive: nunca é pior para uma bomba, e uma em `full` não muda nem ao ligar nem ao desligar o Turbo.
   Mudar a função ou a proteção grava na hora: o pedido salvo do header de novo (agora com a política nova), ou 100% para uma bomba em `full` que o FanPilot ainda não controlava. Só o nome não grava nada. Durante o Turbo, mudar a função ou a proteção é recusado (o nome pode).
4. **Painel:** o card mostra o nome dado (o original fica em segundo plano) e o selo "Bomba". O editor ganha a seção "Nome e função", e gráficos e legendas passam a usar o nome dado.
5. **API:** `POST /api/fans/{i}/meta` com `{label, role, pumpPolicy, pumpMinPercent}`. O nome vai de 1 a 32 caracteres, sem quebras de linha; nome vazio volta ao original. O mínimo da bomba vai de 50% a 100%. `GET /api/fans` traz `label`, `role`, `pumpPolicy` e `pumpMinPercent` de cada fan. O `fan-meta.json` é dos arquivos que vão para `outra-maquina-…` quando a pasta vem de outro PC, como as curvas.
   O `--hardware-test` recusa o header da CPU e qualquer bomba (pelo nome ou marcada pelo usuário), usando o mesmo `role`.
6. Alertas (item 16): bomba parada é alerta crítico.

## Critérios de aceite

- [x] O header "SYS_FAN5_PUMP" aparece como bomba sem nenhuma configuração.
- [x] Com a bomba em `full`, aplicar 30% nela resulta em 100%, e o Turbo e os perfis não a alteram (os perfis do item 18 passam pelo mesmo `BuildCurve`).
- [x] O nome dado sobrevive a reinício e a reordenação dos headers.

## Testes

- Automatizados (`FanMetaTests`): detecção pelo nome, política `full` e `min` no editor, no Turbo e na reaplicação, marcar como bomba grava na hora, só o nome não grava nada, validação, Turbo bloqueando a troca de proteção, "Restaurar original" permitido, nome seguindo o header depois de reinício e reordenação; `MachineDataTests` com o `fan-meta.json`. De ponta a ponta, no cenário `worst`: o selo "Bomba", 30% virando 100%, e um nome dado no card e na legenda do gráfico que sobrevive a um reinício.
- Manuais: o `tools/ui-mock` (o pior caso tem bombas e um nome dado) e a B760M (verificação em hardware, `docs/ROADMAP.md`).
