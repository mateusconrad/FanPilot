// The fan editor on the demo board: dragging points within the rules, presets, applying (the request sent and the
// state that comes back), fixed speed and restoring the original curve.
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

test.beforeEach(async ({ page }) => {
  // Presets jump into place instead of morphing.
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(fp.url + '/');
  await expect(page.locator('#statusText')).toHaveText('ao vivo');
});

// The editor's SVG geometry (ED in app.js): [°C, %] to a point on the screen.
const ED = { w: 340, h: 230, l: 34, r: 12, t: 10, b: 26 };
async function screenPoint(page, t, p) {
  const box = await page.locator('#edSvg').boundingBox();
  const x = ED.l + t / 100 * (ED.w - ED.l - ED.r), y = ED.t + (1 - p / 100) * (ED.h - ED.t - ED.b);
  return { x: box.x + x / ED.w * box.width, y: box.y + y / ED.h * box.height };
}

// "0° 25% · 35° 25% · …" as [[0, 25], [35, 25], …].
async function editorPoints(page) {
  const text = await page.locator('#edPoints').textContent();
  return text.split(' · ').map((s) => s.match(/(\d+)° (\d+)%/).slice(1).map(Number));
}

async function drag(page, i, t, p) {
  const [from] = [(await editorPoints(page))[i]];
  const start = await screenPoint(page, ...from);
  const end = await screenPoint(page, t, p);
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(end.x, end.y, { steps: 8 });
  await page.mouse.up();
}

async function openFan(page, i) {
  await page.locator('#fans .fan').nth(i).click();
  await expect(page.locator('#sheet')).toBeVisible();
}

test('arrastar um ponto respeita as regras da curva', async ({ page }) => {
  await openFan(page, 1);
  await expect(page.locator('#edTitle')).toHaveText('Fan · System 1');
  expect(await editorPoints(page)).toEqual([[0, 25], [35, 25], [45, 35], [55, 55], [62, 65], [68, 75], [74, 85], [82, 100]]);

  // Up and to the left: never before the previous point, never above the next one.
  await drag(page, 3, 10, 95);
  expect((await editorPoints(page))[3]).toEqual([46, 65]);
  // Down and to the right: never after the next point, never below the previous one (or the 20% minimum).
  await drag(page, 3, 95, 0);
  expect((await editorPoints(page))[3]).toEqual([61, 35]);
  // The last point stays at 100%, at most at 90 °C (maxFullSpeedTemp).
  await drag(page, 7, 99, 10);
  expect((await editorPoints(page))[7]).toEqual([90, 100]);
  // The first point stays at 0 °C and never goes below the minimum.
  await drag(page, 0, 30, 0);
  expect((await editorPoints(page))[0]).toEqual([0, 20]);

  const points = await editorPoints(page);
  const sent = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/api/fans/1'));
  await page.locator('#edApply').click();
  expect((await sent).postDataJSON()).toEqual({ mode: 'curve', style: 'SmartFanMode', points, tempInput: 0 });
  await expect(page.locator('#toast')).toHaveText('Aplicado no chip da placa-mãe');
  await expect(page.locator('#sheet')).toBeHidden();
  await expect(page.locator('#fans .fan').nth(1).locator('.name')).toHaveText('System 1FanPilot');
  await expect(page.locator('#fans .fan').nth(1).locator('.desc')).toContainText('Curva personalizada · System 1');

  const fan = (await (await page.request.get(fp.url + '/api/fans')).json()).fans[1];
  expect(fan.managed).toBe(true);
  expect(fan.request.points).toEqual(points);
  expect(fan.points.map(([t]) => t)).toEqual(points.map(([t]) => t));
  expect(fan.points[fan.points.length - 1][1]).toBe(255);
});

test('uma predefinição troca a curva inteira', async ({ page }) => {
  await openFan(page, 2);
  await page.locator('#edPresets button', { hasText: 'Silencioso' }).click();
  expect(await editorPoints(page)).toEqual([[0, 25], [40, 25], [50, 30], [60, 40], [65, 50], [70, 65], [75, 80], [85, 100]]);
  await page.locator('#edStyle').selectOption('StairStepMode');
  const sent = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/api/fans/2'));
  await page.locator('#edApply').click();
  expect((await sent).postDataJSON()).toMatchObject({ mode: 'curve', style: 'StairStepMode', tempInput: 2 });
  await expect(page.locator('#fans .fan').nth(2).locator('.desc')).toContainText('Curva personalizada');
  const fan = (await (await page.request.get(fp.url + '/api/fans')).json()).fans[2];
  expect(fan.mode).toBe('StairStepMode');
});

test('velocidade fixa e depois a curva original de volta', async ({ page }) => {
  await openFan(page, 3);
  await page.locator('#edModeFixed').click();
  await expect(page.locator('#edFixedBox')).toBeVisible();
  await page.locator('#edFixedSlider').fill('40');
  await expect(page.locator('#edFixedValue')).toHaveText('40%');
  const sent = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/api/fans/3'));
  await page.locator('#edApply').click();
  expect((await sent).postDataJSON()).toEqual({ mode: 'fixed', percent: 40, tempInput: 0 });
  await expect(page.locator('#fans .fan').nth(3).locator('.desc')).toContainText('Fixo 40%');

  await openFan(page, 3);
  await expect(page.locator('#edModeFixed')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#edRestore')).toHaveText('Restaurar original');
  await page.locator('#edRestore').click();
  await expect(page.locator('#toast')).toHaveText('Curva original restaurada');
  await expect(page.locator('#fans .fan').nth(3).locator('.desc')).toContainText('Curva original');
  expect((await (await page.request.get(fp.url + '/api/fans')).json()).fans[3].managed).toBe(false);
});

test('fechar o editor não aplica nada', async ({ page }) => {
  await openFan(page, 0);
  await drag(page, 3, 60, 90);
  await page.locator('#edClose').click();
  await expect(page.locator('#sheet')).toBeHidden();
  expect((await (await page.request.get(fp.url + '/api/fans')).json()).fans[0].managed).toBe(false);
});
