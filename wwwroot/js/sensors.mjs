// Sensor helpers shared by the panel (app.js) and the tests (tests/js).

// The CPU temperature the panel shows (item 19): the reading the backend marked "cpu-main" (the cores when there is a
// reading for them, otherwise the board's CPU sensor), or the one titled "CPU" from a backend that marks none.
export function cpuMain(sensors) {
  if (!sensors) return null;
  return sensors.find((s) => s.role === 'cpu-main')
    || sensors.find((s) => s.type === 'Temperature' && s.title === 'CPU')
    || null;
}
