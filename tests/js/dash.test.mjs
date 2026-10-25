// Panel mode's URL options and drawing (wwwroot/js/dash.mjs). Run from the repository root: node --test "tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { panelOptions, tempLevel, chartRange, seriesPath } from '../../wwwroot/js/dash.mjs';

test('escala e tema pela URL, em inglês ou em português', () => {
  assert.deepEqual(panelOptions(''), { scale: 1, theme: 'dark' });
  assert.deepEqual(panelOptions('?scale=1.2&theme=light'), { scale: 1.2, theme: 'light' });
  assert.deepEqual(panelOptions('?escala=1,5&tema=claro'), { scale: 1.5, theme: 'light' });
  assert.deepEqual(panelOptions('?tema=sistema'), { scale: 1, theme: 'auto' });
  // Out of range or nonsense: the nearest sensible value.
  assert.equal(panelOptions('?scale=9').scale, 2.5);
  assert.equal(panelOptions('?scale=0.1').scale, 0.5);
  assert.equal(panelOptions('?scale=grande').scale, 1);
  assert.equal(panelOptions('?theme=roxo').theme, 'dark');
});

test('cor da temperatura nos mesmos degraus dos cards do painel', () => {
  assert.equal(tempLevel(null), '');
  assert.equal(tempLevel(59.9), 'good');
  assert.equal(tempLevel(60), 'warning');
  assert.equal(tempLevel(75), 'serious');
  assert.equal(tempLevel(85), 'critical');
});

test('o minigráfico cabe os valores com folga, em dezenas, com pelo menos 30 °C', () => {
  assert.deepEqual(chartRange([]), [30, 60]);
  assert.deepEqual(chartRange([42, 58, null]), [30, 70]);
  assert.deepEqual(chartRange([50, 51]), [40, 70]);
});

test('a linha só cobre a janela e se interrompe nas falhas', () => {
  const now = 600_000, span = 600_000;
  const d = seriesPath([0, 300_000, 450_000, 600_000], [30, 40, null, 50], now, span, 30, 50, 600, 200);
  assert.equal(d, 'M0.0 200.0L300.0 100.0M600.0 0.0');
  // Older than the window: left out.
  assert.equal(seriesPath([-1, 600_000], [35, 35], now, span, 30, 50, 600, 200), 'M600.0 150.0');
});
