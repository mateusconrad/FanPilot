// The panel in English (item 21): the browser's language picks it, and it can be changed by hand on the device.
// The other specs run with a Portuguese browser (playwright.config.mjs) and check that nothing changed for it.
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

// Portuguese has letters English never uses: none may be left on an English page.
const PORTUGUESE = /[ãõçáéíóúâêôà]/i;
// The language switch names each language in itself: "Português" stays.
const portugueseIn = async (page) => (await page.locator('body').innerText()).split('\n')
  .filter((line) => PORTUGUESE.test(line) && line.trim() !== 'Português');

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
    await page.locator('summary', { hasText: 'All sensors' }).click();
    await page.locator('summary', { hasText: 'Limits and notifications' }).click();
    await expect(page.locator('#alNtfyState')).toHaveText('not set');
    expect(await portugueseIn(page)).toEqual([]);
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
