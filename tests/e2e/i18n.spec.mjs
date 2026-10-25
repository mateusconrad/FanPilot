// The panel in English (item 21): the browser's language picks it, and it can be changed by hand on the device.
// The other specs run with a Portuguese browser (playwright.config.mjs) and check that nothing changed for it.
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

// Portuguese has letters English never uses: none may be left on an English page.
const PORTUGUESE = /[ãõçáéíóúâêôà]/i;
// The language switch names each language in itself: "Português" stays.
const portugueseIn = async (page) => (await page.locator('body').innerText()).split('\n')
  .filter((line) => PORTUGUESE.test(line) && line.trim() !== 'Português');

// FanPilot runs one at a time: the demo board is stopped before the other boards start.
test.describe('placa demo', () => {
  let fp;
  test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
  test.afterAll(async () => { await fp?.stop(); });

  test.describe('navegador em inglês', () => {
    test.use({ locale: 'en-US' });
    test.beforeEach(async ({ page }) => { await page.emulateMedia({ reducedMotion: 'reduce' }); });

    test('o painel inteiro em inglês', async ({ page }) => {
      await page.goto(fp.url + '/');
      await expect(page.locator('#statusText')).toHaveText('live');
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
      await expect(page.locator('#fans .fan')).toHaveCount(4);
      await expect(page.locator('#profileSeg button')).toHaveText(['Quiet', 'Standard', 'Performance', 'Custom']);
      await expect(page.locator('#fans .fan').first().locator('.desc')).toContainText('Original curve');
      // FanPilot's own names for readings come with a key (item 21): the cores' temperature on the demo board.
      await expect(page.locator('#tiles .tile .label').first()).toHaveText('CPU · package');
      await page.locator('summary', { hasText: 'All sensors' }).click();
      await expect(page.locator('#sensorTable')).toContainText('CPU · socket');
      await page.locator('summary', { hasText: 'Limits and notifications' }).click();
      await expect(page.locator('#alNtfyState')).toHaveText('not set');
      expect(await portugueseIn(page)).toEqual([]);
    });

    test('erros da API traduzidos pelo código', async ({ page }) => {
      await page.goto(fp.url + '/');
      await page.locator('summary', { hasText: 'Limits and notifications' }).click();
      await page.locator('#alTest').click();
      await expect(page.locator('#toast')).toHaveText('Set up ntfy or Telegram before the test.');
    });

    test('o editor em inglês', async ({ page }) => {
      await page.goto(fp.url + '/');
      await page.locator('#fans .fan').nth(1).click();
      await expect(page.locator('#edTitle')).toHaveText('Fan · System 1');
      await expect(page.locator('#edPresets button')).toHaveText(['Quiet', 'Standard', 'Performance', 'Maximum']);
      await page.getByRole('slider', { name: 'Point 3 of 8' }).focus();
      await expect(page.locator('#edTuneLabel')).toHaveText('Point 3 of 8: 45 °C, 35%');
      await page.getByRole('button', { name: '1 °C higher' }).click();
      await expect(page.locator('#edTuneLabel')).toHaveText('Point 3 of 8: 46 °C, 35%');
      await page.locator('#edMeta summary').click();
      await page.keyboard.press('Escape');
      await expect(page.locator('#edDiscard')).toContainText('Discard changes?');
      expect(await portugueseIn(page)).toEqual([]);
    });

    test('pareamento e aparelho bloqueado em inglês', async ({ page }) => {
      await page.goto(fp.url + '/pair');
      await expect(page.locator('h1')).toHaveText('Pair a phone');
      await expect(page.locator('#countdown')).toContainText('new code in');
      await expect(page).toHaveTitle('FanPilot · pairing');
      expect(await portugueseIn(page)).toEqual([]);

      await page.goto(fp.url + '/locked.html');
      await expect(page.locator('h1')).toHaveText('🔒 Device not paired');
      await expect(page.locator('#how strong')).toHaveText(new URL(fp.url).origin.replace(/\/\/[^:]+/, '//localhost') + '/pair');
      expect(await portugueseIn(page)).toEqual([]);
    });
    // Last on the demo board: the alert stays on while the limit is low.
    test('um alerta em inglês, e as mensagens saem no idioma do painel', async ({ page }) => {
      test.setTimeout(60_000);
      await page.goto(fp.url + '/');
      await page.locator('summary', { hasText: 'Limits and notifications' }).click();
      await page.locator('#alCpu').fill('60');
      await page.locator('#alSave').click();
      await expect(page.locator('#toast')).toHaveText('Alerts saved');
      expect((await (await page.request.get(fp.url + '/api/alerts/settings')).json()).language).toBe('en');
      const banner = page.locator('#alertBanner');
      await expect(banner).toContainText(/CPU at \d+ °C/, { timeout: 20_000 });
      await expect(banner).toContainText('Above the 60 °C limit for more than 10 s.');
      await expect(page.locator('#alertsSub')).toHaveText('1 active');
      expect(await portugueseIn(page)).toEqual([]);
    });
  });

  test('o idioma escolhido fica no aparelho', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(fp.url + '/');
    await expect(page.locator('#statusText')).toHaveText('ao vivo');
    await page.locator('#langSelect').selectOption('en');
    await expect(page.locator('#statusText')).toHaveText('live');
    await expect(page.locator('#langSelect')).toHaveValue('en');
    await page.reload();
    await expect(page.locator('h2').first()).toHaveText('Temperatures · last 10 min');
    // The pairing page follows the same choice.
    await page.goto(fp.url + '/pair');
    await expect(page.locator('h1')).toHaveText('Pair a phone');
    await page.locator('#langSelect').selectOption('pt-BR');
    await expect(page.locator('h1')).toHaveText('Parear celular');
  });
});

// The boards whose messages come from FanPilot itself, in English (item 21).
test.describe('placas com avisos, em inglês', () => {
  test.use({ locale: 'en-US' });

  test('sem o pacote da CPU, a dica do PawnIO', async ({ page }) => {
    const worst = await FanPilot.start('worst');
    try {
      await page.goto(worst.url + '/');
      await expect(page.locator('#tiles .tile .label').first()).toHaveText('CPU · socket');
      await expect(page.locator('#tiles .tile-hint')).toHaveText("Install PawnIO (pawnio.eu) to see the cores' temperature.");
    } finally { await worst.stop(); }
  });

  test('sem controle de fans e sem GPU', async ({ page }) => {
    const unsupported = await FanPilot.start('unsupported');
    try {
      await page.goto(unsupported.url + '/');
      await expect(page.locator('#gpuGrid')).toHaveText('GPU unavailable: NVML not found (nvml.dll): install the NVIDIA driver.');
      await expect(page.locator('#fans')).toContainText('Monitoring keeps working.');
      expect(await portugueseIn(page)).toEqual([]);
    } finally { await unsupported.stop(); }
  });
});
