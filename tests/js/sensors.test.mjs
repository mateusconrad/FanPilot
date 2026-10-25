// Item 19: the panel shows the reading the backend marked "cpu-main", whatever its title.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cpuMain } from '../../wwwroot/js/sensors.mjs';

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
