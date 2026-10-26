// Integrations (item 23): the tokens made on the PC's /pair page, which Prometheus and Home Assistant use to read
// /metrics and /api/v1/sensors (from the network, the token's real use, the C# tests check it, elevated in the CI),
// and MQTT for Home Assistant, set up in the panel.
import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';
import { TinyBroker } from './mqtt-broker.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

const outDir = path.join(import.meta.dirname, 'screens');

const withToken = (token) => ({ headers: { Authorization: 'Bearer ' + token } });

test('no /pair, o token aparece uma vez, lê o /metrics e para de valer ao ser revogado', async ({ page, context }, testInfo) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(fp.url + '/pair');
  await expect(page.locator('#tokens')).toHaveText('Nenhum token criado.');
  await expect(page.locator('#tokenNew')).toBeHidden();

  await page.getByLabel('Nome').fill('Prometheus');
  await page.getByRole('button', { name: 'Criar token' }).click();
  await expect(page.locator('#tokenNew')).toBeVisible();
  const token = await page.locator('#tokenValue').textContent();
  expect(token).toMatch(/^fp_[\w-]{40,}$/);
  await expect(page.locator('#tokens .session')).toHaveCount(1);
  await expect(page.locator('#tokens .session')).toContainText('Prometheus');
  await expect(page.locator('#tokens .session')).toContainText('nunca usado');
  await page.getByRole('button', { name: 'Copiar' }).click();
  await expect(page.getByRole('button', { name: 'Copiado' })).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(token);
  fs.mkdirSync(outDir, { recursive: true });
  const file = path.join(outDir, 'pareamento-integracoes.png');
  await page.screenshot({ path: file, fullPage: true, animations: 'disabled' });
  await testInfo.attach('pareamento-integracoes', { path: file, contentType: 'image/png' });

  // What Prometheus gets.
  const metrics = await fetch(fp.url + '/metrics', withToken(token));
  expect(metrics.status).toBe(200);
  expect(metrics.headers.get('content-type')).toBe('text/plain; version=0.0.4; charset=utf-8');
  const text = await metrics.text();
  expect(text).toMatch(/^fanpilot_up 1$/m);
  expect(text).toMatch(/^fanpilot_fan_rpm\{fan="System 1",name="System 1"\} \d+$/m);
  // And Home Assistant's REST sensor.
  const sensors = await (await fetch(fp.url + '/api/v1/sensors', withToken(token))).json();
  expect(sensors.version).toBe(1);
  expect(sensors.cpu.temperature).toBeGreaterThan(0);
  expect(sensors.fans.map((f) => f.name)).toEqual(['CPU', 'System 1', 'System 2', 'System 3']);

  // The page refreshes on its own: the use shows up, the value is still there until the page closes.
  await expect(page.locator('#tokens .session')).toContainText('último uso', { timeout: 10_000 });
  await page.reload();
  await expect(page.locator('#tokenNew')).toBeHidden();
  await expect(page.locator('body')).not.toContainText(token);

  await page.getByRole('button', { name: 'Revogar: Prometheus' }).click();
  await expect(page.locator('#tokens')).toHaveText('Nenhum token criado.');
  const revoked = await fetch(fp.url + '/metrics', withToken(token));
  expect(revoked.status).toBe(401);
  expect(revoked.headers.get('www-authenticate')).toBe('Bearer realm="FanPilot"');
});

test('um nome vazio ou longo demais não vira token', async ({ page }) => {
  await page.goto(fp.url + '/pair');
  // Spaces only: the browser lets it through (required is met), FanPilot says no.
  await page.getByLabel('Nome').fill('   ');
  await page.getByRole('button', { name: 'Criar token' }).click();
  await expect(page.locator('#tokenError')).toHaveText('Não deu certo: Dê ao token um nome de até 40 caracteres.');
  await expect(page.locator('#tokenNew')).toBeHidden();
  await expect(page.locator('#tokens')).toHaveText('Nenhum token criado.');
});

// Home Assistant over MQTT, set up in the panel, against a tiny broker that keeps what FanPilot sends (mqtt-broker.mjs).
test.describe('Home Assistant pelo MQTT', () => {
  let broker;
  test.beforeEach(async () => { broker = await new TinyBroker({ user: 'ha', password: 'segredo' }).start(); });
  test.afterEach(async () => { await broker?.stop(); });

  async function openMqtt(page) {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(fp.url + '/');
    await expect(page.locator('#statusText')).toHaveText('ao vivo');
    await page.locator('#mqttSettings summary').click();
    // The form fills itself from FanPilot: wait for it before typing over it.
    await expect(page.getByLabel('Prefixo de descoberta do Home Assistant')).toHaveValue('homeassistant');
  }

  test('configurado no painel, as entidades vão sozinhas para o broker', async ({ page }, testInfo) => {
    await openMqtt(page);
    await expect(page.locator('#mqttSub')).toHaveText('desligado');
    await page.getByLabel('Endereço do broker (nome ou IP)').fill('127.0.0.1');
    await page.getByLabel('Porta').fill(String(broker.port));
    await page.getByLabel('Usuário (opcional)').fill('ha');
    await page.getByLabel('Senha').fill('segredo');
    await page.getByRole('button', { name: 'Salvar e conectar' }).click();
    await expect(page.locator('#mqttSub')).toHaveText(new RegExp('^conectado a 127[.]0[.]0[.]1:' + broker.port + ' · [0-9]+ entidades$'));

    // The connection: this PC's client, with the user and password, and the last will.
    const c = broker.connects.at(-1);
    expect(c).toMatchObject({ user: 'ha', password: 'segredo', willMessage: 'offline', willRetain: true });
    expect(c.clientId).toMatch(/^fanpilot_[a-z0-9_-]+$/);
    const node = c.clientId.slice('fanpilot_'.length);
    expect(c.willTopic).toBe(`fanpilot/${node}/availability`);
    // The discovery, kept by the broker, in the panel's language; the state, every 5 s.
    const cpu = broker.last(`homeassistant/sensor/fanpilot_${node}_cpu_temperature/config`);
    expect(cpu.retain).toBe(true);
    expect(JSON.parse(cpu.payload)).toMatchObject({ name: 'Temperatura da CPU', unit_of_measurement: '°C', state_topic: `fanpilot/${node}/state` });
    expect(JSON.parse(broker.last(`homeassistant/sensor/fanpilot_${node}_fan1_rpm/config`).payload).name).toBe('Fan System 1');
    const state = JSON.parse(broker.last(`fanpilot/${node}/state`).payload);
    expect(state.version).toBe(1);
    expect(state.fans).toHaveLength(4);
    expect(broker.last(`fanpilot/${node}/availability`)).toMatchObject({ payload: 'online', retain: true });
    await expect(page.locator('#mqTopic')).toHaveText(`O estado vai para fanpilot/${node}/state a cada 5 s.`);
    fs.mkdirSync(outDir, { recursive: true });
    const file = path.join(outDir, 'painel-mqtt.png');
    await page.locator('#mqttCard').screenshot({ path: file, animations: 'disabled' });
    await testInfo.attach('painel-mqtt', { path: file, contentType: 'image/png' });

    // The password never comes back to the panel.
    await page.reload();
    await page.locator('#mqttSettings summary').click();
    await expect(page.getByLabel('Senha')).toHaveValue('');
    await expect(page.getByLabel('Senha')).toHaveAttribute('placeholder', 'salva; deixe vazio para manter');

    // Off: Home Assistant hears offline.
    await page.getByRole('button', { name: 'Desligar' }).click();
    await expect(page.locator('#mqttSub')).toHaveText('desligado');
    await expect.poll(() => broker.last(`fanpilot/${node}/availability`)?.payload).toBe('offline');
  });

  test('uma senha errada aparece como tal', async ({ page }) => {
    await openMqtt(page);
    await page.getByLabel('Endereço do broker (nome ou IP)').fill('127.0.0.1');
    await page.getByLabel('Porta').fill(String(broker.port));
    await page.getByLabel('Usuário (opcional)').fill('ha');
    await page.getByLabel('Senha').fill('errada');
    await page.getByRole('button', { name: 'Salvar e conectar' }).click();
    await expect(page.locator('#mqttSub')).toHaveText(`sem conexão: o broker 127.0.0.1:${broker.port} recusou o usuário ou a senha`);
    await page.getByRole('button', { name: 'Desligar' }).click();
    await expect(page.locator('#mqttSub')).toHaveText('desligado');
  });

  test('um endereço inválido nem é salvo', async ({ page }) => {
    await openMqtt(page);
    await page.getByLabel('Endereço do broker (nome ou IP)').fill('mqtt://broker');
    await page.getByRole('button', { name: 'Salvar e conectar' }).click();
    await expect(page.locator('#toast')).toHaveText('Informe o endereço do broker: um nome ou um IP, sem http://.');
    await expect(page.locator('#mqttSub')).toHaveText('desligado');
  });
});
