// Screenshots of the panel and the editor at phone and desktop widths, in both themes, on the demo and the worst-case
// boards: kept in screens\ (a CI artifact) and in the HTML report, to see what a change did to the layout. No pixel
// comparison yet; what is checked is that nothing is wider than the screen.
import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

const outDir = path.join(import.meta.dirname, 'screens');

async function shot(page, testInfo, name, options = {}) {
  fs.mkdirSync(outDir, { recursive: true });
  const file = path.join(outDir, name + '.png');
  await page.screenshot({ path: file, animations: 'disabled', ...options });
  await testInfo.attach(name, { path: file, contentType: 'image/png' });
}

for (const scenario of ['demo', 'worst']) {
  test.describe(scenario, () => {
    let fp;
    test.beforeAll(async () => { fp = await FanPilot.start(scenario); });
    test.afterAll(async () => { await fp?.stop(); });

    for (const width of [320, 390, 1024]) {
      for (const colorScheme of ['light', 'dark']) {
        test(`${width} px, tema ${colorScheme === 'light' ? 'claro' : 'escuro'}`, async ({ page }, testInfo) => {
          await page.setViewportSize({ width, height: 900 });
          await page.emulateMedia({ colorScheme, reducedMotion: 'reduce' });
          await page.goto(fp.url + '/');
          await expect(page.locator('#statusText')).toHaveText('ao vivo');
          await expect(page.locator('#chart path.series')).not.toHaveCount(0);
          expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
          await shot(page, testInfo, `${scenario}-painel-${width}-${colorScheme}`, { fullPage: true });

          await page.locator('#fans .fan').nth(1).click();
          await expect(page.locator('#sheet')).toBeVisible();
          await shot(page, testInfo, `${scenario}-editor-${width}-${colorScheme}`);
        });
      }
    }
  });
}
