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

## Critérios de aceite

- [ ] Um fan de gabinete com "Maior entre CPU e GPU" acelera com a GPU quente e a CPU fria.
- [ ] GPU sem leitura (driver reiniciando): com a fonte `max`, segue a CPU; com a fonte `gpu`, vai para a BIOS.
- [ ] Curvas antigas continuam funcionando sem mudança.

## Testes

- Automatizados: cálculo das fontes e regras de leitura faltando, no motor.
