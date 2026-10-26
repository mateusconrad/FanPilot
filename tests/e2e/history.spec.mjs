// The history on disk (item 24): the charts' last hour, day and week, read back from data\history\ (seeded here with
// a week of rows as FanPilot writes them), still there after a restart, and the panel without it when it is off.
import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

const DAY = 86_400_000;
const HEADER = 't,cpu-main,cpu-main:max,cpu-power,cpu-power:max,fan-0,fan-0:max,fan-1,fan-1:max,fan-2,fan-2:max,' +
  'fan-3,fan-3:max,gpu-power,gpu-power:max,gpu-temp,gpu-temp:max';
const pad = (n) => String(n).padStart(2, '0');
// FanPilot names the files by the local date.
const fileOf = (ms) => { const d = new Date(ms); return `history/${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}.csv`; };

// A row every 10 s for a week, up to a minute before now: the CPU at 45 °C, but at 88 (91 at most) for six hours
// three days ago.
function week(now) {
  const files = {};
  const spike = now - 3 * DAY;
  for (let t = Math.ceil((now - 7 * DAY) / 10_000) * 10_000; t < now - 60_000; t += 10_000) {
    const cpu = Math.abs(t - spike) < 3 * 3_600_000 ? '88,91' : '45,47';
    (files[fileOf(t)] ??= [HEADER]).push(`${t},${cpu},35,40,1200,1250,700,720,0,0,1500,1550,90,110,50,52`);
  }
  return Object.fromEntries(Object.entries(files).map(([name, rows]) => [name, rows.join('\n') + '\n']));
}

const outDir = path.join(import.meta.dirname, 'screens');
async function shot(page, testInfo, name) {
  fs.mkdirSync(outDir, { recursive: true });
  const file = path.join(outDir, name + '.png');
  await page.screenshot({ path: file, fullPage: true, animations: 'disabled' });
  await testInfo.attach(name, { path: file, contentType: 'image/png' });
}

const getJson = async (url) => (await fetch(url)).json();

async function openPanel(page, url) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(url + '/');
  await expect(page.locator('#statusText')).toHaveText('ao vivo');
  // /api/info answered: the period selector is shown or hidden for good.
  await expect(page.locator('#appVersion')).toContainText('FanPilot');
}

const rangeButton = (page, name, which = 'first') => page.locator('.seg.range')[which]().getByRole('button', { name, exact: true });

test.describe('com uma semana gravada', () => {
  const seeded = Date.now();
  let fp;
  test.beforeAll(async () => { fp = await FanPilot.start('demo', { files: week(seeded) }); });
  test.afterAll(async () => { await fp?.stop(); });

  test('7 dias: a semana inteira, e na dica a média e o máximo do pico de três dias atrás', async ({ page }, testInfo) => {
    await openPanel(page, fp.url);
    await expect(page.locator('#chartTitle')).toHaveText('Temperaturas · últimos 10 min');
    await expect(rangeButton(page, '10 min')).toHaveAttribute('aria-pressed', 'true');
    await rangeButton(page, '7 dias').click();
    await expect(page.locator('#chartTitle')).toHaveText('Temperaturas · últimos 7 dias');
    await expect(page.locator('#rpmTitle')).toHaveText('Fans · RPM · últimos 7 dias');
    await expect(page.locator('#chart')).toHaveAttribute('aria-label', 'Temperatura da CPU e GPU (últimos 7 dias)');
    // One period for both charts.
    await expect(rangeButton(page, '7 dias', 'last')).toHaveAttribute('aria-pressed', 'true');
    await expect(rangeButton(page, '10 min', 'last')).toHaveAttribute('aria-pressed', 'false');
    const threeDays = page.locator('#chart .axis text', { hasText: /^-3 d$/ });
    await expect(threeDays).toHaveCount(1);
    // The line starts a week back, at the left edge of the plot (38 px).
    const cpu = page.locator('#chart path.series[stroke="var(--series-cpu)"]');
    expect(Number((await cpu.getAttribute('d')).match(/^M([\d.]+)/)[1])).toBeLessThan(45);
    // Right above the "-3 d" mark: the spike, with the average of the point and the highest of its rows.
    const mark = await threeDays.boundingBox();
    const box = await page.locator('#chart').boundingBox();
    await page.mouse.move(mark.x + mark.width / 2, box.y + box.height / 2);
    await expect(page.locator('#tip')).toBeVisible();
    await expect(page.locator('#tip')).toContainText('CPU 88 °C · máx. 91');
    await shot(page, testInfo, 'historico-7-dias');
  });

  test('24 h e 1 h nos dois gráficos; o período escolhido volta ao recarregar', async ({ page }) => {
    await openPanel(page, fp.url);
    await rangeButton(page, '24 h').click();
    await expect(page.locator('#chartTitle')).toHaveText('Temperaturas · últimas 24 h');
    await expect(page.locator('#chart .axis text', { hasText: /^-12 h$/ })).toHaveCount(1);
    const cpu = page.locator('#chart path.series[stroke="var(--series-cpu)"]');
    expect(Number((await cpu.getAttribute('d')).match(/^M([\d.]+)/)[1])).toBeLessThan(45);
    // The fans' chart too: the seeded RPM (fan 2 never turned, so it is left out).
    await expect(page.locator('#rpmChart path.series')).not.toHaveCount(0);

    await page.reload();
    await expect(page.locator('#chartTitle')).toHaveText('Temperaturas · últimas 24 h');
    await expect(rangeButton(page, '24 h')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#chart .axis text', { hasText: /^-12 h$/ })).toHaveCount(1);

    await rangeButton(page, '1 h', 'last').click();
    await expect(page.locator('#rpmTitle')).toHaveText('Fans · RPM · última hora');
    await expect(page.locator('#chart .axis text', { hasText: /^-30 min$/ })).toHaveCount(1);
    // Back to the live 10 minutes, which follow the stream again.
    await rangeButton(page, '10 min').click();
    await expect(page.locator('#chartTitle')).toHaveText('Temperaturas · últimos 10 min');
    await expect(page.locator('#chart .axis text', { hasText: /^-10 min$/ })).toHaveCount(1);
  });

  // Item 24's acceptance: the week in under a second on a PC.
  test('a semana chega e aparece em menos de 1 s', async ({ page }) => {
    const t0 = performance.now();
    const data = await getJson(fp.url + '/api/history?range=7d');
    const ms = performance.now() - t0;
    expect(data.t.length).toBeLessThanOrEqual(600);
    expect(data.series['cpu-main'].filter((v) => v !== null).length).toBeGreaterThan(590);
    expect(ms, `a API levou ${Math.round(ms)} ms`).toBeLessThan(1000);

    await openPanel(page, fp.url);
    const start = Date.now();
    await rangeButton(page, '7 dias').click();
    await expect(page.locator('#chart .axis text', { hasText: /^-3 d$/ })).toHaveCount(1);
    const shown = Date.now() - start;
    expect(shown, `o painel levou ${shown} ms`).toBeLessThan(1000);
  });

  test('no celular, o seletor cabe sem rolar de lado', async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 360, height: 800 });
    await openPanel(page, fp.url);
    await rangeButton(page, '7 dias').click();
    // Narrow: a mark every other day.
    await expect(page.locator('#chart .axis text', { hasText: /^-2 d$/ })).toHaveCount(1);
    await expect(page.locator('#chart .axis text', { hasText: /^-3 d$/ })).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
    // Large enough to tap.
    const button = await rangeButton(page, '24 h').boundingBox();
    expect(button.height).toBeGreaterThanOrEqual(24);
    expect(button.width).toBeGreaterThanOrEqual(24);
    await page.locator('#chart').scrollIntoViewIfNeeded();
    await shot(page, testInfo, 'historico-celular');
  });

  // Item 24's first criterion: after a restart, what was recorded is still there.
  test('depois de reiniciar o FanPilot, o que ele gravou continua lá', async () => {
    // The rows of this run: after the seeded week, which ends a minute before the start.
    const recorded = async () => {
      const h = await getJson(fp.url + '/api/history?range=1h');
      return h.t.filter((t, i) => t > seeded - 30_000 && h.series['cpu-main'][i] !== null).length;
    };
    await expect.poll(recorded, { timeout: 40_000 }).toBeGreaterThanOrEqual(2);
    const before = await recorded();
    await fp.restart('demo');
    expect(await recorded()).toBeGreaterThanOrEqual(before);
    const day = await getJson(fp.url + '/api/history?range=24h');
    expect(day.series['cpu-main'].filter((v) => v !== null).length).toBeGreaterThan(500);
  });
});

test.describe('com o histórico desligado ("historyDays": 0)', () => {
  let fp;
  test.beforeAll(async () => { fp = await FanPilot.start('demo', { config: { historyDays: 0 } }); });
  test.afterAll(async () => { await fp?.stop(); });

  test('só os 10 minutos: sem seletor, nada em disco, e a API diz por quê', async ({ page }) => {
    // Even with a longer period remembered from before.
    await page.addInitScript(() => localStorage.setItem('fanpilot.range', '7d'));
    await openPanel(page, fp.url);
    await expect(page.locator('#chartTitle')).toHaveText('Temperaturas · últimos 10 min');
    await expect(page.locator('.seg.range').first()).toBeHidden();
    await expect(page.locator('#chart path.series').first()).toBeVisible();

    expect((await getJson(fp.url + '/api/info')).history).toBe(false);
    const off = await fetch(fp.url + '/api/history?range=24h');
    expect(off.status).toBe(503);
    expect((await off.json()).code).toBe('err.historyOff');
    const bad = await fetch(fp.url + '/api/history?range=2h');
    expect(bad.status).toBe(400);
    expect((await bad.json()).code).toBe('err.historyRange');
    expect(fs.existsSync(path.join(fp.dataDir, 'history'))).toBe(false);
  });
});
