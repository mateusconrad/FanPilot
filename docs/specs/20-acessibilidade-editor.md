# Editor de curvas acessível e com ajuste fino

- **Item:** 20
- **Onda:** 3 · Produto
- **Tipo:** acessibilidade
- **Esforço:** P
- **Depende de:** —

## Problema

- Os pontos da curva só se movem arrastando com o mouse ou o dedo (`wwwroot/app.js:565-596`): não há caminho pelo teclado nem pelo leitor de tela.
- No celular, acertar 1 °C ou 1% arrastando é difícil.
- A folha do editor é marcada como diálogo (`wwwroot/index.html:99`), mas o foco não vai para ela nem fica preso nela, Esc não fecha, e o foco não volta ao fan quando ela fecha (`wwwroot/app.js:430-473`).

## Objetivo

O editor funciona pelo teclado, pelo leitor de tela e com ajuste fino no toque.

## Solução

1. **Pontos como sliders:** cada ponto ganha `tabindex=0`, `role="slider"` e `aria-valuetext="Ponto 3: 50 °C, 40%"`. Setas ←/→ mudam a temperatura em ±1 °C e ↑/↓ a velocidade em ±1%; com Shift, ±5. As restrições são as mesmas do arraste (uma função `movePoint` compartilhada).
2. **Toque:** tocar num ponto o seleciona e mostra uma barra pequena com `− °C +` e `− % +` (segurar repete).
3. **Diálogo:** ao abrir, o foco vai para o primeiro controle; Tab circula dentro da folha; Esc fecha; ao fechar, o foco volta ao card do fan. O resto da página fica `inert` enquanto a folha está aberta.
4. **Alterações não salvas:** fechar com mudanças pergunta dentro da própria folha ("Descartar alterações?"), sem `confirm()`.
5. **Alvos de toque** com pelo menos 44 px renderizados, nas larguras de 320 a 1024 px.

## Critérios de aceite

- [ ] Dá para montar uma curva só com o teclado.
- [ ] TalkBack e VoiceOver anunciam os pontos com temperatura e velocidade.
- [ ] Dá para ajustar um ponto em 1 °C no celular sem arrastar.
- [ ] Esc fecha o editor e o foco volta ao fan.

## Testes

- Automatizados: `movePoint` respeita as mesmas restrições do arraste (teste JS).
- Ponta a ponta (item 26): o fluxo só com teclado e a checagem de acessibilidade (axe) do editor.
- Manuais: roteiro de teclado e leitor de tela no `tools/ui-mock`, em 320, 390 e 1024 px.
