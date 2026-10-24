# Curvas por software suaves: média, histerese e rampa

- **Item:** 15
- **Onda:** 3 · Produto
- **Tipo:** produto
- **Esforço:** M
- **Depende de:** 08

## Problema

Nas placas em que o FanPilot executa as curvas, a velocidade é calculada a cada segundo a partir da temperatura daquele instante (`src/LhmBoard.cs:205-223`). Com carga intermitente, a temperatura de pacote da CPU oscila vários graus em segundos, e o fan sobe e desce o tempo todo: justamente o barulho que o usuário quer evitar. O campo `DeltaT` da curva é copiado (`src/FanManager.cs:223`), mas ignorado nesse caminho.

## Objetivo

Fans estáveis, que não ficam "caçando" velocidade, sem atrasar a reação a um aquecimento de verdade.

## Solução

1. **`SoftwareCurveEngine`** (novo, sem dependência de hardware): recebe as temperaturas a cada ciclo e devolve, para cada header, a velocidade ou "devolver à BIOS". O `LhmBoard` passa a só ler os sensores e escrever o resultado.
2. **Suavização:** média móvel exponencial da temperatura de referência, com constante de tempo τ.
3. **Histerese H:** a velocidade só desce quando a temperatura cai H graus abaixo do ponto que justificava a velocidade atual. Regra: `alvo = curva(T)`; se `alvo < atual`, `alvo = min(atual, curva(T + H))`.
4. **Rampa:** subida limitada a U %/s e descida a D %/s.
5. **Bypass de segurança:** temperatura bruta (sem média) no último ponto da curva ou acima dele → 100% na hora, ignorando média e rampa. Sensor sem leitura ou com valor impossível → BIOS, como hoje.
6. **Predefinições de "Resposta"** em vez de números soltos (valores iniciais, a calibrar no hardware):

   | Resposta | τ | H | Subida | Descida |
   |---|---|---|---|---|
   | Rápida | 1 s | 1 °C | 50 %/s | 10 %/s |
   | **Equilibrada** (padrão) | 4 s | 2 °C | 15 %/s | 3 %/s |
   | Suave | 10 s | 4 °C | 8 %/s | 1 %/s |

7. A resposta fica guardada na requisição da curva (`"response": "balanced"`). Curvas antigas, sem o campo, usam "Equilibrada" (anotar no CHANGELOG).
8. O modo fixo não usa média nem rampa, só a proteção de temperatura.
9. Painel: o editor ganha o controle "Resposta", visível só quando `hardwareCurves` é falso, com uma frase explicando.
10. Caminho Gigabyte: sem mudança. Entender o que o chip faz com `DeltaTemperature` vira uma investigação de engenharia reversa à parte.

## Critérios de aceite

- [ ] Temperatura oscilando ±1 °C em volta de um ponto da curva: no máximo 2 mudanças de velocidade por minuto.
- [ ] Salto de 40 para 80 °C: a velocidade sobe no limite da rampa e, ao atingir o último ponto, vai a 100% na hora.
- [ ] Queda de temperatura: a velocidade desce devagar, e só depois de esfriar H graus.
- [ ] Com carga intermitente real numa placa no caminho LHM, nenhum sobe e desce audível.

## Testes

- Automatizados: sequências sintéticas para cada critério acima, para a volta à BIOS e para o bypass de segurança.
- Manuais: placa no caminho LHM (a B760M com `"backend": "lhm"`, se o LHM controlar o chip dela) com carga intermitente.
