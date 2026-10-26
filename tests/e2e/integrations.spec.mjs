// Integrations (item 23): the tokens made on the PC's /pair page, which Prometheus and Home Assistant use to read
// /metrics and /api/v1/sensors. From the network (the token's real use) the C# tests check it, elevated in the CI.
import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

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
