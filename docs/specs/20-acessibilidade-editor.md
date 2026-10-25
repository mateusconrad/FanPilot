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

### Como ficou (25/10/2026)

- `movePoint` (`wwwroot/js/curve.mjs`) tem as regras do arraste, e o arraste, as setas e os botões passam por ela.
- Cada ponto é um `role="slider"` com `aria-label="Ponto 3 de 8"` e `aria-valuetext="50 °C, 40%"`: o nome diz qual é o
  ponto, e o valor, anunciado a cada mudança, só a temperatura e a velocidade. O valor do slider é a velocidade
  (`aria-orientation="vertical"`), então o gesto de ajuste do leitor de tela muda a velocidade; ←/→ mudam a
  temperatura. Além das setas (Shift: 5), PageUp/PageDown mudam 10% e Home/End levam à menor e à maior velocidade que o
  ponto pode ter.
- O SVG do editor é montado ao abrir e depois atualizado no lugar: o ponto com o foco continua com ele enquanto anda e
  enquanto os dados ao vivo redesenham a folha (antes, o SVG era refeito a cada segundo).
- Tocar ou clicar num ponto o seleciona sem movê-lo: ele só anda depois de 4 px de arraste, e na mesma distância do
  dedo em que foi pego. A barra mostra "Ponto 4 de 8: 55 °C, 55%", com ◀ ▶ (ponto anterior e próximo, dando a volta) e
  − °C + e − % +; segurar repete, e o clique que encerra o segurar não conta um passo a mais. O que a barra faz é
  anunciado por uma região `aria-live`. Sem ponto selecionado, uma dica ocupa o lugar da barra.
- O alvo de cada ponto tem raio de 27 unidades (45 px numa tela de 320 px); onde os alvos se sobrepõem, vale o ponto
  mais próximo do toque. O gráfico ganhou 18 unidades no topo para o alvo de um ponto em 100% caber inteiro.
- A folha: o foco vai para o "Fechar" ao abrir, o cabeçalho e o `main` ficam `inert`, Tab e Shift+Tab dão a volta, Esc
  fecha e o foco volta ao card do fan.
- Fechar, Esc ou um toque fora da folha com uma curva ou um nome não salvos mostram "Descartar alterações?" no topo da
  folha; Esc ou "Continuar editando" devolvem o foco para onde estava. Salvar o nome com uma curva ainda não aplicada
  deixa a folha aberta, em vez de descartar a curva.
- Botões, seletores, campos, o controle da velocidade fixa e o "Nome e função" têm pelo menos 44 px na folha.

## Critérios de aceite

- [x] Dá para montar uma curva só com o teclado.
- [ ] TalkBack e VoiceOver anunciam os pontos com temperatura e velocidade (falta conferir num celular; o axe não
  aponta nada no editor).
- [x] Dá para ajustar um ponto em 1 °C no celular sem arrastar (com toque emulado; o celular de verdade está no roteiro
  do ROADMAP).
- [x] Esc fecha o editor e o foco volta ao fan.

## Testes

- Automatizados: `movePoint` respeita as mesmas restrições do arraste (`tests/js/curve.test.mjs`).
- Ponta a ponta (`tests/e2e/editor-a11y.spec.mjs`): a curva montada e aplicada só com o teclado, Tab dando a volta na
  folha, Esc e o foco de volta ao fan, a pergunta antes de descartar (Fechar, Esc, toque fora, nome não salvo), salvar o
  nome sem perder a curva, segurar o + até o limite, ◀ ▶ e os eixos fixos, o ajuste por toque a 390 px e os alvos de
  44 px em 320, 390 e 1024 px. O axe do editor (`a11y.spec.mjs`) roda com a barra, o "Nome e função" e a pergunta na
  tela, nos dois temas.
- Manuais: TalkBack e VoiceOver num celular pareado (tabela "Verificação em hardware pendente" do ROADMAP).
