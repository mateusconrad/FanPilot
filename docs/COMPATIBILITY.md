# Tested boards

Each row comes from a test on a real board, reported through the
[compatibility form](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml).
Tested on a board that isn't here, or on a newer version? Follow the
[testing guide](TESTING-OTHER-BOARDS.md) and report, even if it didn't work: knowing where it doesn't work helps too.

| Board | Sensor chip | Path | Monitoring | Fan control | Tested with | Report |
|---|---|---|---|---|---|---|
| Gigabyte B760M GAMING X AX | ITE IT8689E | Gigabyte Control Center 26.03.26.01 | yes | yes, curves in the chip | development builds (October 2026); 0.1.0 comes after `--hardware-test` | development board |

- **Path:** Gigabyte Control Center (the curves stay in the chip and keep working with FanPilot closed) or
  LibreHardwareMonitor (FanPilot runs the curves and, when it closes, hands the fans back to the BIOS). See the
  [README](../README.md#compatibility).
- **Fan control:** "yes" when fixed speed, curves and handing back on closing all worked; "partly" says what was
  missing.

## How a board gets on the list

1. The report arrives as an issue with the `compatibilidade` label.
2. It worked from the diagnostics to handing back on closing: the issue gets the `placa-confirmada` label and the
   board goes in the table.
3. FanPilot can't control (or even monitor) the board: the `placa-sem-suporte` label, and the board goes in the table
   with what worked and why, so nobody repeats the test for nothing.

## Em português

A tabela acima lista as placas testadas: o caminho usado (Gigabyte Control Center, com as curvas no chip, ou
LibreHardwareMonitor, com as curvas executadas pelo FanPilot), se o monitoramento e o controle de fans funcionaram, e
com qual versão. Para testar numa placa que não está aqui, siga o [guia de teste](TESTANDO-OUTRAS-PLACAS.md) e relate
pelo [formulário de compatibilidade](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml),
mesmo que não tenha funcionado.
