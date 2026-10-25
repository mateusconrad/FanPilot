// Panel mode (item 22, wwwroot/panel.js): what the page takes from its URL and how its readings look. Pure functions, run
// from tests/js too.

// ?scale=1.2&theme=light (or the Portuguese ?escala=1.2&tema=claro) for screens set up once and left alone. The scale
// multiplies every size and stays between 0.5 and 2.5; the theme is dark unless asked otherwise ("auto": the device's).
export function panelOptions(search) {
  const q = new URLSearchParams(search || '');
  const rawScale = Number(String(q.get('scale') || q.get('escala') || '1').replace(',', '.'));
  const scale = Number.isFinite(rawScale) && rawScale > 0 ? Math.min(2.5, Math.max(0.5, rawScale)) : 1;
  const themes = { dark: 'dark', escuro: 'dark', light: 'light', claro: 'light', auto: 'auto', sistema: 'auto', system: 'auto' };
  const theme = themes[String(q.get('theme') || q.get('tema') || '').toLowerCase()] || 'dark';
  return { scale, theme };
}

// The same steps as the panel's cards (tempStatus in app.js): the colour of a temperature.
export function tempLevel(v) {
  if (v === null || v === undefined || Number.isNaN(v)) return '';
  return v < 60 ? 'good' : v < 75 ? 'warning' : v < 85 ? 'serious' : 'critical';
}

// The minigraph's vertical range: the values with some room, on round tens, never narrower than 30 °C.
export function chartRange(values) {
  const vals = values.filter((v) => v !== null && v !== undefined && !Number.isNaN(v));
  if (!vals.length) return [30, 60];
  let lo = Math.floor((Math.min(...vals) - 5) / 10) * 10, hi = Math.ceil((Math.max(...vals) + 5) / 10) * 10;
  if (hi - lo < 30) hi = lo + 30;
  return [lo, hi];
}

// An SVG path for one series in a w × h box, the window being the last span ms before now; gaps (null) break the line.
export function seriesPath(times, values, now, span, lo, hi, w, h) {
  let d = '', pen = false;
  for (let i = 0; i < times.length; i++) {
    const v = values[i];
    if (v === null || v === undefined || times[i] < now - span) { pen = false; continue; }
    const x = (1 - (now - times[i]) / span) * w, y = (1 - (v - lo) / (hi - lo)) * h;
    d += (pen ? 'L' : 'M') + x.toFixed(1) + ' ' + y.toFixed(1);
    pen = true;
  }
  return d;
}
