import fs from 'node:fs';
import { scratchRoot, stopLeftovers } from './fanpilot.mjs';

export default function globalTeardown() {
  stopLeftovers();
  fs.rmSync(scratchRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
