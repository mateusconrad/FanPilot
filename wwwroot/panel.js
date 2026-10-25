// Panel mode (item 22): a read-only, full-screen view of the readings, for a screen left on next to the monitor or
// inside the case. Live data over SSE like the panel (app.js); nothing on this page changes a fan.
import { t, num, loadLanguage, translatePage } from './js/i18n.mjs';
import { cpuMain } from './js/sensors.mjs';
import { panelOptions, tempLevel, chartRange, seriesPath } from './js/dash.mjs';

const $ = (id) => document.getElementById(id);
const HISTORY_MS = 10 * 60 * 1000;
const RECONNECT_MS = 3000;
const BAR_IDLE_MS = 5000;
const CHART = { w: 600, h: 200 }; // the SVG's viewBox
const history = { t: [], cpu: [], gpu: [] };
let fans = null; // /api/fans, for the headers' names

const fmt = (v, d = 0) => (v === null || v === undefined || Number.isNaN(v)) ? '–' : num(Number(v), d);
const sensorTitle = (s) => (s.titleKey ? t(s.titleKey, null, s.title) : s.title);
const find = (snap, type, title) => (snap.sensors || []).find((s) => s.type === type && s.title === title);

async function api(path) {
  const r = await fetch(path);
  if (r.status === 401) { location.reload(); throw new Error(t('err.unpaired')); }
  if (!r.ok) throw new Error(r.statusText);
  return r.json();
}

// ?scale= and ?theme= (dash.mjs): dark unless asked otherwise, as a screen left on in a dark room wants.
function applyOptions() {
  const { scale, theme } = panelOptions(location.search);
  document.body.style.setProperty('--s', scale);
  if (theme === 'auto') document.documentElement.removeAttribute('data-theme');
  else document.documentElement.setAttribute('data-theme', theme);
  if (theme === 'light') document.querySelector('meta[name=theme-color]').content = '#f9f9f7';
}

function renderTiles(snap) {
  const cpu = cpuMain(snap.sensors);
  const cpuT = cpu ? cpu.value : null;
  const pkg = find(snap, 'Power', 'CPU Package');
  const clk = find(snap, 'Frequency', 'CPU');
  $('cpuLabel').textContent = cpu ? sensorTitle(cpu) : 'CPU';
  $('cpuTemp').textContent = fmt(cpuT);
  $('cpuSub').textContent = [pkg ? fmt(pkg.value, 0) + ' W' : '', clk ? fmt(clk.value / 1000, 2) + ' GHz' : ''].filter(Boolean).join(' · ');
  $('cpuTile').className = 'dash-tile ' + tempLevel(cpuT);

  const gpu = snap.gpu || {};
  const gpuT = gpu.ok ? gpu.temp : null;
  $('gpuLabel').textContent = gpu.ok ? gpu.name || 'GPU' : t('tile.gpu.unavailable');
  $('gpuTemp').textContent = fmt(gpuT);
  $('gpuSub').textContent = gpu.ok
    ? [gpu.power !== undefined ? fmt(gpu.power, 0) + ' W' : '', gpu.util !== undefined ? t('tile.gpu.util', { n: gpu.util }) : ''].filter(Boolean).join(' · ')
    : '';
  $('gpuTile').className = 'dash-tile ' + tempLevel(gpuT);
}

// Every header with its RPM, by the name the user gave it.
function renderFans(snap) {
  const list = (fans && fans.fans) || [];
  const rpm = snap.fanRpm || [];
  $('dashFans').replaceChildren(...list.map((f, i) => {
    const item = document.createElement('span');
    item.className = 'dash-fan';
    const value = document.createElement('b');
    value.textContent = rpm[i] ? String(rpm[i]) : '–';
    item.append((f.label || f.title) + ' ', value, ' RPM');
    return item;
  }));
}

function push(snap) {
  const cpu = cpuMain(snap.sensors);
  history.t.push(snap.t);
  history.cpu.push(cpu ? cpu.value : null);
  history.gpu.push(snap.gpu && snap.gpu.ok && snap.gpu.temp !== undefined ? snap.gpu.temp : null);
  while (history.t.length && history.t[0] < snap.t - HISTORY_MS) {
    history.t.shift(); history.cpu.shift(); history.gpu.shift();
  }
}

// /api/history: the series by id, the CPU as "cpu-main" (item 19).
function seed(hist) {
  const n = hist.t.length;
  const take = (a) => { const out = (a || []).slice(0, n); while (out.length < n) out.push(null); return out; };
  history.t = hist.t.slice();
  history.cpu = take(hist.series['cpu-main']);
  history.gpu = take(hist.series['gpu-temp']);
}

// Up to the last 10 minutes; a FanPilot that just started has less, and the chart spreads what there is (at least a
// minute) instead of drawing a dot at the edge.
function renderChart() {
  const now = history.t.length ? history.t[history.t.length - 1] : Date.now();
  const span = Math.min(HISTORY_MS, Math.max(60_000, history.t.length ? now - history.t[0] : 0));
  const [lo, hi] = chartRange(history.cpu.concat(history.gpu));
  $('cpuLine').setAttribute('d', seriesPath(history.t, history.cpu, now, span, lo, hi, CHART.w, CHART.h));
  $('gpuLine').setAttribute('d', seriesPath(history.t, history.gpu, now, span, lo, hi, CHART.w, CHART.h));
  $('axisTop').textContent = hi + '°';
  $('axisBottom').textContent = lo + '°';
}

function setStatus(ok, text) {
  $('statusDot').className = 'dot ' + (ok ? 'ok' : 'bad');
  $('statusText').textContent = text;
  $('dashStatus').title = text;
}

function onSnapshot(snap) {
  if (!snap.t) return; // the server has not sampled yet
  const ok = !snap.service || snap.service.ok !== false;
  setStatus(ok, t(ok ? 'status.live' : 'status.boardDown'));
  renderTiles(snap);
  renderFans(snap);
  push(snap);
  renderChart();
}

// As the panel does: a closed stream (FanPilot restarting, or full) is opened again a little later.
function connect() {
  const es = new EventSource('/api/stream');
  let lastFrame = Date.now();
  es.onmessage = (m) => {
    lastFrame = Date.now();
    try { onSnapshot(JSON.parse(m.data)); } catch (e) { /* a broken frame: the next one comes in a second */ }
  };
  const stall = setInterval(() => {
    if (es.readyState === EventSource.OPEN && Date.now() - lastFrame > 5000) setStatus(false, t('status.stale'));
  }, 1000);
  es.onerror = async () => {
    setStatus(false, t('status.reconnecting'));
    if (es.readyState === EventSource.CLOSED) {
      clearInterval(stall);
      setTimeout(connect, RECONNECT_MS);
    }
    try { const r = await fetch('/api/state'); if (r.status === 401) location.reload(); } catch (e) { /* offline */ }
  };
}

async function loadFans() {
  try { fans = await api('/api/fans'); } catch (e) { /* the RPMs wait for the names until the next try */ }
}

// The options show after a touch, a click or a key, and fade away after a few seconds unless one has the focus.
function wireBar() {
  const bar = $('dashBar');
  let timer = 0;
  const show = () => {
    bar.classList.remove('idle');
    clearTimeout(timer);
    timer = setTimeout(() => { if (!bar.contains(document.activeElement)) bar.classList.add('idle'); }, BAR_IDLE_MS);
  };
  for (const type of ['pointerdown', 'pointermove', 'keydown']) document.addEventListener(type, show);
  bar.addEventListener('focusin', show);
  show();
}

// The Fullscreen API works over plain HTTP; the iPhone has none for pages, so there the button goes away.
function wireFullscreen() {
  const button = $('fullBtn');
  if (!document.fullscreenEnabled) { button.hidden = true; return; }
  const label = () => { button.textContent = t(document.fullscreenElement ? 'panel.exitFullscreen' : 'panel.fullscreen'); };
  button.onclick = async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
    } catch (e) { /* refused: the page stays as it is */ }
  };
  document.addEventListener('fullscreenchange', label);
  label();
}

// The screen stays on: the Wake Lock API where the browser allows it (HTTPS, or the PC itself), else a muted video
// playing in a loop, which keeps Android's screen on as any playing video does (NoSleep.js's technique). Both need a
// tap first.
let awake = false, wakeLock = null, video = null;
async function setAwake(on) {
  awake = on;
  $('awakeBtn').setAttribute('aria-pressed', on);
  if (!on) {
    if (wakeLock) { wakeLock.release().catch(() => {}); wakeLock = null; }
    if (video) video.pause();
    $('awakeHint').textContent = '';
    return;
  }
  if (navigator.wakeLock && window.isSecureContext) {
    try {
      wakeLock = await navigator.wakeLock.request('screen');
      $('awakeHint').textContent = t('panel.awake.on');
      return;
    } catch (e) { /* refused (battery saver, say): the video */ }
  }
  try {
    if (!video) {
      video = document.createElement('video');
      video.muted = true;
      video.loop = true;
      video.playsInline = true;
      video.className = 'dash-keepawake';
      video.src = '/media/keepawake.mp4';
      document.body.append(video);
    }
    await video.play();
    $('awakeHint').textContent = t('panel.awake.video');
  } catch (e) {
    awake = false;
    $('awakeBtn').setAttribute('aria-pressed', false);
    $('awakeHint').textContent = t('panel.awake.failed');
  }
}

// OLED screens: the whole page moves a few pixels every 3 minutes, so nothing stays lit on the same pixels for hours.
function oledGuard() {
  setInterval(() => {
    const shift = () => Math.round(Math.random() * 12 - 6);
    $('dash').style.transform = `translate(${shift()}px, ${shift()}px)`;
  }, 3 * 60 * 1000);
}

async function init() {
  applyOptions();
  await loadLanguage();
  translatePage();
  wireBar();
  wireFullscreen();
  $('awakeBtn').onclick = () => setAwake(!awake);
  // A wake lock ends when the page is hidden, and a hidden video may be paused: both come back with the page.
  document.addEventListener('visibilitychange', () => { if (awake && document.visibilityState === 'visible') setAwake(true); });
  oledGuard();
  await loadFans();
  try {
    const snap = await api('/api/state');
    seed(await api('/api/history'));
    onSnapshot(snap);
  } catch (e) { /* the stream fills in */ }
  connect();
  setInterval(loadFans, 30000); // a name given in the panel shows up here too
}

init();
