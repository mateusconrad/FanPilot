// The Windows service (item 14a): FanPilot.exe --service under the Service Control Manager, as LocalSystem, with the
// fake board, from a scratch copy. It installs a real service, so it only runs where that is expected: the CI
// (elevated), or with FANPILOT_SERVICE_TEST=1 on a PC as Administrator. The service is removed at the end whatever
// happens, and before starting in case a crashed run left it behind.
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { expect, test } from '@playwright/test';
import { buildDir, freePort, isAdmin, scratchRoot } from './fanpilot.mjs';

const NAME = 'FanPilotE2E';
const enabled = isAdmin && (!!process.env.CI || process.env.FANPILOT_SERVICE_TEST === '1');
test.skip(!enabled, 'instala um serviço do Windows: roda na CI ou com FANPILOT_SERVICE_TEST=1 como Administrador');
test.describe.configure({ mode: 'serial' });

// PowerShell with the values in environment variables: no quoting of paths inside the script.
function ps(script, env = {}) {
  return execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8', env: { ...process.env, ...env } }).trim();
}
const status = () => ps('(Get-Service -Name $env:N).Status', { N: NAME });
const servicePid = () => Number(ps('(Get-CimInstance Win32_Service -Filter "Name=\'$env:N\'").ProcessId', { N: NAME }));

function removeService() {
  try {
    ps('if (Get-Service -Name $env:N -ErrorAction SilentlyContinue) { Stop-Service -Name $env:N -Force -ErrorAction SilentlyContinue; sc.exe delete $env:N | Out-Null }', { N: NAME });
  } catch { /* nothing to remove */ }
}

let dir, dataDir, port;
const url = (p) => `http://localhost:${port}${p}`;

async function answers() {
  try { return (await fetch(url('/api/info'))).ok; } catch { return false; }
}

async function waitFor(condition, timeoutMs, what) {
  const until = Date.now() + timeoutMs;
  while (Date.now() < until) {
    if (await condition()) return;
    await sleep(250);
  }
  throw new Error(`${what} não aconteceu em ${timeoutMs / 1000} s.\nLog:\n${readLog()}`);
}

function readLog() {
  try { return fs.readFileSync(path.join(dataDir, 'fanpilot.log'), 'utf8'); } catch (e) { return '(sem log: ' + e.message + ')'; }
}

test.beforeAll(async () => {
  removeService();
  fs.mkdirSync(scratchRoot, { recursive: true });
  dir = fs.mkdtempSync(path.join(scratchRoot, 'service-'));
  fs.cpSync(buildDir, dir, { recursive: true, filter: (src) => !/^[\\/]?data(-fake)?([\\/]|$)/.test(path.relative(buildDir, src)) });
  dataDir = path.join(dir, 'servico-dados');
  fs.mkdirSync(dataDir);
  port = await freePort();
  fs.writeFileSync(path.join(dataDir, 'config.json'), JSON.stringify({ port }));
  const bin = `"${path.join(dir, 'FanPilot.exe')}" --service --data "${dataDir}" --fake-board`;
  ps('New-Service -Name $env:N -BinaryPathName $env:BIN -StartupType Manual -DisplayName "FanPilot (teste E2E)" | Out-Null', { N: NAME, BIN: bin });
  // What the installer (item 14c) sets: restart after 1 s, 5 s and 60 s.
  execFileSync('sc.exe', ['failure', NAME, 'reset=', '86400', 'actions=', 'restart/1000/restart/5000/restart/60000'], { stdio: 'ignore' });
});

test.afterAll(() => removeService());

test('o serviço sobe sem console, responde e guarda os dados numa pasta protegida', async () => {
  ps('Start-Service -Name $env:N', { N: NAME });
  await waitFor(answers, 30_000, 'O serviço responder');
  expect(status()).toBe('Running');
  expect((await fetch(url('/api/state'))).status).toBe(200);
  expect(readLog()).toContain('como serviço');
  expect(ps('(Get-Acl -Path $env:D).AreAccessRulesProtected', { D: dataDir })).toBe('True');
});

test('o FanPilot.exe aberto com o serviço ativo diz que já está rodando', () => {
  const r = spawnSync(path.join(dir, 'FanPilot.exe'), ['--fake-board', '--no-browser', '--data', dataDir], { encoding: 'utf8', timeout: 60_000 });
  expect(r.status).toBe(4);
  expect(r.stdout).toContain('já está rodando');
  expect(r.stdout).toContain(`http://localhost:${port}/`);
});

test('o Windows reinicia o serviço se o processo morrer', async () => {
  const before = servicePid();
  expect(before).toBeGreaterThan(0);
  ps('Stop-Process -Id $env:P -Force', { P: String(before) });
  await waitFor(async () => {
    const now = servicePid();
    return now > 0 && now !== before && await answers();
  }, 45_000, 'O Windows reiniciar o serviço');
  expect(status()).toBe('Running');
  expect(readLog()).toContain('O FanPilot anterior terminou sem encerrar normalmente');
});

test('parar o serviço encerra limpo e rápido', async () => {
  ps('Stop-Service -Name $env:N', { N: NAME });
  expect(status()).toBe('Stopped');
  expect(await answers()).toBe(false);
  const stops = [...readLog().matchAll(/Encerrado em (\d+) ms\./g)].map((m) => Number(m[1]));
  expect(stops.length).toBeGreaterThan(0);
  expect(stops[stops.length - 1]).toBeLessThan(1000);
});
