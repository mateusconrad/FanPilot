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
5. **`--diagnose`** termina mostrando o link para abrir o relato já no formulário, com o modelo da placa no título.
6. **Privacidade do diagnóstico:** o arquivo é feito para ser publicado numa issue. Conferir se o relatório do LibreHardwareMonitor (`src/Diagnose.cs:51-58`) inclui números de série ou UUIDs e, se incluir, removê-los antes de salvar.
7. README (seção Compatibilidade) e guia de testes passam a apontar para o formulário e para a tabela.

## Critérios de aceite

- [ ] "New issue" no GitHub mostra o formulário de compatibilidade.
- [ ] `--diagnose` mostra o link com o título já preenchido.
- [ ] O arquivo do diagnóstico não contém números de série.
- [ ] `docs/COMPATIBILIDADE.md` existe e tem link no README.

## Testes

- Automatizados: a remoção de números de série e UUIDs do diagnóstico, com um relatório de exemplo.
