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

1. **`SoftwareCurveEngine`** (`src/SoftwareCurveEngine.cs`, sem dependência de hardware): recebe a temperatura de referência de cada header e o tempo que passou desde o último ciclo (medido, não presumido em 1 s) e devolve a velocidade ou "devolver à BIOS". O `LhmBoard` passa a só ler os sensores e escrever o resultado. Uma curva nova, ou um sensor que volta depois de uma falha, começa direto no valor da curva: não há de onde fazer rampa.
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

7. A resposta fica guardada na requisição da curva (`"response": "balanced"`) e na curva (`FanCurve.Response`), fora da assinatura que o watchdog compara com o hardware. Uma requisição sem o campo recebe "Equilibrada"; um valor desconhecido é recusado (400). Curvas salvas antes, sem o campo, recebem "Equilibrada" ao serem reaplicadas (anotado no CHANGELOG); velocidades fixas salvas continuam sem resposta.
8. O modo fixo não usa média nem rampa, só a proteção de temperatura.
9. Painel: o editor ganha o controle "Resposta", visível só quando `hardwareCurves` é falso, com uma frase explicando a opção escolhida, e a descrição do fan diz a resposta ("Curva personalizada, resposta suave"). Nas placas com a curva no chip o pedido continua sem o campo.
10. Caminho Gigabyte: sem mudança. Entender o que o chip faz com `DeltaTemperature` vira uma investigação de engenharia reversa à parte.

## Critérios de aceite

- [x] Temperatura oscilando ±1 °C em volta de um ponto da curva: no máximo 2 mudanças de velocidade por minuto (sem resposta, a mesma sequência dá 10 ou mais).
- [x] Salto de 40 para 80 °C: a velocidade sobe no limite da rampa e, ao atingir o último ponto, vai a 100% na hora.
- [x] Queda de temperatura: a velocidade desce devagar, e só depois de esfriar H graus.
- [ ] Com carga intermitente real numa placa no caminho LHM, nenhum sobe e desce audível. Fica na verificação em hardware (`docs/ROADMAP.md`).

## Testes

- Automatizados: sequências sintéticas para cada critério acima, para a volta à BIOS e para o bypass de segurança (`SoftwareCurveTests`); a resposta indo do pedido para a placa e de volta ao painel, a recusa de um valor desconhecido e as curvas salvas sem o campo (`FanManagerTests`); o controle no editor do cenário `lhm` e a ausência dele no `demo` (ponta a ponta).
- Limitação conhecida: o medidor "velocidade comandada" do painel calcula a curva na temperatura do momento; numa curva suavizada, durante uma rampa, a velocidade escrita fica um pouco atrás dele.
- Manuais: placa no caminho LHM (a B760M com `"backend": "lhm"`, se o LHM controlar o chip dela) com carga intermitente.
