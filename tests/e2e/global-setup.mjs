import fs from 'node:fs';
import path from 'node:path';
import { buildDir, isAdmin, program, scratchRoot, stopLeftovers, windows } from './fanpilot.mjs';

export default function globalSetup() {
  if (!fs.existsSync(path.join(buildDir, program))) {
    throw new Error(`Falta o ${program} de Debug em ${buildDir}: rode "dotnet build src/FanPilot.csproj -c Debug" na raiz do repositório.`);
  }
  // Elevated, the tests that play a phone on the network run; the Windows CI must not skip them silently. On Linux
  // they run as a plain user, as the runner is, and the phone tests stay with Windows.
  if (process.env.CI && windows && !isAdmin) throw new Error('Na CI, os testes de ponta a ponta precisam rodar como Administrador.');
  stopLeftovers();
  fs.rmSync(scratchRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  fs.rmSync(path.join(import.meta.dirname, 'screens'), { recursive: true, force: true });
}
