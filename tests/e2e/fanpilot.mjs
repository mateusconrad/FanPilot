// The real FanPilot.exe (a Debug build, where --fake-board exists) on a fake board, from a scratch copy, on a free
// port. Without Administrator (a developer's PC) it serves localhost only; elevated (the CI) it listens on every
// address, as it does for phones, and the tests that play the phone run too.
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';

const repo = path.resolve(import.meta.dirname, '..', '..');
// FANPILOT_BUILD: another Debug output folder.
export const buildDir = process.env.FANPILOT_BUILD || path.join(repo, 'src', 'bin', 'Debug');
// Every FanPilot the tests start runs from here, so a leftover one can be told apart from a real FanPilot. The long
// form of the path: the CI's %TEMP% can be an 8.3 name, and process paths are not.
export const scratchRoot = path.join(fs.realpathSync.native(os.tmpdir()), 'fanpilot-e2e');

// Windows PowerShell, with values passed in environment variables instead of quoted inside the script. PSModulePath
// is left out: under pwsh 7 (the CI's shell) it points Windows PowerShell at modules it cannot load (Get-Acl failed so).
export function powershell(script, env = {}) {
  const childEnv = { ...process.env, ...env };
  for (const k of Object.keys(childEnv)) if (k.toLowerCase() === 'psmodulepath') delete childEnv[k];
  return execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8', env: childEnv }).trim();
}

export const isAdmin = powershell(
  '([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)') === 'True';

// A process ended by kill() has no exit code, only a signal (SIGTERM, even on Windows).
export const hasExited = (proc) => proc.exitCode !== null || proc.signalCode !== null;

export function freePort() {
  return new Promise((resolve, reject) => {
    const srv = net.createServer().once('error', reject);
    srv.listen(0, () => {
      const { port } = srv.address();
      srv.close(() => resolve(port));
    });
  });
}

// A FanPilot left running by a crashed run would hold the fake board's single-instance lock.
export function stopLeftovers() {
  powershell("Get-CimInstance Win32_Process -Filter \"Name='FanPilot.exe'\" | Where-Object { $_.ExecutablePath -like ($env:ROOT + '\\*') } | " +
    'ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }', { ROOT: scratchRoot });
}

export class FanPilot {
  // files: { '<name>': content } written to data-fake\ before the start (state files, a damaged one, history\...).
  // config: settings for config.json besides the port ({ historyDays: 0 }).
  static async start(scenario = 'demo', { files = {}, config = {} } = {}) {
    fs.mkdirSync(scratchRoot, { recursive: true });
    const fp = new FanPilot(fs.mkdtempSync(path.join(scratchRoot, scenario + '-')), config);
    fs.cpSync(buildDir, fp.dir, { recursive: true, filter: (src) => !/^[\\/]?data(-fake)?([\\/]|$)/.test(path.relative(buildDir, src)) });
    fs.mkdirSync(fp.dataDir);
    for (const [name, content] of Object.entries(files)) {
      const file = path.join(fp.dataDir, name);
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, content);
    }
    await fp.launch(scenario);
    return fp;
  }

  constructor(dir, config = {}) {
    this.dir = dir;
    this.config = config;
    this.output = '';
  }

  get url() { return `http://localhost:${this.port}`; }
  get dataDir() { return path.join(this.dir, 'data-fake'); }

  async launch(scenario) {
    for (let attempt = 1; ; attempt++) {
      if (!this.port) {
        this.port = await freePort();
        fs.writeFileSync(path.join(this.dataDir, 'config.json'), JSON.stringify({ ...this.config, port: this.port }));
      }
      // RunAsInvoker: without it Windows would ask for elevation (app.manifest). Elevated, nothing changes.
      this.proc = spawn(path.join(this.dir, 'FanPilot.exe'), ['--fake-board=' + scenario, '--no-browser'], {
        cwd: this.dir, env: { ...process.env, __COMPAT_LAYER: 'RunAsInvoker' }, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
      });
      this.proc.stdout.on('data', (d) => { this.output += d; });
      this.proc.stderr.on('data', (d) => { this.output += d; });
      const exit = new Promise((resolve) => this.proc.once('exit', (code) => resolve({ code })));
      const ready = await Promise.race([this.waitReady(), exit]);
      if (ready === true) return;
      const { code } = ready === false ? await exit : ready;
      // 5: the port was taken after freePort found it free. 4: a leftover FanPilot holds the lock.
      if (attempt < 3 && code === 5) { this.port = null; continue; }
      if (attempt < 3 && code === 4) { stopLeftovers(); continue; }
      throw new Error(`O FanPilot (${scenario}) não subiu: saída ${code}.\n${this.output}`);
    }
  }

  // Up when /api/info answers 200: until the board is open, every request gets 503.
  async waitReady(timeoutMs = 30000) {
    const until = Date.now() + timeoutMs;
    while (Date.now() < until) {
      if (hasExited(this.proc)) return false;
      try {
        if ((await fetch(this.url + '/api/info')).ok) return true;
      } catch { /* not listening yet */ }
      await sleep(150);
    }
    throw new Error(`O FanPilot não respondeu em ${timeoutMs / 1000} s.\n${this.output}`);
  }

  // Ends the process the way a crash or Task Manager would (the fake board has nothing to hand back).
  async kill() {
    if (!this.proc || hasExited(this.proc)) return;
    const exited = new Promise((resolve) => this.proc.once('exit', resolve));
    this.proc.kill();
    await exited;
  }

  // Same folder, port and data: what a phone sees when FanPilot is restarted on the PC.
  async restart(scenario) {
    await this.kill();
    await this.launch(scenario);
  }

  async stop() {
    await this.kill();
    fs.rmSync(this.dir, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  }

  readJson(name) { return JSON.parse(fs.readFileSync(path.join(this.dataDir, name), 'utf8')); }
}
