// Accessibility of the main screens, by axe-core: no serious or critical violation, in both themes.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

async function seriousViolations(page) {
  const { violations } = await new AxeBuilder({ page }).analyze();
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id} (${v.impact}): ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
}

for (const colorScheme of ['light', 'dark']) {
  test.describe(`tema ${colorScheme === 'light' ? 'claro' : 'escuro'}`, () => {
    test.beforeEach(async ({ page }) => { await page.emulateMedia({ colorScheme, reducedMotion: 'reduce' }); });

    test('painel', async ({ page }) => {
      await page.goto(fp.url + '/');
      await expect(page.locator('#statusText')).toHaveText('ao vivo');
      await page.locator('summary', { hasText: 'Todos os sensores' }).click();
      expect(await seriousViolations(page)).toEqual([]);
    });

    test('editor de curva', async ({ page }) => {
      await page.goto(fp.url + '/');
      await page.locator('#fans .fan').nth(1).click();
      await expect(page.locator('#sheet')).toBeVisible();
      expect(await seriousViolations(page)).toEqual([]);
    });

    test('pareamento', async ({ page }) => {
      await page.goto(fp.url + '/pair');
      await expect(page.locator('#qr svg')).toBeVisible();
      expect(await seriousViolations(page)).toEqual([]);
    });
  });
}
