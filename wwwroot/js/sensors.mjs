// Sensor helpers shared by the panel (app.js) and the tests (tests/js).

// The CPU temperature the panel shows (item 19): the reading the backend marked "cpu-main" (the cores when there is a
// reading for them, otherwise the board's CPU sensor), or the one titled "CPU" from a backend that marks none.
export function cpuMain(sensors) {
  if (!sensors) return null;
  return sensors.find((s) => s.role === 'cpu-main')
    || sensors.find((s) => s.type === 'Temperature' && s.title === 'CPU')
    || null;
}

// The temperature a curve follows now (item 25), as SoftwareCurveEngine.SourceTemp computes it: its board sensor, the
// GPU, or the higher of the two that read. null when there is none (the backend then hands the fan to the BIOS).
export function referenceTemp(source, tempInput, snap) {
  const board = snap && snap.tempInputs ? snap.tempInputs[tempInput] : null;
  const gpu = snap && snap.gpu && snap.gpu.ok ? snap.gpu.temp : null;
  const reads = (v) => typeof v === 'number' && v >= -40 && v <= 125;
  if (source === 'gpu') return reads(gpu) ? gpu : null;
  if (source === 'max') {
    const both = [board, gpu].filter(reads);
    return both.length ? Math.max(...both) : null;
  }
  return board === undefined ? null : board;
}
