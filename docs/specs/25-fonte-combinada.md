# Fonte de temperatura combinada nas curvas por software

- **Item:** 25
- **Onda:** 4 · Crescimento
- **Tipo:** produto
- **Esforço:** P–M
- **Depende de:** 15

## Problema

Cada curva segue um único sensor. Os fans de gabinete deveriam reagir também à GPU, que é quem mais aquece o gabinete em jogos, mas a GPU não aparece como opção de referência.

## Objetivo

Nas placas em que o FanPilot executa as curvas, permitir a GPU e "a maior entre CPU e GPU" como referência.

## Solução

1. **Fonte de temperatura** na requisição da curva: `{"type": "sensor", "index": 2}` | `{"type": "gpu"}` | `{"type": "max", "of": [...]}`. O campo atual `tempInput` continua aceito e equivale a `sensor`.
2. **No `SoftwareCurveEngine`** (item 15), a fonte é calculada antes da média.
3. **Leitura faltando:** com `max`, usa as fontes disponíveis; se nenhuma tiver leitura → BIOS (regra atual). Com `gpu` sem leitura → BIOS.
4. **Editor:** o seletor de sensor ganha "GPU" e "Maior entre CPU e GPU", só quando `hardwareCurves` é falso.
5. **Gigabyte:** o chip só conhece os próprios sensores, então as opções não aparecem. Um modo híbrido (o FanPilot reescrevendo a curva no chip) fica como investigação futura, porque perderia a vantagem de a curva valer mesmo sem o FanPilot.

### Como ficou (25/10/2026)

- Em vez do objeto `{"type": …}`, a requisição ganhou o campo `source`: `"sensor"` (o padrão, e o que vale para as
  curvas antigas), `"gpu"` ou `"max"`, a maior entre o sensor do `tempInput` e a GPU. Cobre os dois casos da spec com
  menos formato novo; um `max` com outras fontes pode vir depois sem quebrar este.
- `FanCurve.Source` vai e volta com a curva (`desired-curves.json`, `/api/fans`); o sensor fica guardado como ausente,
  então nada muda nas curvas salvas antes. `SoftwareCurveEngine.SourceTemp` escolhe a temperatura antes da suavização:
  com `max`, só as fontes que leem (um termistor aberto, 127 °C, não conta); nenhuma → BIOS, como já era; `gpu` sem
  leitura → BIOS. O `LhmBoard` recebe a temperatura da GPU do `FanPilotHost` (`GpuMonitor.Temperature`).
- A Gigabyte recusa `gpu` e `max` (`err.source.chip`); os perfis (item 18) guardam a fonte que o fan já usava.
- No editor, "GPU" e "Maior entre CPU e GPU" aparecem no seletor do sensor só quando o FanPilot executa as curvas e há
  leitura da GPU (ou a curva já usa a GPU); uma dica explica o que acontece sem leitura. Os cards e a linha "agora" do
  editor usam a mesma regra (`referenceTemp` em `wwwroot/js/sensors.mjs`).

## Critérios de aceite

- [x] Um fan de gabinete com "Maior entre CPU e GPU" acelera com a GPU quente e a CPU fria (no motor, em teste; falta
  a placa de verdade no caminho LHM).
- [x] GPU sem leitura (driver reiniciando): com a fonte `max`, segue a CPU; com a fonte `gpu`, vai para a BIOS.
- [x] Curvas antigas continuam funcionando sem mudança.

## Testes

- Automatizados: cálculo das fontes e regras de leitura faltando, no motor (`SoftwareCurveTests`); a fonte indo com a
  curva, nos perfis, a recusa na Gigabyte e de uma fonte desconhecida (`FanManagerTests`); a mesma regra no painel
  (`tests/js/sensors.test.mjs`).
- Ponta a ponta: na placa falsa `lhm`, escolher "Maior entre CPU e GPU" e depois "GPU" e ver o pedido, o card e a volta
  à BIOS; na `demo` (Gigabyte), as opções não aparecem.
