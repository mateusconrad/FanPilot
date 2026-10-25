// The live stream (/api/stream): the panel reconnects by itself when FanPilot restarts or turns it away. A refused
// stream (503: FanPilot still starting, or too many panels open) is never retried by EventSource itself.
import http from 'node:http';
import { expect, test } from '@playwright/test';
import { FanPilot } from './fanpilot.mjs';

let fp;
test.beforeAll(async () => { fp = await FanPilot.start('demo'); });
test.afterAll(async () => { await fp?.stop(); });

test('o painel se reconecta quando o FanPilot reinicia', async ({ page }) => {
  await page.goto(fp.url + '/');
  const status = page.locator('#statusText');
  await expect(status).toHaveText('ao vivo');
  // "slow" takes 4 s to open the board, as the Gigabyte service can, and the stream answers 503 meanwhile: the
  // browser's first retry lands in that window.
  const restarted = fp.restart('slow');
  await expect(status).toHaveText('reconectando…');
  await restarted;
  await expect(status).toHaveText('ao vivo', { timeout: 20_000 });
});

// A stream held open by this process. A panel closed a moment ago keeps its slot until the hub's next write
// notices it, so a 503 is retried for a while.
async function openStream() {
  for (let attempt = 0; attempt < 20; attempt++) {
    const { req, res } = await new Promise((resolve, reject) => {
      const r = http.get(fp.url + '/api/stream', (s) => resolve({ req: r, res: s }));
      r.once('error', reject);
    });
    if (res.statusCode === 200) return { req, res };
    req.destroy();
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  throw new Error('O hub não abriu uma vaga em 6 s.');
}

test('o painel recusado por excesso de conexões entra quando uma vaga abre', async ({ page }) => {
  // SseHub.MaxClients panels already open (from this process: the browser keeps only 6 connections per host).
  const open = [];
  try {
    for (let i = 0; i < 16; i++) open.push(await openStream());
    await page.goto(fp.url + '/');
    const status = page.locator('#statusText');
    await expect(status).toHaveText('reconectando…');
    open.pop().req.destroy();
    await expect(status).toHaveText('ao vivo', { timeout: 20_000 });
  } finally {
    for (const { req } of open) req.destroy();
  }
});
