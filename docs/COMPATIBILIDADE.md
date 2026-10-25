# Placas testadas

Cada linha vem de um teste numa placa real, relatado pelo
[formulário de compatibilidade](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml).
Testou numa placa que não está aqui, ou numa versão mais nova? Siga o
[guia de teste](TESTANDO-OUTRAS-PLACAS.md) e relate, mesmo que não tenha funcionado: saber onde não funciona também
ajuda.

| Placa | Chip de sensores | Caminho | Monitoramento | Controle de fans | Testado com | Relato |
|---|---|---|---|---|---|---|
| Gigabyte B760M GAMING X AX | ITE IT8689E | Gigabyte Control Center 26.03.26.01 | sim | sim, curvas no chip | builds de desenvolvimento (outubro de 2026); a 0.1.0 entra depois do `--hardware-test` | placa de desenvolvimento |

- **Caminho:** Gigabyte Control Center (as curvas ficam no chip e continuam valendo com o FanPilot fechado) ou
  LibreHardwareMonitor (o FanPilot executa as curvas e, ao fechar, devolve os fans à BIOS). Veja o
  [README](../README.md#compatibilidade).
- **Controle de fans:** "sim" quando fixo, curva e a devolução ao fechar funcionaram; "em parte" diz o que faltou.

## Como uma placa entra na lista

1. O relato chega como issue com o label `compatibilidade`.
2. Funcionou do diagnóstico à devolução ao fechar: a issue ganha o label `placa-confirmada` e a placa entra na tabela.
3. O FanPilot não consegue controlar (ou nem monitorar) a placa: label `placa-sem-suporte`, e a placa entra na
   tabela com o que funcionou e o motivo, para ninguém repetir o teste sem necessidade.
