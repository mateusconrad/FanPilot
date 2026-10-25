// Alerts (item 16) end to end: the "stall" board's System 2 seizes after 35 s; the panel shows it and the ntfy
// destination (a local server standing in for ntfy.sh) gets it. Slow on purpose: the rule wants 30 s of spinning
// before a stop counts.
import http from 'node:http';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp, ntfy;
const received = [];

test.beforeAll(async () => {
  ntfy = http.createServer((req, res) => {
    let body = '';
    req.on('data', (d) => { body += d; });
    req.on('end', () => {
      received.push({ url: decodeURIComponent(req.url), body });
      res.end('{}');
    });
  });
  await new Promise((resolve) => ntfy.listen(0, '127.0.0.1', resolve));
  fp = await FanPilot.start('stall');
});

test.afterAll(async () => {
  await fp?.stop();
  ntfy?.close();
});

test('sem destino configurado, nada sai; o teste chega ao ntfy configurado', async ({ page }) => {
  await page.goto(fp.url + '/');
  await page.locator('#alertSettings summary').click();
  await expect(page.locator('#alNtfyState')).toHaveText('não configurado');
  await page.locator('#alTest').click();
  await expect(page.locator('#toast')).toHaveText('Configure o ntfy ou o Telegram antes do teste.');
  expect(received).toHaveLength(0);

  await page.locator('#alNtfy').fill(`http://127.0.0.1:${ntfy.address().port}/fanpilot-e2e`);
  await page.locator('#alPcName').fill('PC de teste');
  await page.locator('#alSave').click();
  await expect(page.locator('#toast')).toHaveText('Alertas salvos');
  await expect(page.locator('#alNtfyState')).toHaveText('configurado (…-e2e)');
  await page.locator('#alTest').click();
  await expect(page.locator('#toast')).toHaveText('Teste enviado');
  expect(received.at(-1).url).toContain('title=PC de teste: Teste de alerta');
});

test('um fan que para vira alerta no painel e no ntfy', async ({ page }) => {
  test.setTimeout(120_000);
  await page.goto(fp.url + '/');
  const banner = page.locator('#alertBanner');
  await expect(banner).toContainText('Fan parado: System 2', { timeout: 70_000 });
  await expect(page.locator('#alertList')).toContainText('Fan parado: System 2');
  await expect(page.locator('#alertsSub')).toHaveText('1 ativo');
  await expect.poll(() => received.some((r) => r.url.includes('title=PC de teste: Fan parado: System 2')), { timeout: 10_000 }).toBe(true);

  // Acknowledged: off the banner, still in the card while it lasts.
  await banner.getByRole('button', { name: 'Dispensar: Fan parado: System 2' }).click();
  await expect(banner).toBeHidden();
  await expect(page.locator('#alertList')).toContainText('Fan parado: System 2');
  // A header without a tachometer (CPU, System 3) never alerts.
  // The API sends each alert's words as a message the panel translates (item 21).
  expect((await (await page.request.get(fp.url + '/api/alerts')).json()).active.map((a) => a.title))
    .toEqual([{ code: 'alert.fanStopped.title', params: { name: 'System 2' }, text: 'Fan stopped: System 2' }]);
});
