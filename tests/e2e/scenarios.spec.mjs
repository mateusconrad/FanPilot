// The boards the panel has to cope with besides the demo one (FakeBoard.Scenarios), each on its own FanPilot.
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

function onScenario(scenario, options) {
  let fp;
  test.beforeAll(async () => { fp = await FanPilot.start(scenario, options); });
  test.afterAll(async () => { await fp?.stop(); });
  return () => fp;
}

test.describe('pior caso: placa grande, nomes longos, sensor desconectado', () => {
  const fp = onScenario('worst', {
    files: {
      // Saved for a header this board does not have: shown, never applied, discardable.
      'desired-curves.json': JSON.stringify({ version: 2, fans: { 'fake/9': { title: 'SYS_FAN9_PUMP', request: { mode: 'fixed', percent: 100 } } } }),
      // Damaged on disk: set aside, and the panel says so.
      'turbo.json': '{ "active": tr',
    },
  });

  test('cabe na tela do celular e mostra os avisos', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto(fp().url + '/');
    await expect(page.locator('#statusText')).toHaveText('ao vivo');
    await expect(page.locator('#fans .fan')).toHaveCount(8);
    await expect(page.locator('#fans .fan .name').last()).toHaveText('SYS_FAN6_PUMP · bomba do watercooler ligada no conector de 4 pinos');
    await expect(page.locator('#gpuName')).toHaveText('NVIDIA GeForce RTX 5090 D v2 Laptop GPU');
    await expect(page.locator('#notices')).toContainText('turbo.json estava ilegível');
    // Nothing wider than a 320 px phone.
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);

    await page.locator('summary', { hasText: 'Todos os sensores' }).click();
    await expect(page.locator('#sensorTable')).toContainText('127 °C');
    await expect(page.locator('#sensorTable')).toContainText('-128 °C');
  });

  test('a configuração órfã pode ser descartada', async ({ page }) => {
    await page.goto(fp().url + '/');
    const orphan = page.locator('#orphans .orphan');
    await expect(orphan).toContainText('"SYS_FAN9_PUMP" não corresponde a nenhum fan desta placa');
    await orphan.getByRole('button', { name: 'Descartar' }).click();
    await expect(page.locator('#toast')).toHaveText('Configuração descartada');
    await expect(orphan).toHaveCount(0);
    expect(fp().readJson('desired-curves.json').fans).toEqual({});
  });
});

test.describe('placa sem controle de fans e sem GPU', () => {
  const fp = onScenario('unsupported');

  test('só monitora e diz por quê', async ({ page }) => {
    await page.goto(fp().url + '/');
    await expect(page.locator('#statusText')).toHaveText('ao vivo');
    await expect(page.locator('#fans')).toContainText('não oferece controle de fans pelo caminho que o FanPilot usa. O monitoramento continua funcionando.');
    await expect(page.locator('#turboBtn')).toBeHidden();
    await expect(page.locator('#gpuGrid')).toHaveText('GPU indisponível: NVML não encontrado (nvml.dll): instale o driver da NVIDIA.');
    await expect(page.locator('#gpuControls')).toBeHidden();
  });
});

test.describe('serviço da placa fora do ar', () => {
  const fp = onScenario('down');

  test('o painel mostra o erro e a GPU continua', async ({ page }) => {
    await page.goto(fp().url + '/');
    await expect(page.locator('#statusText')).toHaveText('sensores da placa indisponíveis');
    await expect(page.locator('#statusDot')).toHaveClass(/bad/);
    await expect(page.locator('#fans')).toHaveText('Fans indisponíveis: EasyTuneEngineService indisponível: Falha ao se conectar a uma porta de IPC');
    await expect(page.locator('#gpuName')).toHaveText('GPU de teste');
  });
});

test.describe('LibreHardwareMonitor: curvas executadas pelo FanPilot', () => {
  const fp = onScenario('lhm');

  test('fans começam na BIOS, recebem uma curva e voltam à BIOS', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(fp().url + '/');
    await expect(page.locator('#statusText')).toHaveText('ao vivo');
    await expect(page.locator('#fanMode')).toBeVisible();
    await expect(page.locator('#fans .fan .desc')).toHaveText(Array(4).fill(/^Controle da BIOS/));

    await page.locator('#fans .fan').nth(1).click();
    await expect(page.locator('#edRestore')).toHaveText('Devolver à BIOS');
    await expect(page.locator('#edRestore')).toBeDisabled();
    await page.locator('#edApply').click();
    await expect(page.locator('#toast')).toHaveText('Aplicado: o FanPilot está controlando este fan');
    await expect(page.locator('#fans .fan').nth(1).locator('.desc')).toContainText('Curva personalizada');

    await page.locator('#fans .fan').nth(1).click();
    await expect(page.locator('#edRestore')).toBeEnabled();
    await page.locator('#edRestore').click();
    await expect(page.locator('#toast')).toHaveText('Fan devolvido ao controle da BIOS');
    await expect(page.locator('#fans .fan').nth(1).locator('.desc')).toContainText('Controle da BIOS');
  });
});
