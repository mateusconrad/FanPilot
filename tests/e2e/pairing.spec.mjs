// Pairing: the PC's /pair page, and a phone on the network (another browser context, through the PC's own LAN
// address, which is not loopback) from the lock screen to the panel and back. The phone part needs FanPilot listening
// on every address, which needs Administrator: the CI runs it, a PC without elevation skips it.
import { expect, test } from '@playwright/test';
import { FanPilot, isAdmin } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

test('a página /pair mostra o QR, o endereço e os aparelhos', async ({ page }) => {
  await page.goto(fp.url + '/pair');
  await expect(page.locator('#qr svg')).toBeVisible();
  // The code itself is in the QR only.
  await expect(page.locator('#url')).toHaveText(/^http:\/\/.+:\d+\/auth\?c=…$/);
  await expect(page.locator('#host option')).not.toHaveCount(0);
  await expect(page.locator('#sessions')).toHaveText('Nenhum aparelho pareado.');
  await expect(page.locator('#countdown')).toHaveText(/^novo código em \d+ s$/);
});

test.describe('celular na rede', () => {
  test.skip(!isAdmin, 'precisa de Administrador: só assim o FanPilot escuta nos endereços da rede');

  test('pareia pelo QR, usa o painel e é despareado no PC', async ({ page, browser }) => {
    const info = await (await page.request.get(fp.url + '/api/pair/info')).json();
    const pairUrl = info.urls[0];
    const lan = new URL(pairUrl).origin;
    const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
    try {
      const mobile = await phone.newPage();
      await mobile.goto(lan + '/');
      await expect(mobile.locator('h1')).toHaveText('🔒 Aparelho não pareado');
      expect((await mobile.request.get(lan + '/api/fans')).status()).toBe(401);
      expect((await mobile.request.get(lan + '/pair')).status()).toBe(403);

      await mobile.goto(pairUrl);
      await expect(mobile.locator('body')).toContainText('Pareado!');
      await mobile.waitForURL(lan + '/');
      await expect(mobile.locator('#statusText')).toHaveText('ao vivo');
      await expect(mobile.locator('#fans .fan')).toHaveCount(4);

      // Single use: the same QR on another phone is turned away.
      const other = await browser.newContext();
      const second = await other.newPage();
      await second.goto(pairUrl);
      await expect(second.locator('body')).toContainText('Código inválido ou expirado');
      await other.close();

      await page.goto(fp.url + '/pair');
      await expect(page.locator('#sessions .session')).toHaveCount(1);
      await page.locator('#sessions .session').getByRole('button', { name: 'Desparear' }).click();
      await expect(page.locator('#sessions')).toHaveText('Nenhum aparelho pareado.');
      await mobile.reload();
      await expect(mobile.locator('h1')).toHaveText('🔒 Aparelho não pareado');
    } finally {
      await phone.close();
    }
  });
});
