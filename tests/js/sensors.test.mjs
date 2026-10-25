// Item 19: the panel shows the reading the backend marked "cpu-main", whatever its title.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cpuMain, referenceTemp } from '../../wwwroot/js/sensors.mjs';

const socket = { id: 'temperature-514', type: 'Temperature', title: 'CPU · socket', value: 52, role: 'cpu-socket' };
const pkg = { id: 'temperature-cpu-package', type: 'Temperature', title: 'CPU · pacote', value: 66, role: 'cpu-main' };
const clock = { id: 'frequency-cpu', type: 'Frequency', title: 'CPU', value: 4600 };

test('a leitura marcada como cpu-main ganha, mesmo depois de outra', () => {
  assert.equal(cpuMain([socket, clock, pkg]), pkg);
});

test('sem papel nenhum, a temperatura com o título CPU', () => {
  const old = { id: 'temperature-514', type: 'Temperature', title: 'CPU', value: 52 };
  assert.equal(cpuMain([clock, old]), old);
});

test('sem CPU, nada', () => {
  assert.equal(cpuMain([clock]), null);
  assert.equal(cpuMain(undefined), null);
});

// Item 25: the same rules as SoftwareCurveEngine.SourceTemp, for the cards and the editor.
test('a temperatura de referência segue a fonte da curva', () => {
  const snap = { tempInputs: [40, 52, 127], gpu: { ok: true, temp: 75 } };
  assert.equal(referenceTemp('sensor', 1, snap), 52);
  assert.equal(referenceTemp('gpu', 1, snap), 75);
  assert.equal(referenceTemp('max', 1, snap), 75);
  assert.equal(referenceTemp('max', 1, { ...snap, gpu: { ok: true, temp: 45 } }), 52);
  // No GPU reading: "max" follows the sensor, "gpu" has nothing; an open input (127 °C) counts as no reading.
  assert.equal(referenceTemp('max', 1, { tempInputs: [40, 52], gpu: { ok: false } }), 52);
  assert.equal(referenceTemp('gpu', 1, { tempInputs: [40, 52], gpu: { ok: false } }), null);
  assert.equal(referenceTemp('max', 2, { ...snap, gpu: { ok: false } }), null);
  assert.equal(referenceTemp('sensor', 5, snap), null);
});
