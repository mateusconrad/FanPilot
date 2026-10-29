// The release zip as a user gets it: extracted, FanPilot.exe started on this machine's real board (on the CI runner,
// no Gigabyte Control Center and no PawnIO: a board FanPilot can only report on), the panel and the API answering,
// then --version and --diagnose. Runs when FANPILOT_ZIP names the zip (the CI sets it) and needs Administrator, as
// FanPilot itself does. The data folder starts empty, so nothing is ever applied to a fan.
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { expect, test } from '@playwright/test';
import { freePort, hasExited, isAdmin, powershell, scratchRoot } from './fanpilot.mjs';

const zip = process.env.FANPILOT_ZIP;
test.skip(!zip || process.platform !== 'win32', 'FANPILOT_ZIP não definido: o smoke do zip roda na CI do Windows');
test.skip(!isAdmin, 'o FanPilot.exe da release precisa de Administrador');

const version = zip && path.basename(zip).match(/^FanPilot-(.+)-win-x64\.zip$/)[1];
let dir, proc, port, output = '';

function run(args) {
  try {
    return { code: 0, out: execFileSync(path.join(dir, 'FanPilot.exe'), args, { cwd: dir, encoding: 'utf8', timeout: 120_000 }) };
  } catch (e) {
    return { code: e.status, out: (e.stdout || '') + (e.stderr || '') };
  }
}

// The state files, to prove --diagnose wrote none of them.
function stateFiles() {
  const data = path.join(dir, 'data');
  return Object.fromEntries(fs.readdirSync(data).filter((f) => f.endsWith('.json')).map((f) => [f, fs.readFileSync(path.join(data, f), 'utf8')]));
}

test.beforeAll(async () => {
  fs.mkdirSync(scratchRoot, { recursive: true });
  dir = fs.mkdtempSync(path.join(scratchRoot, 'zip-'));
  powershell('Expand-Archive -LiteralPath $env:ZIP -DestinationPath $env:DEST', { ZIP: zip, DEST: dir });
  port = await freePort();
  fs.mkdirSync(path.join(dir, 'data'));
  fs.writeFileSync(path.join(dir, 'data', 'config.json'), JSON.stringify({ port }));
  proc = spawn(path.join(dir, 'FanPilot.exe'), ['--no-browser'], { cwd: dir, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
  proc.stdout.on('data', (d) => { output += d; });
  proc.stderr.on('data', (d) => { output += d; });
  for (const until = Date.now() + 90_000; Date.now() < until;) {
    if (hasExited(proc)) throw new Error(`O FanPilot.exe do zip saiu com ${proc.exitCode ?? proc.signalCode}:\n${output}`);
    try {
      if ((await fetch(`http://localhost:${port}/api/info`)).ok) return;
    } catch { /* not listening yet */ }
    await sleep(250);
  }
  throw new Error(`O FanPilot.exe do zip não respondeu em 90 s:\n${output}`);
});

async function stopServer() {
  if (!proc || hasExited(proc)) return;
  const exited = new Promise((resolve) => proc.once('exit', resolve));
  proc.kill();
  await exited;
}

test.afterAll(async () => {
  await stopServer();
  if (dir) fs.rmSync(dir, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
});

test('o exe do zip sobe e responde o painel e a API', async ({ request }) => {
  const base = `http://localhost:${port}`;
  const panel = await request.get(base + '/');
  expect(panel.status()).toBe(200);
  expect(await panel.text()).toContain('<title>FanPilot</title>');
  expect((await request.get(base + '/pair')).status()).toBe(200);
  const state = await request.get(base + '/api/state');
  expect(state.status()).toBe(200);
  expect(await state.json()).toHaveProperty('service');
  const info = await (await request.get(base + '/api/info')).json();
  expect(info.version).toMatch(new RegExp(`^${version.replace(/\./g, '\\.')}\\+[0-9a-f]{7}$`));
});

test('--version e --diagnose', async () => {
  const v = run(['--version']);
  expect(v.code).toBe(0);
  expect(v.out.trim()).toMatch(new RegExp(`^FanPilot ${version.replace(/\./g, '\\.')}\\+[0-9a-f]{7}$`));

  // Read-only, and with the panel's FanPilot closed, as the README asks.
  await stopServer();
  const before = stateFiles();
  const d = run(['--diagnose', '--no-browser']);
  expect(d.code, d.out).toBe(0);
  const reports = fs.readdirSync(path.join(dir, 'data')).filter((f) => /^diagnostico-.*\.txt$/.test(f));
  expect(reports).toHaveLength(1);
  expect(fs.readFileSync(path.join(dir, 'data', reports[0]), 'utf8')).toContain(`FanPilot ${version}+`);
  expect(stateFiles()).toEqual(before);
});
