// The panel on the demo board (the B760M FanPilot was developed on): what it shows, Turbo and the GPU fan.
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

test.beforeEach(async ({ page }) => {
  await page.goto(fp.url + '/');
  await expect(page.locator('#statusText')).toHaveText('ao vivo');
});

test('mostra temperaturas, fans, gráficos e sensores', async ({ page }) => {
  await expect(page.locator('#tiles .tile')).toHaveCount(4);
  await expect(page.locator('#tiles .tile').first()).toContainText('CPU');
  await expect(page.locator('#fans .fan .name')).toHaveText(['CPU', 'System 1', 'System 2', 'System 3']);
  // CPU and System 3 have nothing with a tachometer plugged in, as on the real board.
  await expect(page.locator('#fans .fan .rpm').first()).toHaveText('sem leitura');
  await expect(page.locator('#fans .fan .rpm').nth(1)).toContainText('RPM');
  await expect(page.locator('#fans .fan .desc').first()).toContainText('Curva original · CPU');
  await expect(page.locator('#chart path.series')).toHaveCount(2);
  await expect(page.locator('#rpmChart path.series')).toHaveCount(2);
  await expect(page.locator('#rpmNote')).toHaveText('Sem leitura de RPM (0): CPU, System 3 — não aparecem no gráfico.');
  await expect(page.locator('#gpuName')).toHaveText('GPU de teste');

  await page.locator('summary', { hasText: 'Todos os sensores' }).click();
  await expect(page.locator('#sensorTable tr.group')).toHaveText(['Temperaturas', 'Fans', 'Tensões', 'Clocks', 'Potência']);
  await expect(page.locator('#sensorTable')).toContainText('VCore');
  await expect(page.locator('#appVersion')).toHaveText(/^FanPilot \d+\.\d+\.\d+(\+[0-9a-f]{7})? · Placa de teste$/);
});

test('Turbo põe todos os fans em 100% e desligar devolve o que estava', async ({ page }) => {
  const turbo = page.locator('#turboBtn');
  await turbo.click();
  await expect(turbo).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#turboBanner')).toBeVisible();
  await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Turbo 100%/));
  expect((await (await page.request.get(fp.url + '/api/fans')).json()).turbo).toBe(true);

  // The editor stays closed while Turbo is on.
  await page.locator('#fans .fan').nth(1).click();
  await expect(page.locator('#toast')).toHaveText('Desligue o Turbo para ajustar um fan individualmente.');
  await expect(page.locator('#sheet')).toBeHidden();

  await turbo.click();
  await expect(turbo).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('#turboBanner')).toBeHidden();
  await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Curva original/));
});

// Item 18: a profile on every fan, Turbo on top of it, and back to the user's own settings.
test('um perfil vai para todos os fans e Personalizado devolve o que havia', async ({ page }) => {
  const seg = page.locator('#profileSeg');
  await expect(seg.getByRole('button', { name: 'Personalizado' })).toHaveAttribute('aria-pressed', 'true');
  await seg.getByRole('button', { name: 'Silencioso' }).click();
  await expect(page.locator('#toast')).toHaveText('Perfil Silencioso em todos os fans (menos as bombas)');
  await expect(seg.getByRole('button', { name: 'Silencioso' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Perfil Silencioso/));

  await page.locator('#turboBtn').click();
  await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Turbo 100%/));
  await page.locator('#turboBtn').click();
  await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Perfil Silencioso/));

  await seg.getByRole('button', { name: 'Personalizado' }).click();
  await expect(page.locator('#toast')).toHaveText('Configuração pessoal de volta');
  await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Curva original/));
  expect((await (await page.request.get(fp.url + '/api/fans')).json()).mode).toBe('custom');
});

test('fan da GPU: fixo e de volta ao automático', async ({ page }) => {
  await page.locator('#gpuFixed').click();
  await expect(page.locator('#gpuFixedBox')).toBeVisible();
  await page.locator('#gpuSlider').fill('60');
  await page.locator('#gpuApply').click();
  await expect(page.locator('#toast')).toHaveText('Fan da GPU fixado em 60%');
  await expect(page.locator('#gpuSub')).toHaveText('fan manual');
  await expect(page.locator('#gpuFanValue')).toHaveText('fan 60%');

  await page.locator('#gpuAuto').click();
  await expect(page.locator('#toast')).toHaveText('Fan da GPU em automático');
  await expect(page.locator('#gpuSub')).toHaveText('fan automático');
  await expect(page.locator('#gpuFixedBox')).toBeHidden();
});
