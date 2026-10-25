# Relatos de compatibilidade estruturados

- **Item:** 13
- **Onda:** 2 · Entrega
- **Tipo:** comunidade
- **Esforço:** P
- **Depende de:** —

## Problema

O suporte a placas que não são Gigabyte está "em teste, ainda sem placa confirmada" (README). O guia `docs/TESTANDO-OUTRAS-PLACAS.md` pede uma issue em texto livre com modelo, diagnóstico e resultado. Relatos sem estrutura são difíceis de comparar e não viram uma lista de compatibilidade.

## Objetivo

Cada teste numa placa nova vira um dado comparável, e a lista de placas confirmadas fica pública.

## Solução

1. **`.github/ISSUE_TEMPLATE/compatibilidade.yml`** (formulário de issue):
   - modelo da placa (obrigatório), versão da BIOS, fabricante (lista), caminho usado (Gigabyte Control Center ou LibreHardwareMonitor), versão do FanPilot;
   - etapas concluídas (caixas: diagnóstico, monitoramento, fan fixo, curva, devolução à BIOS ao fechar);
   - resultado (funcionou, parcial, não funcionou), detalhes e um campo para anexar o `diagnostico-….txt` e o `fanpilot.log`.
2. **`.github/ISSUE_TEMPLATE/bug.yml`** e **`config.yml`** (com link para o guia de testes).
3. **Labels:** `compatibilidade`, `placa-confirmada`, `placa-sem-suporte`.
4. **`docs/COMPATIBILIDADE.md`**: tabela com placa, chip, caminho, monitoramento, controle, versão testada e relato (#issue), começando pela B760M GAMING X AX.
5. **`--diagnose`** termina mostrando o link para abrir o relato já no formulário, com o modelo da placa no título e os campos da placa e da versão preenchidos (`?template=compatibilidade.yml&title=…&placa=…&versao=…`). O mesmo link vai no começo do arquivo: aberto de um terminal sem elevação, o console elevado fecha antes de dar para ler, e o arquivo abre no Bloco de Notas.
6. **Privacidade do diagnóstico:** o arquivo é feito para ser publicado numa issue. O relatório do LibreHardwareMonitor inclui, sim, `Motherboard Serial`, `System Enclosure Serial`, `System Enclosure Asset Tag` e `Processor Serial`, além de GUIDs (4 campos e 4 GUIDs na B760M). `Diagnose.Redact` troca por "(omitido)" o valor de qualquer campo cujo nome tenha serial, UUID, GUID, asset tag ou service tag, e qualquer GUID solto. O modelo da placa, os part numbers das memórias e o SKU ficam: identificam o modelo, não a máquina.
7. README (seção Compatibilidade) e guia de testes passam a apontar para o formulário e para a tabela.

## Critérios de aceite

- [ ] "New issue" no GitHub mostra o formulário de compatibilidade. Os links do `config.yml` já aparecem na API do GitHub; a página dos formulários só abre com login, e fica para conferir no navegador.
- [x] `--diagnose` mostra o link com o título já preenchido (no console e no começo do arquivo).
- [x] O arquivo do diagnóstico não contém números de série (conferido com o relatório real da B760M: 4 campos e 4 GUIDs antes, nenhum depois).
- [x] `docs/COMPATIBILIDADE.md` existe e tem link no README.

## Testes

- Automatizados (`DiagnoseTests`): a remoção de números de série, etiquetas e UUIDs com linhas no formato do LibreHardwareMonitor (`
`, como no arquivo real: um primeiro filtro com `$` em modo multilinha não removia nada, e o teste pegou), o modelo preservado e o link com placa, versão e título.
