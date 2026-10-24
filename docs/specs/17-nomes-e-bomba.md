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

   "Restaurar original" continua permitido (é a curva da BIOS/GCC).
4. **Painel:** o card mostra o nome dado (o original fica em segundo plano) e o selo "Bomba". O editor ganha a seção "Nome e função", e gráficos e legendas passam a usar o nome dado.
5. **API:** `POST /api/fans/{i}/meta` com `{label, role, pumpPolicy, pumpMinPercent}`. O nome vai de 1 a 32 caracteres; nome vazio volta ao original.
6. Alertas (item 16): bomba parada é alerta crítico.

## Critérios de aceite

- [ ] O header "SYS_FAN5_PUMP" aparece como bomba sem nenhuma configuração.
- [ ] Com a bomba em `full`, aplicar 30% nela resulta em 100%, e o Turbo e os perfis não a alteram.
- [ ] O nome dado sobrevive a reinício e a reordenação dos headers.

## Testes

- Automatizados: detecção, política aplicada em todos os caminhos de escrita, persistência pela chave.
- Manuais: mock com o fixture de pior caso (que tem bomba) e B760M.
