// End-to-end tests: the panel in a real browser against FanPilot.exe on the fake board (fanpilot.mjs), plus the
// release zip's smoke test when FANPILOT_ZIP names it (the CI). Run from tests\e2e: npx playwright test.
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  testMatch: /.*\.spec\.mjs$/,
  // FanPilot is a single-instance app, fake board included: one at a time.
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  globalSetup: './global-setup.mjs',
  globalTeardown: './global-teardown.mjs',
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    // The Edge that comes with Windows, the CI runner's included: no browser to download.
    channel: 'msedge',
    locale: 'pt-BR',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
