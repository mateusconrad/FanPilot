// Curve math of the panel (wwwroot/js/curve.mjs). Run from the repository root: node --test "tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { curveAt, resample, normalize, movePoint } from '../../wwwroot/js/curve.mjs';

const fixture = JSON.parse(readFileSync(new URL('../fixtures/curves.json', import.meta.url), 'utf8'));

test('curveAt segue a mesma fixture que o FanCurve.PwmAt do backend', () => {
  for (const c of fixture.cases) {
    const curve = fixture.curves[c.curve];
    const got = curveAt(curve.points, curve.mode, c.t);
    assert.ok(Math.abs(got - c.pwm) < 1e-9, `${c.curve} a ${c.t} °C: esperado ${c.pwm}, veio ${got}`);
  }
});

test('curveAt sem pontos não tem valor (o painel mostra "–")', () => {
  assert.equal(curveAt([], 'SmartFanMode', 50), null);
});

test('resample mantém as pontas, distribui os pontos e nunca devolve o próprio preset', () => {
  const preset = [[0, 30], [35, 30], [45, 40], [55, 55], [62, 65], [68, 75], [74, 85], [82, 100]];
  const six = resample(preset, 6);
  assert.equal(six.length, 6);
  assert.deepEqual(six[0], [0, 30]);
  assert.deepEqual(six[5], [82, 100]);
  for (let i = 1; i < six.length; i++) assert.ok(six[i][0] > six[i - 1][0], 'temperaturas crescentes');
  const same = resample(preset, 8);
  assert.deepEqual(same, preset);
  assert.notEqual(same, preset);
  assert.notEqual(same[0], preset[0]);
});

test('normalize aplica o piso, a monotonia e 100% no último ponto', () => {
  const pts = normalize([[0, 5], [30, 50], [40, 30], [50, 60], [90, 70]], { minDutyPercent: 20 });
  assert.deepEqual(pts.map((p) => p[1]), [20, 50, 50, 60, 100]);
});

// The same moves as the drag test in tests/e2e/editor.spec.mjs: the keys and the fine-tune buttons use this too.
test('movePoint segue as regras do arraste', () => {
  const limits = { minDutyPercent: 20, maxFullSpeedTemp: 90 };
  const pts = [[0, 25], [35, 25], [45, 35], [55, 55], [62, 65], [68, 75], [74, 85], [82, 100]];
  // Up and to the left: never before the previous point, never above the next one.
  assert.deepEqual(movePoint(pts, 3, 10, 95, limits), [46, 65]);
  // Down and to the right: never after the next point, never below the previous one.
  assert.deepEqual(movePoint(pts, 3, 95, 0, limits), [61, 35]);
  // The last point stays at 100%, at most at maxFullSpeedTemp; the first stays at 0 °C, never below the minimum.
  assert.deepEqual(movePoint(pts, 7, 99, 10, limits), [90, 100]);
  assert.deepEqual(movePoint(pts, 0, 30, 0, limits), [0, 20]);
  // Rounded to whole degrees and percents, as the editor shows them.
  assert.deepEqual(movePoint(pts, 2, 44.6, 30.4, limits), [45, 30]);
  // Neighbors 2 °C apart leave a single place.
  assert.deepEqual(movePoint([[0, 30], [40, 40], [41, 45], [42, 50], [60, 100]], 2, 0, 45, limits), [41, 45]);
  // The input is left alone: the caller decides where the result goes.
  assert.deepEqual(pts[3], [55, 55]);
});
