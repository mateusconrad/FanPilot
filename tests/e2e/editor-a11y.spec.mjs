// The fan editor by keyboard, by screen reader and by touch (item 20), on a demo board of its own: every fan starts
// on the default curve (FakeBoard.DefaultCurve).
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(fp.url + '/');
  await expect(page.locator('#statusText')).toHaveText('ao vivo');
});

const DEFAULT = [[0, 25], [35, 25], [45, 35], [55, 55], [62, 65], [68, 75], [74, 85], [82, 100]];

// "0° 25% · 35° 25% · …" as [[0, 25], [35, 25], …].
async function editorPoints(page) {
  const text = await page.locator('#edPoints').textContent();
  return text.split(' · ').map((s) => s.match(/(\d+)° (\d+)%/).slice(1).map(Number));
}

async function fanState(page, i) {
  const fan = (await (await page.request.get(fp.url + '/api/fans')).json()).fans[i];
  return [fan.managed, fan.points, fan.request];
}

// Presses Tab until the target has the focus, as someone without a mouse would.
async function tabUntil(page, target, max = 40) {
  for (let i = 0; i < max; i++) {
    if (await target.evaluate((n) => n === document.activeElement)) return;
    await page.keyboard.press('Tab');
  }
  throw new Error('Tab não chegou ao alvo');
}

test('dá para montar uma curva só com o teclado', async ({ page }) => {
  const card = page.locator('#fans .fan').nth(1);
  await card.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#sheet')).toBeVisible();
  // The focus goes into the sheet, and the page behind it is out of reach.
  await expect(page.locator('#edClose')).toBeFocused();
  await expect(page.locator('main')).toHaveJSProperty('inert', true);

  // Each point is a slider that says where it is; reaching one selects it for the fine-tune bar.
  const second = page.getByRole('slider', { name: 'Ponto 2 de 8' });
  await tabUntil(page, second);
  await expect(second).toHaveAttribute('aria-valuetext', '35 °C, 25%');
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 2 de 8: 35 °C, 25%');
  for (const key of ['ArrowRight', 'Shift+ArrowRight', 'ArrowUp', 'Shift+ArrowUp', 'Shift+ArrowUp', 'Shift+ArrowRight']) {
    await page.keyboard.press(key);
  }
  // 35 + 1 + 5 + 5 stops 1 °C before the next point (45 °C); 25 + 1 + 5 + 5 stops at its 35%.
  await expect(second).toHaveAttribute('aria-valuetext', '44 °C, 35%');
  await expect(second).toHaveAttribute('aria-valuenow', '35');
  await expect(second).toBeFocused();

  await page.keyboard.press('Tab');
  const third = page.getByRole('slider', { name: 'Ponto 3 de 8' });
  await expect(third).toBeFocused();
  // End: as fast as the next point lets it; Home: as slow as the previous one.
  await page.keyboard.press('End');
  await expect(third).toHaveAttribute('aria-valuetext', '45 °C, 55%');
  await page.keyboard.press('Home');
  await expect(third).toHaveAttribute('aria-valuetext', '45 °C, 35%');
  await page.keyboard.press('PageUp');
  await page.keyboard.press('Shift+ArrowRight');
  await expect(third).toHaveAttribute('aria-valuetext', '50 °C, 45%');

  const expected = [[0, 25], [44, 35], [50, 45], ...DEFAULT.slice(3)];
  expect(await editorPoints(page)).toEqual(expected);
  await tabUntil(page, page.locator('#edApply'));
  const sent = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/api/fans/1'));
  await page.keyboard.press('Enter');
  expect((await sent).postDataJSON().points).toEqual(expected);
  await expect(page.locator('#toast')).toHaveText('Aplicado no chip da placa-mãe');
  await expect(page.locator('#sheet')).toBeHidden();
  // Back on the fan it was opened from, with the page reachable again.
  await expect(card).toBeFocused();
  await expect(page.locator('main')).toHaveJSProperty('inert', false);
});

test('Tab dá a volta dentro da folha', async ({ page }) => {
  await page.locator('#fans .fan').nth(0).click();
  await expect(page.locator('#edClose')).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(page.locator('#edApply')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.locator('#edClose')).toBeFocused();
});

test('Esc fecha e devolve o foco ao fan; alterações não aplicadas pedem confirmação', async ({ page }) => {
  const card = page.locator('#fans .fan').nth(2);
  await card.click();
  await expect(page.locator('#edClose')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.locator('#sheet')).toBeHidden();
  await expect(card).toBeFocused();

  const before = await fanState(page, 2);
  await card.click();
  const point = page.getByRole('slider', { name: 'Ponto 4 de 8' });
  await point.focus();
  await page.keyboard.press('ArrowUp');
  // Esc asks inside the sheet; Esc again goes back to where the focus was.
  await page.keyboard.press('Escape');
  await expect(page.locator('#edDiscard')).toBeVisible();
  await expect(page.locator('#edKeep')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.locator('#edDiscard')).toBeHidden();
  await expect(point).toBeFocused();
  // "Fechar" and a tap outside the sheet ask too.
  await page.locator('#edClose').click();
  await expect(page.locator('#edDiscard')).toBeVisible();
  await page.locator('#edKeep').click();
  await expect(page.locator('#edDiscard')).toBeHidden();
  await page.locator('#backdrop').click({ position: { x: 10, y: 10 } });
  await expect(page.locator('#edDiscard')).toBeVisible();
  await page.locator('#edDiscardYes').click();
  await expect(page.locator('#sheet')).toBeHidden();
  await expect(card).toBeFocused();
  expect(await fanState(page, 2)).toEqual(before);

  // A change undone by hand leaves nothing to discard; a name typed and not saved does.
  await card.click();
  await point.focus();
  await page.keyboard.press('ArrowUp');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Escape');
  await expect(page.locator('#sheet')).toBeHidden();
  await card.click();
  await page.locator('#edMeta summary').click();
  await page.locator('#edLabel').fill('Traseira');
  await page.keyboard.press('Escape');
  await expect(page.locator('#edDiscard')).toBeVisible();
});

test('salvar o nome não joga fora uma curva ainda não aplicada', async ({ page }) => {
  await page.locator('#fans .fan').nth(3).click();
  await page.getByRole('slider', { name: 'Ponto 4 de 8' }).focus();
  await page.keyboard.press('ArrowUp');
  await page.locator('#edMeta summary').click();
  await page.locator('#edLabel').fill('Frente');
  await page.locator('#edMetaSave').click();
  await expect(page.locator('#toast')).toHaveText('Nome e função salvos');
  // Still open, with the new name and the curve as it was being edited.
  await expect(page.locator('#sheet')).toBeVisible();
  await expect(page.locator('#edTitle')).toHaveText('Fan · Frente');
  expect((await editorPoints(page))[3]).toEqual([55, 56]);
  // The name is saved: only the curve is left to apply or discard.
  await page.locator('#edClose').click();
  await expect(page.locator('#edDiscard')).toBeVisible();
  await page.locator('#edDiscardYes').click();
  await expect(page.locator('#fans .fan').nth(3).locator('.name')).toContainText('Frente');
});

test('segurar + repete até o limite, e cada clique é um passo', async ({ page }) => {
  await page.locator('#fans .fan').nth(0).click();
  // A click on a point selects it without moving it.
  await page.getByRole('slider', { name: 'Ponto 3 de 8' }).click();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 3 de 8: 45 °C, 35%');
  expect(await editorPoints(page)).toEqual(DEFAULT);

  await page.getByRole('button', { name: 'Aumentar 1 °C' }).click();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 3 de 8: 46 °C, 35%');
  // Held down, + keeps going until the next point's speed (55%).
  const box = await page.getByRole('button', { name: 'Aumentar 1%' }).boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 3 de 8: 46 °C, 55%');
  await page.mouse.up();
  await page.getByRole('button', { name: 'Diminuir 1%' }).click();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 3 de 8: 46 °C, 54%');
  // Screen readers hear what the buttons did.
  await expect(page.locator('#edTuneLive')).toHaveText('Ponto 3 de 8: 46 °C, 54%');
  expect(await editorPoints(page)).toEqual([DEFAULT[0], DEFAULT[1], [46, 54], ...DEFAULT.slice(3)]);

  // ◀ ▶ go through the points, around the ends; the first point's temperature and the last one's speed are fixed.
  await page.getByRole('button', { name: 'Ponto anterior' }).click();
  await page.getByRole('button', { name: 'Ponto anterior' }).click();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 1 de 8: 0 °C, 25%');
  await expect(page.getByRole('button', { name: 'Aumentar 1 °C' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Aumentar 1%' })).toBeEnabled();
  await page.getByRole('button', { name: 'Ponto anterior' }).click();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 8 de 8: 82 °C, 100%');
  await expect(page.getByRole('button', { name: 'Aumentar 1%' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Aumentar 1 °C' })).toBeEnabled();
  await page.getByRole('button', { name: 'Próximo ponto' }).click();
  await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 1 de 8: 0 °C, 25%');
});

test.describe('no celular', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });

  test('ajusta um ponto em 1 °C sem arrastar', async ({ page }) => {
    await page.locator('#fans .fan').nth(0).tap();
    await expect(page.locator('#edTuneHint')).toBeVisible();
    await page.getByRole('slider', { name: 'Ponto 3 de 8' }).tap();
    await expect(page.locator('#edTune')).toBeVisible();
    await expect(page.locator('#edTuneHint')).toBeHidden();
    await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 3 de 8: 45 °C, 35%');
    await page.getByRole('button', { name: 'Diminuir 1 °C' }).tap();
    await expect(page.locator('#edTuneLabel')).toHaveText('Ponto 3 de 8: 44 °C, 35%');
    expect((await editorPoints(page))[2]).toEqual([44, 35]);
  });
});

// Every control of the sheet at least 44 px, the points' targets inside the chart, and nothing wider than the screen.
for (const width of [320, 390, 1024]) {
  test(`alvos de toque de pelo menos 44 px em ${width} px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await page.locator('#fans .fan').nth(0).click();
    await page.locator('#edMeta summary').click();
    await page.getByRole('slider', { name: 'Ponto 3 de 8' }).focus();
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Escape');
    await expect(page.locator('#edDiscard')).toBeVisible();
    await expect(page.locator('#edTune')).toBeVisible();

    const measure = () => page.evaluate(() => {
      const svg = document.getElementById('edSvg').getBoundingClientRect();
      const out = [];
      for (const n of document.getElementById('sheet').querySelectorAll('button, select, input, summary, [role=slider]')) {
        if (!n.checkVisibility()) continue;
        const slider = n.matches('[role=slider]');
        const r = (slider ? n.querySelector('.hit') : n).getBoundingClientRect();
        const name = n.id || n.getAttribute('aria-label') || n.textContent.trim();
        if (r.width < 44 || r.height < 44) out.push(`${name}: ${r.width.toFixed(1)}×${r.height.toFixed(1)}`);
        if (slider && (r.top < svg.top - 0.5 || r.bottom > svg.bottom + 0.5 || r.left < svg.left - 0.5 || r.right > svg.right + 0.5)) {
          out.push(`${name}: alvo fora do gráfico`);
        }
      }
      return out;
    });
    expect(await measure()).toEqual([]);
    expect(await page.evaluate(() => document.getElementById('sheet').scrollWidth)).toBeLessThanOrEqual(width);

    await page.locator('#edKeep').click();
    await page.locator('#edModeFixed').click();
    await expect(page.locator('#edFixedSlider')).toBeVisible();
    expect(await measure()).toEqual([]);
  });
}
