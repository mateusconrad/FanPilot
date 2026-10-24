// Curve math of the panel (wwwroot/js/curve.mjs). Run from the repository root: node --test "tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { curveAt, resample, normalize } from '../../wwwroot/js/curve.mjs';

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
