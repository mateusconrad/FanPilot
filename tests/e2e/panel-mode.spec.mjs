// Panel mode (item 22): the read-only full-screen view at the sizes people leave it on (case screens, old phones and
// tablets), live, with no fan control within reach. Screenshots go to screens\ with the panel's.
import fs from 'node:fs';
import path from 'node:path';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

test.beforeEach(async ({ page }) => { await page.emulateMedia({ reducedMotion: 'reduce' }); });

const outDir = path.join(import.meta.dirname, 'screens');
async function shot(page, testInfo, name) {
  fs.mkdirSync(outDir, { recursive: true });
  const file = path.join(outDir, name + '.png');
  await page.screenshot({ path: file, animations: 'disabled' });
  await testInfo.attach(name, { path: file, contentType: 'image/png' });
}

async function openPanel(page, query = '') {
  await page.goto(fp.url + '/panel' + query);
  await expect(page.locator('#cpuTemp')).not.toHaveText('–');
  await expect(page.locator('#dashFans .dash-fan')).toHaveCount(4);
  await expect(page.locator('#cpuLine')).toHaveAttribute('d', /^M/);
}

const SIZES = [
  { name: '800x480', width: 800, height: 480 },
  { name: '1280x400', width: 1280, height: 400 },
  { name: 'celular-paisagem', width: 844, height: 390 },
  { name: 'celular-retrato', width: 390, height: 844 },
  { name: 'tablet', width: 1024, height: 768 },
];

for (const size of SIZES) {
  test(`cabe em ${size.name} sem rolar, com os números grandes`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: size.width, height: size.height });
    await openPanel(page);
    const layout = await page.evaluate(() => {
      const doc = document.documentElement;
      const spills = [...document.querySelectorAll('.dash-value, .dash-label, .dash-sub')].filter((n) => {
        const box = n.getBoundingClientRect(), tile = n.closest('.dash-tile').getBoundingClientRect();
        return box.right > tile.right + 0.5 || box.bottom > tile.bottom + 0.5;
      }).map((n) => n.id || n.className);
      return { width: doc.scrollWidth, height: doc.scrollHeight, spills, value: parseFloat(getComputedStyle(document.getElementById('cpuTemp')).fontSize) };
    });
    expect(layout.width).toBeLessThanOrEqual(size.width);
    expect(layout.height).toBeLessThanOrEqual(size.height);
    expect(layout.spills).toEqual([]);
    // Readable from across the room.
    expect(layout.value).toBeGreaterThanOrEqual(40);
    await shot(page, testInfo, `modo-painel-${size.name}`);
  });
}

test('só leitura: nenhum controle de fan, nada enviado ao FanPilot', async ({ page }) => {
  const posts = [];
  page.on('request', (r) => { if (r.method() !== 'GET') posts.push(r.url()); });
  await openPanel(page);
  await expect(page.locator('input, select, #fans, #turboBtn, #profileSeg, #sheet')).toHaveCount(0);
  await expect(page.locator('button')).toHaveText(['Tela cheia', 'Manter a tela ligada']);
  // The keyboard reaches only the options, and after them the page itself.
  const reached = [];
  for (let i = 0; i < 4; i++) {
    await page.keyboard.press('Tab');
    reached.push(await page.evaluate(() => document.activeElement === document.body ? 'body' : document.activeElement.id || document.activeElement.getAttribute('href')));
  }
  expect(reached.slice(0, 3)).toEqual(['fullBtn', 'awakeBtn', '/']);
  expect(['body', 'fullBtn']).toContain(reached[3]);
  expect(posts).toEqual([]);
});

test('os dados chegam ao vivo e o gráfico anda', async ({ page }) => {
  await openPanel(page);
  await expect(page.locator('#statusDot')).toHaveClass(/ok/);
  const first = await page.locator('#cpuLine').getAttribute('d');
  await expect.poll(() => page.locator('#cpuLine').getAttribute('d'), { timeout: 5000 }).not.toBe(first);
  await expect(page.locator('#cpuLabel')).toHaveText('CPU · pacote');
  await expect(page.locator('#gpuSub')).toContainText('% uso');
});

test('tema claro e escala pela URL', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 480 });
  await openPanel(page);
  const normal = parseFloat(await page.locator('#cpuTemp').evaluate((n) => getComputedStyle(n).fontSize));
  await openPanel(page, '?tema=claro&escala=1.3');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  const bigger = parseFloat(await page.locator('#cpuTemp').evaluate((n) => getComputedStyle(n).fontSize));
  expect(bigger / normal).toBeCloseTo(1.3, 1);
});

test('a barra de opções some sozinha e volta com um toque', async ({ page }) => {
  await openPanel(page);
  await expect(page.locator('#dashBar')).not.toHaveClass(/idle/);
  await expect(page.locator('#dashBar')).toHaveClass(/idle/, { timeout: 8000 });
  await page.mouse.click(10, 10);
  await expect(page.locator('#dashBar')).not.toHaveClass(/idle/);
});

test('tela cheia pelo botão', async ({ page }) => {
  await openPanel(page);
  await page.locator('#fullBtn').click();
  await expect(page.locator('#fullBtn')).toHaveText('Sair da tela cheia');
  await page.locator('#fullBtn').click();
  await expect(page.locator('#fullBtn')).toHaveText('Tela cheia');
});

test('manter a tela ligada, e sem a Wake Lock API, pelo vídeo mudo', async ({ page }) => {
  // Over the LAN (plain HTTP) the browser has no Wake Lock: the video is what keeps the screen on there.
  await page.addInitScript(() => { Object.defineProperty(Navigator.prototype, 'wakeLock', { get: () => undefined }); });
  await openPanel(page);
  await page.locator('#awakeBtn').click();
  await expect(page.locator('#awakeBtn')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#awakeHint')).toContainText('vídeo mudo');
  await expect.poll(() => page.locator('video.dash-keepawake').evaluate((v) => !v.paused && v.currentTime > 0)).toBe(true);
  const media = await page.request.get(fp.url + '/media/keepawake.mp4');
  expect(media.headers()['content-type']).toBe('video/mp4');
  await page.locator('#awakeBtn').click();
  await expect(page.locator('#awakeBtn')).toHaveAttribute('aria-pressed', 'false');
  expect(await page.locator('video.dash-keepawake').evaluate((v) => v.paused)).toBe(true);
});

test('o painel tem o link para o modo painel', async ({ page }) => {
  await page.goto(fp.url + '/');
  await expect(page.locator('#statusText')).toHaveText('ao vivo');
  await page.getByRole('link', { name: 'Modo painel' }).click();
  await expect(page).toHaveURL(/\/panel$/);
  await expect(page).toHaveTitle('FanPilot · modo painel');
  // The Portuguese address works too.
  await page.goto(fp.url + '/painel');
  await expect(page.locator('#cpuTemp')).toBeVisible();
});

for (const theme of ['escuro', 'claro']) {
  test(`acessibilidade (axe) no tema ${theme}`, async ({ page }) => {
    await openPanel(page, '?tema=' + theme);
    const { violations } = await new AxeBuilder({ page }).analyze();
    expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
  });
}
