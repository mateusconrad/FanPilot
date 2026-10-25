import fs from 'node:fs';
import path from 'node:path';
import { buildDir, isAdmin, scratchRoot, stopLeftovers } from './fanpilot.mjs';

export default function globalSetup() {
  if (!fs.existsSync(path.join(buildDir, 'FanPilot.exe'))) {
    throw new Error(`Falta o FanPilot.exe de Debug em ${buildDir}: rode "dotnet build src/FanPilot.csproj -c Debug" na raiz do repositório.`);
  }
  // Elevated, the tests that play a phone on the network run; the CI must not skip them silently.
  if (process.env.CI && !isAdmin) throw new Error('Na CI, os testes de ponta a ponta precisam rodar como Administrador.');
  stopLeftovers();
  fs.rmSync(scratchRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  fs.rmSync(path.join(import.meta.dirname, 'screens'), { recursive: true, force: true });
}
