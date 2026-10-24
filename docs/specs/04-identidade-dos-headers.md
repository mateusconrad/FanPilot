# Identificar cada header por uma chave estável, não pela posição

- **Item:** 04
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** confiabilidade, segurança
- **Esforço:** P–M
- **Depende de:** 03

## Problema

- As curvas salvas, o backup das originais e o estado do Turbo são indexados pela posição do header (`src/FanManager.cs:37-41`, `:118-140`, `:285-299`, `:301-322`). Na hora de reaplicar, nada confere se o header naquela posição ainda é o mesmo.
- No LibreHardwareMonitor, a lista segue a ordem dos sensores de controle (`src/LhmBoard.cs:94-100`). Uma atualização do LHM, da BIOS ou uma mudança no chip detectado pode reordenar ou remover headers. No GCC, uma atualização pode fazer o mesmo.
- `MachineData` detecta troca de PC ou de placa, mas não uma reordenação.
- Pior caso: a curva de 25% de um fan de gabinete vai parar no header da bomba.

## Objetivo

Uma configuração salva só é aplicada ao header para o qual foi feita.

## Solução

1. **`FanCurve.Key`**, uma chave estável por header:
   - LibreHardwareMonitor: o `Identifier` do sensor de controle (ex.: `/lpc/<chip>/…/control/1`);
   - GCC: `gcc:<índice>:<título>` (o GCC não expõe nada mais estável que índice e título).
2. **Formato v2** de `desired-curves.json`, `backup-curves.json` e do estado do Turbo: `{"version": 2, "fans": {"<key>": {…, "title": "…", "index": N}}}`, gravado pelo `DataFile` (item 03).
3. **Migração v1 → v2** na primeira leitura: para cada índice salvo, se o header atual nesse índice tem o mesmo título da curva salva, a entrada adota a chave dele. Se não tiver, a entrada vira **órfã** e não é aplicada.
4. **Reaplicar, watchdog, restaurar e Turbo** convertem a chave no índice atual. Chave sem header correspondente → nada é aplicado.
5. **Órfãs visíveis:** `/api/fans` inclui `orphans: [{title, key}]`, e o painel mostra um aviso ("A configuração salva para 'System 2' não corresponde a nenhum fan desta placa e não foi aplicada") com o botão "Descartar".
6. A API pública continua usando o índice (válido durante a sessão), e `/api/fans` passa a incluir `key` em cada fan.

## Fora de escopo

- Nomes personalizados e papel "bomba" (item 17), que vão usar esta mesma chave.

## Critérios de aceite

- [ ] Placa falsa que inverte a ordem de dois headers entre duas execuções: cada curva continua no seu header.
- [ ] Header que desaparece: a curva dele não é aplicada a nenhum outro, e o painel mostra o aviso.
- [ ] Na B760M, os arquivos v1 atuais migram sem perder nenhuma curva (mesma ordem e mesmos títulos).

## Testes

- Automatizados: migração v1; reordenação; header removido; Turbo com o estado anterior guardado por chave.
- Manuais: migração na B760M com o `data\` atual (fazer uma cópia antes).
