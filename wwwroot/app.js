// FanPilot dashboard: live data over SSE (/api/stream), fan curve editor, GPU fan control.
import { curveAt, resample, normalize, movePoint } from './js/curve.mjs';
import { cpuMain } from './js/sensors.mjs';
// tr, not t: t is a temperature or a time all over this file.
import { t as tr, msg, num, time, language, loadLanguage, translatePage, wireLanguageSelect } from './js/i18n.mjs';

(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const SVGNS = 'http://www.w3.org/2000/svg';
  const HISTORY_MS = 10 * 60 * 1000;

  const state = {
    snap: null,
    history: { t: [], series: {} },
    fans: null,          // /api/fans payload
    editor: null,        // open editor state
    gpuDirty: false,
    alerts: [],          // the active alerts of the last snapshot
  };

  // ---------- helpers ----------
  async function api(path, body) {
    const opts = body === undefined ? {} : {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-FanPilot': '1' }, body: JSON.stringify(body)
    };
    const r = await fetch(path, opts);
    if (r.status === 401) { location.reload(); throw new Error(tr('err.unpaired')); }
    const data = await r.json().catch(() => ({}));
    // FanPilot's errors come with a code to translate; the English text is the fallback.
    if (!r.ok) throw new Error(data.code ? tr(data.code, data.params, data.error) : data.error || r.statusText);
    return data;
  }

  // Enter animations come from CSS @starting-style; exits play the .leaving transition, then hide.
  // Showing again mid-exit cancels the pending hide.
  const exits = new WeakMap();
  function show(node) {
    clearTimeout(exits.get(node));
    node.classList.remove('leaving');
    node.hidden = false;
  }
  function hide(node, ms) {
    if (node.hidden || node.classList.contains('leaving')) return;
    node.classList.add('leaving');
    exits.set(node, setTimeout(() => { node.hidden = true; node.classList.remove('leaving'); }, ms));
  }

  let toastTimer = 0;
  function toast(msg, isError) {
    const t = $('toast');
    t.textContent = msg;
    t.classList.toggle('error', !!isError);
    show(t);
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => hide(t, 150), isError ? 5000 : 2200);
  }

  function el(tag, attrs, parent) {
    const e = document.createElementNS(SVGNS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }

  // Names and messages come from the hardware service and the driver: always escape before innerHTML.
  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  // 8 colors in style.css (--fan-0..7); boards with more headers reuse them.
  const fanColor = (i) => `var(--fan-${i % 8})`;

  const fmt = (v, d = 0) => (v === null || v === undefined || Number.isNaN(v)) ? '–' : num(Number(v), d);
  const pct = (pwm) => Math.round(pwm / 2.55);

  function tempStatus(v) {
    if (v === null || v === undefined) return { cls: '', label: tr('temp.none') };
    if (v < 60) return { cls: 'good', label: tr('temp.normal') };
    if (v < 75) return { cls: 'warning', label: tr('temp.warm') };
    if (v < 85) return { cls: 'serious', label: tr('temp.hot') };
    return { cls: 'critical', label: tr('temp.critical') };
  }

  function sensor(snap, type, title) {
    if (!snap || !snap.sensors) return null;
    return snap.sensors.find((s) => s.type === type && s.title === title) || null;
  }
  const cpuTempOf = (snap) => { const s = cpuMain(snap && snap.sensors); return s ? s.value : null; };

  // ---------- tiles ----------
  function renderTiles(snap) {
    const gpu = snap.gpu || {};
    const cpu = cpuMain(snap.sensors);
    const cpuT = cpu ? cpu.value : null;
    const pkg = sensor(snap, 'Power', 'CPU Package');
    const clk = sensor(snap, 'Frequency', 'CPU');
    const tiles = [
      // The label says which reading it is ("CPU · pacote", "CPU · socket"); the hint, how to get the cores' one.
      { label: esc(cpu ? sensorTitle(cpu) : 'CPU'), value: fmt(cpuT), unit: '°C', status: tempStatus(cpuT), meta: clk ? fmt(clk.value / 1000, 2) + ' GHz' : '', hint: cpu && sensorHint(cpu) },
      { label: gpu.name ? 'GPU' : tr('tile.gpu.unavailable'), value: fmt(gpu.temp), unit: '°C', status: tempStatus(gpu.temp), meta: gpu.util !== undefined ? tr('tile.gpu.util', { n: gpu.util }) : '' },
      { label: tr('tile.cpu.power'), value: fmt(pkg && pkg.value, 0), unit: 'W', meta: tr('tile.cpu.package') },
      { label: tr('tile.gpu.power'), value: fmt(gpu.power, 0), unit: 'W', meta: gpu.powerLimit ? tr('tile.gpu.limit', { n: gpu.powerLimit }) : '' },
    ];
    $('tiles').innerHTML = tiles.map((tile) =>
      `<div class="tile"><div class="label">${tile.label}</div>` +
      `<div class="value">${tile.value}<small>${tile.unit}</small></div>` +
      `<div class="meta">${tile.status ? `<span class="badge ${tile.status.cls}">${tile.status.label}</span>` : ''}${tile.meta ? '<span>' + esc(tile.meta) + '</span>' : ''}</div>` +
      (tile.hint ? `<div class="tile-hint">${esc(tile.hint)}</div>` : '') + '</div>'
    ).join('');
  }

  // FanPilot names some readings itself ("CPU · pacote"): those come with a key to translate; the hardware's own
  // names (LibreHardwareMonitor, the GCC) are shown as they are.
  const sensorTitle = (s) => (s.titleKey ? tr(s.titleKey, null, s.title) : s.title);
  const sensorHint = (s) => (s.hintKey ? tr(s.hintKey, null, s.hint) : s.hint);

  // ---------- time-series charts (temperatures, fan RPM) ----------
  // state.history = { t: [ms...], series: { cpu, gpu, 'fan-0'.. : values aligned with t } }
  function pushHistory(snap) {
    const h = state.history;
    h.t.push(snap.t);
    const vals = { cpu: cpuTempOf(snap), gpu: snap.gpu && snap.gpu.temp !== undefined ? snap.gpu.temp : null };
    (snap.fanRpm || []).forEach((v, i) => { vals['fan-' + i] = v; });
    const n = h.t.length;
    Object.keys(vals).forEach((k) => { if (!h.series[k]) h.series[k] = new Array(n - 1).fill(null); });
    Object.keys(h.series).forEach((k) => h.series[k].push(k in vals ? vals[k] : null));
    while (h.t.length && h.t[0] < snap.t - HISTORY_MS) {
      h.t.shift();
      Object.keys(h.series).forEach((k) => h.series[k].shift());
    }
  }

  function seedHistory(hist) {
    const cpuId = state.snap && (cpuMain(state.snap.sensors) || {}).id;
    const n = hist.t.length;
    const take = (src) => { const a = (src || []).slice(0, n); while (a.length < n) a.push(null); return a; };
    const series = {
      cpu: take(hist.series['cpu-main'] || (cpuId && hist.series[cpuId]) || hist.series['temperature-514']),
      gpu: take(hist.series['gpu-temp']),
    };
    Object.keys(hist.series).filter((k) => k.startsWith('fan-')).forEach((k) => { series[k] = take(hist.series[k]); });
    state.history = { t: hist.t.slice(), series };
  }

  // Direct chart labels only (legend and tooltip keep the full name): "System 1" / "SYS_FAN1" -> "Sys 1",
  // "SYS_FAN5_PUMP" -> "Sys 5 pump", "CPU_FAN" -> "CPU".
  const shortFanName = (title) => title.replace(/^(System|SYS_FAN)\s*/i, 'Sys ').replace(/^CPU_FAN$/i, 'CPU')
    .replace(/_PUMP$/i, ' pump').replace(/_/g, ' ');
  // The name the user gave the header (item 17), or the chip's.
  const fanName = (f) => f.label || f.title;
  const fmtRpm = (v) => v >= 1000 ? fmt(v / 1000, 1) + 'k' : String(Math.round(v));

  // Fans whose tachometer reads 0 for the whole window have nothing to plot.
  function spinningFans() {
    if (!state.fans) return [];
    const h = state.history;
    return state.fans.fans.map((f, i) => ({ f, i })).filter(({ i }) => (h.series['fan-' + i] || []).some((v) => v > 0));
  }

  const CHARTS = {
    temp: {
      svg: 'chart', wrap: 'chartWrap', tip: 'tip', unit: '°C',
      tick: (v) => v + '°', endText: (s, v) => `${s.label} ${Math.round(v)}°`,
      domain: (vals) => {
        let lo = Math.min(...vals, 30), hi = Math.max(...vals, 60);
        return [Math.floor((lo - 5) / 10) * 10, Math.ceil((hi + 5) / 10) * 10];
      },
      series: () => [
        { key: 'cpu', label: 'CPU', color: 'var(--series-cpu)' },
        { key: 'gpu', label: 'GPU', color: 'var(--series-gpu)' },
      ],
    },
    rpm: {
      svg: 'rpmChart', wrap: 'rpmWrap', tip: 'rpmTip', unit: 'RPM',
      tick: fmtRpm, endText: (s, v) => `${s.label} ${Math.round(v)}`,
      domain: (vals) => [0, niceCeil(Math.max(...vals, 1000) * 1.1)],
      series: () => spinningFans().map(({ f, i }) => ({ key: 'fan-' + i, label: f.label || shortFanName(f.title), name: fanName(f), color: fanColor(i) })),
    },
  };

  function niceStep(span, ticks) {
    const raw = span / ticks;
    const mag = Math.pow(10, Math.floor(Math.log10(raw)));
    const n = raw / mag;
    return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * mag;
  }
  function niceCeil(v) { const s = niceStep(v, 4); return Math.ceil(v / s) * s; }

  function renderLineChart(def) {
    const svg = $(def.svg);
    const h = state.history;
    svg.innerHTML = '';
    def.geo = null;
    const series = def.series().filter((s) => h.series[s.key]);
    const empty = !h.t.length || !series.length;
    svg.toggleAttribute('hidden', empty); // SVG elements have no .hidden property
    if (empty) return series;
    const lastOf = (key) => { const a = h.series[key]; for (let i = a.length - 1; i >= 0; i--) if (a[i] !== null) return a[i]; return null; };
    const labels = series.map((s) => { const v = lastOf(s.key); return v === null ? '' : def.endText(s, v); });
    // Draw in CSS pixels so axis text keeps its real size on narrow phones.
    const w = Math.max(300, Math.round($(def.wrap).clientWidth));
    // Right margin fits the longest direct label (11px text, ~6.2px per character), up to a third of the chart.
    const r = Math.min(Math.round(w * 0.38), Math.max(40, Math.ceil(Math.max(...labels.map((t) => t.length)) * 6.2) + 14));
    const g = { w, l: 38, r, t: 10, b: 24 };
    g.h = g.w < 480 ? 190 : 220;
    svg.setAttribute('viewBox', `0 0 ${g.w} ${g.h}`);
    const now = h.t[h.t.length - 1];
    g.t0 = now - HISTORY_MS;
    const vals = [];
    series.forEach((s) => h.series[s.key].forEach((v) => { if (v !== null) vals.push(v); }));
    const [lo, hi] = def.domain(vals);
    g.X = (t) => g.l + (t - g.t0) / HISTORY_MS * (g.w - g.l - g.r);
    g.Y = (v) => g.t + (1 - (v - lo) / (hi - lo)) * (g.h - g.t - g.b);

    const grid = el('g', { class: 'grid' }, svg);
    const axis = el('g', { class: 'axis' }, svg);
    const step = niceStep(hi - lo, 4);
    for (let v = lo; v <= hi + 1e-9; v += step) {
      el('line', { x1: g.l, x2: g.w - g.r, y1: g.Y(v), y2: g.Y(v) }, grid);
      el('text', { x: g.l - 6, y: g.Y(v) + 4, 'text-anchor': 'end' }, axis).textContent = def.tick(v);
    }
    // A label every 2 minutes, or every 5 when that would crowd the axis.
    const stepMin = g.X(now) - g.X(now - 2 * 60000) < 44 ? 5 : 2;
    for (let m = 10; m >= 0; m -= stepMin) {
      if (m > 0 && g.X(now) - g.X(now - m * 60000) < 46) continue; // would collide with "now"
      const tx = el('text', { x: g.X(now - m * 60000), y: g.h - 6, 'text-anchor': m === 0 ? 'end' : 'middle' }, axis);
      tx.textContent = m === 0 ? tr('chart.now') : tr('chart.minutesAgo', { n: m });
    }
    el('line', { class: 'baseline', x1: g.l, x2: g.w - g.r, y1: g.h - g.b, y2: g.h - g.b }, svg);

    const ends = [];
    series.forEach((s) => {
      let d = '', pen = false, lastX = 0, lastY = 0, lastV = null;
      h.series[s.key].forEach((v, i) => {
        if (v === null || h.t[i] < g.t0) { pen = false; return; }
        const x = g.X(h.t[i]), y = g.Y(v);
        d += (pen ? 'L' : 'M') + x.toFixed(1) + ' ' + y.toFixed(1);
        pen = true; lastX = x; lastY = y; lastV = v;
      });
      if (!d) return;
      el('path', { class: 'series', d, stroke: s.color }, svg);
      el('circle', { cx: lastX, cy: lastY, r: 4, fill: s.color, stroke: 'var(--surface)', 'stroke-width': 2 }, svg);
      ends.push({ x: lastX + 7, y: lastY + 4, text: def.endText(s, lastV) });
    });
    // Direct labels: keep at least 13px apart, inside the plot height.
    ends.sort((a, b) => a.y - b.y);
    for (let i = 1; i < ends.length; i++) ends[i].y = Math.max(ends[i].y, ends[i - 1].y + 13);
    const overflow = ends.length ? ends[ends.length - 1].y - (g.h - g.b) : 0;
    if (overflow > 0) ends.forEach((e) => { e.y -= overflow; });
    ends.forEach((e) => { el('text', { class: 'end-label', x: e.x, y: e.y }, svg).textContent = e.text; });
    g.cross = el('line', { class: 'crosshair', y1: g.t, y2: g.h - g.b, visibility: 'hidden' }, svg);
    g.series = series;
    def.geo = g;
    return series;
  }

  function renderCharts() {
    renderLineChart(CHARTS.temp);
    const fans = renderLineChart(CHARTS.rpm);
    $('rpmLegend').innerHTML = fans.map((s) => `<span><i style="background:${s.color}"></i>${esc(s.name)}</span>`).join('');
    if (!fans.length) {
      $('rpmNote').textContent = state.fans && state.fans.supported === false
        ? (msg(state.fans.notice) || tr('chart.rpm.noFans'))
        : tr('chart.rpm.none');
    } else if (state.fans) {
      const silent = state.fans.fans.filter((f, i) => !fans.some((s) => s.key === 'fan-' + i)).map(fanName);
      $('rpmNote').textContent = silent.length ? tr('chart.rpm.silent', { fans: silent.join(', ') }) : '';
    }
  }

  function chartHover(def, ev) {
    const g = def.geo;
    const h = state.history;
    if (!g || !h.t.length) return;
    const svg = $(def.svg);
    const box = svg.getBoundingClientRect();
    const sx = (ev.clientX - box.left) / box.width * g.w;
    const t = g.t0 + (sx - g.l) / (g.w - g.l - g.r) * HISTORY_MS;
    let best = 0;
    for (let i = 0; i < h.t.length; i++) if (Math.abs(h.t[i] - t) < Math.abs(h.t[best] - t)) best = i;
    const x = g.X(h.t[best]);
    g.cross.setAttribute('x1', x); g.cross.setAttribute('x2', x); g.cross.setAttribute('visibility', 'visible');
    const tip = $(def.tip);
    tip.innerHTML = `<div>${time(h.t[best])}</div>` + g.series.map((s) => {
      const v = h.series[s.key][best];
      return `<div class="row"><i style="background:${s.color}"></i>${esc(s.name || s.label)} <b>${v === null ? '–' : (def.unit === 'RPM' ? Math.round(v) : fmt(v))} ${def.unit}</b></div>`;
    }).join('');
    tip.style.display = 'block';
    const px = x / g.w * box.width;
    tip.style.left = Math.min(Math.max(px - tip.offsetWidth / 2, 0), box.width - tip.offsetWidth) + 'px';
    tip.style.top = '0px';
  }
  function chartLeave(def) {
    $(def.tip).style.display = 'none';
    if (def.geo) def.geo.cross.setAttribute('visibility', 'hidden');
  }

  // ---------- fans ----------
  // Header left to the BIOS on boards where FanPilot runs the curves (LibreHardwareMonitor); it has no points.
  const BIOS = 'BiosDefault';
  // false on those boards: closing FanPilot hands every fan back to the BIOS, and the panel says so.
  const hardwareCurves = () => !state.fans || state.fans.hardwareCurves !== false;
  // How a curve FanPilot runs follows the temperature (SoftwareCurveEngine.Responses); a chip curve has none.
  // Name and description of each response.
  const RESPONSES = {
    fast: ['response.fast', 'response.fast.desc'],
    balanced: ['response.balanced', 'response.balanced.desc'],
    smooth: ['response.smooth', 'response.smooth.desc'],
  };
  // The presets by key (FanPresets.cs, which also sends a name of its own, used for a key the panel doesn't know).
  const PRESETS = { quiet: 'preset.quiet', standard: 'preset.standard', performance: 'preset.performance', max: 'preset.max' };
  function presetName(key) {
    const p = (state.fans.presets || {})[key];
    const fallback = p ? p.name : key;
    return PRESETS[key] ? tr(PRESETS[key], null, fallback) : fallback;
  }

  function fanDescription(f, snap) {
    const req = f.request;
    let mode;
    if (state.fans.turbo) mode = tr('fan.turbo');
    else if (f.managed && f.role === 'pump' && f.pumpPolicy === 'full') mode = tr('fan.pumpFull');
    else if (f.managed && req && req.profile) mode = tr('fan.profile', { name: presetName(req.profile) });
    else if (f.managed && req && req.mode === 'fixed') mode = tr('fan.fixed', { n: Math.max(req.percent, f.role === 'pump' ? f.pumpMinPercent : 0) });
    else if (f.managed && !hardwareCurves()) mode = tr('fan.customResponse', { response: tr(RESPONSES[(req && req.response) || 'balanced'][0]).toLocaleLowerCase() });
    else if (f.managed) mode = tr('fan.custom');
    else if (f.mode === BIOS) mode = tr('fan.bios');
    else if (f.points.every((p) => p[1] >= 255)) mode = tr('fan.gccMax');
    else mode = tr('fan.original');
    const temps = snap && snap.tempInputs;
    const srcT = temps ? temps[f.tempInput] : null;
    const srcName = state.fans.tempInputs[f.tempInput] || '?';
    const now = srcT !== null && srcT !== undefined ? curveAt(f.points, f.mode, srcT) : null;
    return `${mode} · ${srcName} ${fmt(srcT)} °C` + (now !== null ? ` → ${pct(now)}%` : '');
  }

  function renderFans() {
    const box = $('fans');
    if (!state.fans) return;
    $('turboBtn').hidden = !state.fans.fans.length;
    $('fanSub').hidden = !state.fans.fans.length;
    renderProfiles();
    $('fanMode').hidden = hardwareCurves() || !state.fans.fans.length;
    if (!state.fans.supported) {
      box.innerHTML = '<p class="hint"></p>';
      box.firstChild.textContent = (msg(state.fans.notice) ||
        tr('fans.unsupported', { board: state.fans.board || tr('fans.unknownBoard') })) + ' ' + tr('fans.monitoringOnly');
      return;
    }
    if (!state.fans.fans.length) { box.innerHTML = '<p class="hint"></p>'; box.firstChild.textContent = tr('fans.unavailable'); return; }
    const snap = state.snap || {};
    if (box.querySelectorAll('.fan').length !== state.fans.fans.length) {
      box.innerHTML = '';
      state.fans.fans.forEach((f, i) => {
        const b = document.createElement('button');
        b.className = 'fan';
        b.innerHTML = '<div class="name"></div><div class="rpm"></div>' +
          `<div class="duty" role="meter" aria-valuemin="0" aria-valuemax="100"><i style="background:${fanColor(i)}"></i></div>` +
          '<div class="desc"></div>';
        b.onclick = () => openEditor(i);
        box.appendChild(b);
      });
    }
    const turbo = !!state.fans.turbo;
    $('turboBtn').setAttribute('aria-pressed', turbo);
    $('turboBtn').textContent = tr(turbo ? 'turbo.on' : 'turbo.off');
    $('turboBanner').hidden = !turbo;
    state.fans.fans.forEach((f, i) => {
      const b = box.children[i];
      const rpm = snap.fanRpm ? snap.fanRpm[i] : null;
      const duty = commandedPercent(f, snap);
      b.querySelector('.name').innerHTML = esc(fanName(f)) + (f.label ? `<span class="orig">${esc(f.title)}</span>` : '') +
        (turbo ? `<span class="pill">${esc(tr('fan.pill.turbo'))}</span>` : f.managed ? '<span class="pill">FanPilot</span>' : '') +
        (f.role === 'pump' ? `<span class="pill pump">${esc(tr('fan.pill.pump'))}</span>` : '');
      b.querySelector('.rpm').innerHTML = rpm ? `${rpm}<small> RPM</small>` : `<small>${esc(tr('fan.noReading'))}</small>`;
      const meter = b.querySelector('.duty');
      meter.firstChild.style.transform = `scaleX(${(duty === null ? 0 : duty) / 100})`;
      meter.setAttribute('aria-valuenow', duty === null ? 0 : duty);
      meter.title = tr('fan.commanded', { value: duty === null ? '–' : duty + '%' });
      b.querySelector('.desc').textContent = fanDescription(f, snap);
    });
  }

  // Duty cycle (0..100%) the chip is commanding right now, from the curve and its reference sensor.
  function commandedPercent(f, snap) {
    const srcT = snap && snap.tempInputs ? snap.tempInputs[f.tempInput] : null;
    if (srcT === null || srcT === undefined) return null;
    const pwm = curveAt(f.points, f.mode, srcT);
    return pwm === null ? null : pct(pwm);
  }

  // Configurations saved for a header this board no longer has (e.g. after a BIOS update): never applied, discardable.
  // Rebuilt only when /api/fans is loaded, not every second, so the buttons keep their state.
  function renderOrphans() {
    const list = (state.fans && state.fans.orphans) || [];
    $('orphans').replaceChildren(...list.map((o) => {
      const row = document.createElement('div');
      row.className = 'notice-banner orphan';
      const text = document.createElement('span');
      text.textContent = tr('orphan.text', { name: o.title || o.key });
      const discard = document.createElement('button');
      discard.className = 'btn';
      discard.textContent = tr('orphan.discard');
      discard.onclick = async () => {
        discard.disabled = true;
        try { await api('/api/fans/orphans', { key: o.key }); toast(tr('orphan.discarded')); await loadFans(); }
        catch (e) { toast(e.message, true); discard.disabled = false; }
      };
      row.append(text, discard);
      return row;
    }));
  }

  async function loadFans() {
    try { state.fans = await api('/api/fans'); renderFans(); renderOrphans(); renderCharts(); }
    catch (e) {
      $('fans').innerHTML = `<p class="hint">${esc(tr('fans.error', { error: e.message }))}</p>`;
      $('turboBtn').hidden = true;
      $('fanSub').hidden = true;
    }
  }

  // ---------- global profiles (item 18) ----------
  const profileName = (mode) => mode && mode !== 'custom' ? presetName(mode.slice('profile:'.length)) : null;

  function renderProfiles() {
    const seg = $('profileSeg');
    seg.hidden = !state.fans.fans.length;
    seg.querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.mode === (state.fans.mode || 'custom')));
  }

  async function setProfile(mode) {
    if (!state.fans || mode === state.fans.mode) return;
    const buttons = $('profileSeg').querySelectorAll('button');
    buttons.forEach((b) => { b.disabled = true; });
    try {
      const r = await api('/api/fans/mode', { mode });
      const name = profileName(mode);
      toast(r.turbo ? tr('profiles.afterTurbo') : name ? tr('profiles.applied', { name }) : tr('profiles.customBack'));
      await loadFans();
    } catch (e) { toast(e.message, true); }
    finally { buttons.forEach((b) => { b.disabled = false; }); }
  }

  async function toggleTurbo() {
    if (!state.fans) return;
    const on = !state.fans.turbo;
    $('turboBtn').disabled = true;
    try {
      await api('/api/fans/turbo', { on });
      toast(tr(on ? 'turbo.toast.on' : 'turbo.toast.off'));
      await loadFans();
    } catch (e) { toast(e.message, true); }
    finally { $('turboBtn').disabled = false; }
  }

  // ---------- fan editor ----------
  // The presets come from the backend (/api/fans "presets", FanPresets.cs): the profiles use the same ones.
  // { quiet: { name: 'Silencioso', points: [[°C, %], …] }, standard, performance, max }
  const presetPoints = (key, limits, n) =>
    normalize(resample(state.fans.presets[key].points.map(([t, p]) => [Math.min(t, limits.maxFullSpeedTemp), p]), n), limits);
  // t: 28 leaves room above the 100% line for a whole touch target.
  const ED = { w: 340, h: 248, l: 34, r: 12, t: 28, b: 26 };
  const exX = (t) => ED.l + t / 100 * (ED.w - ED.l - ED.r);
  const exY = (p) => ED.t + (1 - p / 100) * (ED.h - ED.t - ED.b);
  // A point's touch target: 27 units are 45 px across on a 320 px phone, where the SVG is 286 px wide.
  const HIT_R = 27;

  // Editor works in [temp, percent] with the fan's own point count; hardware readbacks may differ, so resample.
  function toEditorPoints(points, limits, n) {
    // A fan on the BIOS has no curve to start from: open the editor on the "Padrão" preset.
    if (!points.length) return presetPoints('standard', limits, n);
    let pts = points.map(([t, pwm]) => [Math.round(t), pct(pwm)]);
    if (pts.length !== n) {
      const temps = [0, 30, 40, 50, 60, 70, 75, Math.min(85, limits.maxFullSpeedTemp)];
      pts = resample(temps.map((t) => [t, pct(curveAt(points, 'SmartFanMode', t))]), n);
    }
    pts[0][0] = 0;
    pts[pts.length - 1][1] = 100;
    return normalize(pts, limits);
  }

  function openEditor(i) {
    if (state.fans.turbo) { toast(tr('ed.turboOn'), true); return; }
    const f = state.fans.fans[i];
    const limits = state.fans.limits;
    const req = f.managed ? f.request : null;
    state.editor = {
      index: i,
      mode: req && req.mode === 'fixed' ? 'fixed' : 'curve',
      percent: req && req.mode === 'fixed' ? req.percent : 50,
      style: f.mode === 'StairStepMode' ? 'StairStepMode' : 'SmartFanMode',
      tempInput: f.tempInput,
      pointCount: f.pointCount,
      points: toEditorPoints(f.points, limits, f.pointCount),
      response: (req && RESPONSES[req.response] && req.response) || 'balanced',
      selected: null,    // the point the fine-tune bar adjusts
    };
    renderEditorTitle(f);
    renderPresetChips();
    const profile = profileName(state.fans.mode);
    $('edProfileNote').hidden = !profile;
    $('edProfileNote').textContent = profile ? tr('ed.profileNote', { profile }) : '';
    $('edMeta').open = false;
    $('edLabel').value = f.label || '';
    $('edLabel').placeholder = f.title;
    $('edRole').value = f.role || 'fan';
    $('edPumpPolicy').value = f.pumpPolicy || 'full';
    $('edPumpMin').value = f.pumpMinPercent || 70;
    renderMeta();
    const sel = $('edTempInput');
    sel.innerHTML = '';
    state.fans.tempInputs.forEach((name, k) => {
      const o = document.createElement('option');
      o.value = k; o.textContent = name; o.selected = k === f.tempInput;
      sel.appendChild(o);
    });
    $('edStyle').value = state.editor.style;
    $('edFixedSlider').min = limits.minDutyPercent;
    $('edFixedSlider').value = state.editor.percent;
    const hw = hardwareCurves();
    $('edFixedHint').textContent = tr(hw ? 'ed.fixedHint.chip' : 'ed.fixedHint.app', { temp: limits.fixedModeSafetyTemp });
    const socket = hw && state.snap && (state.snap.sensors || []).some((s) => s.role === 'cpu-socket');
    $('edLimits').textContent = [tr('ed.limits', { min: limits.minDutyPercent, max: limits.maxFullSpeedTemp }),
      tr(hw ? 'ed.limits.chip' : 'ed.limits.app'), socket ? tr('ed.limits.socket') : ''].filter(Boolean).join(' ');
    $('edResponseBox').hidden = hw;
    $('edResponse').value = state.editor.response;
    $('edRestore').textContent = tr(hw ? 'ed.restore.chip' : 'ed.restore.bios');
    $('edRestore').disabled = !f.hasBackup || (!hw && !f.managed);
    state.editor.savedCurve = curveDraft(state.editor);
    state.editor.savedMeta = metaDraft();
    $('edDiscard').hidden = true;
    $('edTuneLive').textContent = '';
    show($('sheet'));
    show($('backdrop'));
    document.body.classList.add('sheet-open');
    setBackgroundInert(true);
    renderEditor();
    $('sheet').scrollTop = 0;
    $('edClose').focus({ preventScroll: true });
  }

  function renderEditorTitle(f) {
    $('edTitle').textContent = tr(f.role === 'pump' ? 'ed.title.pump' : 'ed.title.fan', { name: fanName(f) });
    $('edPumpNote').hidden = !(f.role === 'pump' && f.pumpPolicy === 'full');
  }

  // What "Aplicar" and "Salvar nome e função" would save: closing the sheet with a change asks first.
  const curveDraft = (e) => JSON.stringify([e.mode, e.percent, e.style, e.tempInput, e.points, e.response]);
  const metaDraft = () => JSON.stringify([$('edLabel').value.trim(), $('edRole').value, $('edPumpPolicy').value, $('edPumpMin').value]);

  // While the sheet is open, Tab and screen readers can't reach the page behind it.
  const setBackgroundInert = (on) => document.querySelectorAll('body > header, body > main').forEach((n) => { n.inert = on; });

  function closeEditor() {
    if (!state.editor) return;
    const index = state.editor.index;
    state.editor = null;
    hide($('sheet'), 200);
    hide($('backdrop'), 200);
    document.body.classList.remove('sheet-open');
    setBackgroundInert(false);
    // Focus goes back to the fan the sheet was opened from.
    const card = $('fans').querySelectorAll('.fan')[index];
    if (card) card.focus({ preventScroll: true });
  }

  // "Fechar", Esc and the backdrop: with something not saved, the sheet asks before throwing it away.
  function requestClose() {
    const e = state.editor;
    if (!e) return;
    if (curveDraft(e) === e.savedCurve && metaDraft() === e.savedMeta) { closeEditor(); return; }
    if ($('edDiscard').hidden) e.returnFocus = document.activeElement;
    $('edDiscard').hidden = false;
    $('edKeep').focus();
  }

  function keepEditing() {
    const back = state.editor && state.editor.returnFocus;
    $('edDiscard').hidden = true;
    (back && back.isConnected && $('sheet').contains(back) ? back : $('edClose')).focus();
  }

  // Esc closes the sheet (or answers "Continuar editando"); Tab and Shift+Tab go around inside it.
  function sheetKey(ev) {
    if (!state.editor || ev.isComposing) return;
    if (ev.key === 'Escape') {
      ev.preventDefault();
      if ($('edDiscard').hidden) requestClose(); else keepEditing();
      return;
    }
    if (ev.key !== 'Tab') return;
    const items = [...$('sheet').querySelectorAll('button, select, input, summary, [tabindex="0"]')]
      .filter((n) => !n.disabled && (n.checkVisibility ? n.checkVisibility() : n.getClientRects().length > 0));
    const first = items[0], last = items[items.length - 1];
    if (ev.shiftKey && document.activeElement === first) { ev.preventDefault(); last.focus(); }
    else if (!ev.shiftKey && document.activeElement === last) { ev.preventDefault(); first.focus(); }
  }

  function renderEditor() {
    const e = state.editor;
    if (!e) return;
    $('edModeCurve').setAttribute('aria-pressed', e.mode === 'curve');
    $('edModeFixed').setAttribute('aria-pressed', e.mode === 'fixed');
    $('edCurveBox').hidden = e.mode !== 'curve';
    $('edFixedBox').hidden = e.mode !== 'fixed';
    $('edFixedValue').textContent = e.percent + '%';
    $('edResponseHint').textContent = tr('ed.response.hint', { name: tr(RESPONSES[e.response][0]), desc: tr(RESPONSES[e.response][1]) });

    const svg = $('edSvg');
    const pts = e.points;
    if (svg.querySelectorAll('.point').length !== pts.length) buildEditorSvg(pts.length);
    let d = '';
    pts.forEach(([t, p], i) => {
      if (i === 0) d += `M${exX(t)} ${exY(p)}`;
      else if (e.style === 'StairStepMode') d += `H${exX(t)}V${exY(p)}`;
      else d += `L${exX(t)} ${exY(p)}`;
    });
    d += `H${exX(100)}`;
    svg.querySelector('.area').setAttribute('d', d + `V${exY(0)}H${exX(0)}Z`);
    svg.querySelector('.curve').setAttribute('d', d);

    const snap = state.snap || {};
    const srcT = snap.tempInputs ? snap.tempInputs[e.tempInput] : null;
    const hasT = srcT !== null && srcT !== undefined;
    const now = svg.querySelector('.now');
    now.toggleAttribute('hidden', !hasT);
    if (hasT) { now.setAttribute('x1', exX(srcT)); now.setAttribute('x2', exX(srcT)); }
    $('edNow').textContent = hasT ? tr('ed.now', { t: fmt(srcT), p: pct(curveAt(pts.map(([t, p]) => [t, p * 2.55]), e.style, srcT)) }) : '';

    const limits = state.fans.limits;
    svg.querySelectorAll('.point').forEach((g, i) => {
      const [t, p] = pts[i];
      g.setAttribute('transform', `translate(${exX(t)} ${exY(p)})`);
      g.setAttribute('aria-valuenow', p);
      g.setAttribute('aria-valuemin', limits.minDutyPercent);
      g.setAttribute('aria-valuetext', `${t} °C, ${p}%`);
      g.classList.toggle('selected', i === e.selected);
    });
    $('edPoints').textContent = pts.map(([t, p]) => `${t}° ${p}%`).join(' · ');
    renderTune();
  }

  // Grid, axes and one slider per point, built when the editor opens. renderEditor moves them in place, so a point
  // keeps its node, and the focus, while it moves and while the live data redraws the sheet.
  function buildEditorSvg(n) {
    const svg = $('edSvg');
    svg.innerHTML = '';
    const grid = el('g', { class: 'grid' }, svg);
    const axis = el('g', { class: 'axis' }, svg);
    for (let p = 0; p <= 100; p += 25) {
      el('line', { x1: ED.l, x2: ED.w - ED.r, y1: exY(p), y2: exY(p) }, grid);
      el('text', { x: ED.l - 5, y: exY(p) + 3, 'text-anchor': 'end' }, axis).textContent = p + '%';
    }
    for (let t = 0; t <= 100; t += 20) {
      el('line', { x1: exX(t), x2: exX(t), y1: ED.t, y2: ED.h - ED.b }, grid);
      el('text', { x: exX(t), y: ED.h - 8, 'text-anchor': 'middle' }, axis).textContent = t + '°';
    }
    el('path', { class: 'area' }, svg);
    el('path', { class: 'curve' }, svg);
    el('line', { class: 'now', y1: ED.t, y2: ED.h - ED.b }, svg);
    // The value a screen reader adjusts (swipe up or down) is the speed; ←/→ change the temperature.
    for (let i = 0; i < n; i++) {
      const g = el('g', {
        class: 'point', tabindex: 0, role: 'slider', 'aria-orientation': 'vertical',
        'aria-label': tr('ed.point', { i: i + 1, n }), 'aria-valuemax': 100, 'data-i': i,
      }, svg);
      el('circle', { class: 'ring', r: 11 }, g);
      el('circle', { class: 'pt' + (i === 0 || i === n - 1 ? ' fixed' : ''), r: 6 }, g);
      el('circle', { class: 'hit', r: HIT_R }, g);
    }
  }

  // The fine-tune bar shows the selected point; the hint takes its place until a point is selected.
  function renderTune() {
    const e = state.editor, i = e.selected;
    $('edTune').hidden = i === null;
    $('edTuneHint').hidden = i !== null;
    if (i === null) return;
    const [temp, p] = e.points[i];
    $('edTuneLabel').textContent = tr('ed.tune.label', { i: i + 1, n: e.points.length, t: temp, p });
    // The first point's temperature and the last one's speed don't move.
    $('edTuneTMinus').disabled = $('edTuneTPlus').disabled = i === 0;
    $('edTunePMinus').disabled = $('edTunePPlus').disabled = i === e.points.length - 1;
  }

  // Moves point i by [dt °C, dp %] within the rules (arrow keys and the fine-tune bar); it becomes the selected one.
  function nudge(i, dt, dp) {
    const e = state.editor;
    cancelAnimationFrame(morphFrame);
    const [t, p] = e.points[i];
    e.points[i] = movePoint(e.points, i, t + dt, p + dp, state.fans.limits);
    e.selected = i;
    renderEditor();
  }

  // A focused point: ←/→ the temperature and ↑/↓ the speed, 1 at a time or 5 with Shift; PageUp/PageDown 10%,
  // Home/End the lowest and the highest speed the point can have.
  const POINT_KEYS = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowDown: [0, -1], ArrowUp: [0, 1], PageDown: [0, -10], PageUp: [0, 10], Home: [0, -100], End: [0, 100] };
  function pointKey(ev) {
    const g = ev.target.closest('.point');
    const step = POINT_KEYS[ev.key];
    if (!g || !step || ev.altKey || ev.ctrlKey || ev.metaKey) return;
    ev.preventDefault();
    const k = ev.shiftKey ? 5 : 1;
    nudge(Number(g.getAttribute('data-i')), step[0] * k, step[1] * k);
  }

  // One fine-tune step on the selected point, said to screen readers; false when nothing moved.
  function tune(dt, dp) {
    const e = state.editor;
    if (!e || e.selected === null) return false;
    const before = e.points[e.selected].join();
    nudge(e.selected, dt, dp);
    $('edTuneLive').textContent = $('edTuneLabel').textContent;
    return e.points[e.selected].join() !== before;
  }

  // ◀ ▶ in the bar: the previous or the next point, going around.
  function selectNearby(d) {
    const e = state.editor;
    e.selected = (e.selected + d + e.points.length) % e.points.length;
    renderEditor();
    $('edTuneLive').textContent = $('edTuneLabel').textContent;
  }

  // − and + step once per tap, click or key press, and repeat while a pointer holds them. The click that ends a hold
  // doesn't add one more step.
  function wireTune() {
    const steps = { edTuneTMinus: [-1, 0], edTuneTPlus: [1, 0], edTunePMinus: [0, -1], edTunePPlus: [0, 1] };
    for (const [id, [dt, dp]] of Object.entries(steps)) {
      const b = $(id);
      let timer = 0, repeatedAt = -Infinity;
      const stop = () => clearTimeout(timer);
      b.addEventListener('pointerdown', (ev) => {
        if (ev.button !== 0) return;
        stop();
        timer = setTimeout(function repeat() {
          if (!tune(dt, dp)) return;
          repeatedAt = performance.now();
          timer = setTimeout(repeat, 80);
        }, 400);
      });
      for (const type of ['pointerup', 'pointercancel', 'pointerleave']) b.addEventListener(type, stop);
      b.addEventListener('click', () => { if (performance.now() - repeatedAt > 300) tune(dt, dp); });
      b.addEventListener('contextmenu', (ev) => ev.preventDefault());
    }
    $('edTunePrev').onclick = () => selectNearby(-1);
    $('edTuneNext').onclick = () => selectNearby(1);
  }

  // Picking a preset morphs the curve into place instead of jumping, so the change is visible.
  // Same strong ease-in-out as --ease-in-out would be in CSS; skipped under reduced motion.
  const MORPH_MS = 240;
  const easeInOut = cubicBezier(0.77, 0, 0.175, 1);
  let morphFrame = 0;
  function morphPoints(target) {
    const e = state.editor;
    cancelAnimationFrame(morphFrame);
    if (matchMedia('(prefers-reduced-motion: reduce)').matches || e.points.length !== target.length) {
      e.points = target;
      renderEditor();
      return;
    }
    const from = e.points.map((p) => p.slice());
    const start = performance.now();
    const step = (now) => {
      if (state.editor !== e) return; // sheet closed or reopened mid-morph
      const k = easeInOut(Math.min(1, (now - start) / MORPH_MS));
      e.points = from.map(([t, p], i) => [Math.round(t + (target[i][0] - t) * k), Math.round(p + (target[i][1] - p) * k)]);
      if (k < 1) morphFrame = requestAnimationFrame(step); else e.points = target;
      renderEditor();
    };
    morphFrame = requestAnimationFrame(step);
  }

  // CSS-style cubic-bezier(x1, y1, x2, y2) easing: solve x(u) = t by Newton's method, return y(u).
  function cubicBezier(x1, y1, x2, y2) {
    const a = (p1, p2) => 1 - 3 * p2 + 3 * p1, b = (p1, p2) => 3 * p2 - 6 * p1, c = (p1) => 3 * p1;
    const at = (u, p1, p2) => ((a(p1, p2) * u + b(p1, p2)) * u + c(p1)) * u;
    const slope = (u, p1, p2) => 3 * a(p1, p2) * u * u + 2 * b(p1, p2) * u + c(p1);
    return (t) => {
      if (t <= 0 || t >= 1) return t <= 0 ? 0 : 1;
      let u = t;
      for (let i = 0; i < 8; i++) {
        const d = slope(u, x1, x2);
        if (Math.abs(d) < 1e-6) break;
        u -= (at(u, x1, x2) - t) / d;
      }
      return at(Math.min(1, Math.max(0, u)), y1, y2);
    };
  }

  // Pressing within a point's target grabs the nearest point (targets overlap where points are close) and selects it
  // for the fine-tune bar. It follows the pointer, at the same offset it was grabbed, once the pointer moves 4 px:
  // a tap only selects.
  function startDrag(ev) {
    const e = state.editor;
    if (!e || ev.button !== 0) return;
    const svg = $('edSvg');
    const at = (v) => {
      const box = svg.getBoundingClientRect();
      return [(v.clientX - box.left) / box.width * ED.w, (v.clientY - box.top) / box.height * ED.h];
    };
    const [sx, sy] = at(ev);
    let i = -1, nearest = HIT_R;
    e.points.forEach(([t, p], k) => {
      const dist = Math.hypot(exX(t) - sx, exY(p) - sy);
      if (dist <= nearest) { i = k; nearest = dist; }
    });
    if (i < 0) return;
    ev.preventDefault();
    cancelAnimationFrame(morphFrame); // grabbing a point stops a running preset morph
    const dx = exX(e.points[i][0]) - sx, dy = exY(e.points[i][1]) - sy;
    e.selected = i;
    renderEditor();
    svg.querySelectorAll('.point')[i].focus({ preventScroll: true });
    svg.setPointerCapture(ev.pointerId);
    let moved = false;
    const move = (mv) => {
      if (!moved && Math.hypot(mv.clientX - ev.clientX, mv.clientY - ev.clientY) < 4) return;
      moved = true;
      const [x, y] = at(mv);
      e.points[i] = movePoint(e.points, i, (x + dx - ED.l) / (ED.w - ED.l - ED.r) * 100,
        (1 - (y + dy - ED.t) / (ED.h - ED.t - ED.b)) * 100, state.fans.limits);
      renderEditor();
    };
    const up = () => {
      svg.removeEventListener('pointermove', move);
      svg.removeEventListener('pointerup', up);
      svg.removeEventListener('pointercancel', up);
    };
    svg.addEventListener('pointermove', move);
    svg.addEventListener('pointerup', up);
    svg.addEventListener('pointercancel', up);
  }

  async function applyEditor() {
    const e = state.editor;
    const body = e.mode === 'fixed'
      ? { mode: 'fixed', percent: e.percent, tempInput: e.tempInput }
      : { mode: 'curve', style: e.style, points: e.points, tempInput: e.tempInput };
    if (e.mode === 'curve' && !hardwareCurves()) body.response = e.response;
    $('edApply').disabled = true;
    try {
      await api('/api/fans/' + e.index, body);
      toast(tr(hardwareCurves() ? 'ed.applied.chip' : 'ed.applied.app'));
      await loadFans();
      closeEditor();
    } catch (err) { toast(err.message, true); }
    finally { $('edApply').disabled = false; }
  }

  // The pump fields only matter for a pump, the minimum only for that policy.
  function renderMeta() {
    $('edPumpBox').hidden = $('edRole').value !== 'pump';
    $('edPumpMinField').hidden = $('edPumpPolicy').value !== 'min';
  }

  async function saveMeta() {
    const e = state.editor;
    const body = { label: $('edLabel').value.trim(), role: $('edRole').value };
    if (body.role === 'pump') {
      body.pumpPolicy = $('edPumpPolicy').value;
      body.pumpMinPercent = Number($('edPumpMin').value);
    }
    $('edMetaSave').disabled = true;
    try {
      await api('/api/fans/' + e.index + '/meta', body);
      toast(tr('ed.meta.saved'));
      await loadFans();
      // A curve not applied yet keeps the sheet open: saving the name doesn't throw it away.
      if (state.editor === e && curveDraft(e) !== e.savedCurve) {
        renderEditorTitle(state.fans.fans[e.index]);
        e.savedMeta = metaDraft();
        $('edMeta').open = false;
        $('edMeta').querySelector('summary').focus();
      } else closeEditor();
    } catch (err) { toast(err.message, true); }
    finally { $('edMetaSave').disabled = false; }
  }

  async function restoreEditor() {
    const e = state.editor;
    $('edRestore').disabled = true;
    try {
      await api('/api/fans/' + e.index + '/restore', {});
      toast(tr(hardwareCurves() ? 'ed.restored.chip' : 'ed.restored.bios'));
      await loadFans();
      closeEditor();
    } catch (err) { toast(err.message, true); }
    finally { $('edRestore').disabled = false; }
  }

  function wireEditor() {
    $('edClose').onclick = requestClose;
    $('backdrop').onclick = requestClose;
    $('edKeep').onclick = keepEditing;
    $('edDiscardYes').onclick = closeEditor;
    document.addEventListener('keydown', sheetKey);
    const svg = $('edSvg');
    svg.addEventListener('pointerdown', startDrag);
    svg.addEventListener('keydown', pointKey);
    // Reaching a point with Tab selects it, as a tap does.
    svg.addEventListener('focusin', (ev) => {
      const g = ev.target.closest('.point');
      if (!g || !state.editor) return;
      state.editor.selected = Number(g.getAttribute('data-i'));
      renderEditor();
    });
    wireTune();
    $('edModeCurve').onclick = () => { state.editor.mode = 'curve'; renderEditor(); };
    $('edModeFixed').onclick = () => { state.editor.mode = 'fixed'; renderEditor(); };
    $('edStyle').onchange = (ev) => { state.editor.style = ev.target.value; renderEditor(); };
    $('edResponse').onchange = (ev) => { state.editor.response = ev.target.value; renderEditor(); };
    $('edTempInput').onchange = (ev) => { state.editor.tempInput = Number(ev.target.value); renderEditor(); };
    $('edFixedSlider').oninput = (ev) => { state.editor.percent = Number(ev.target.value); renderEditor(); };
    $('edApply').onclick = applyEditor;
    $('edRestore').onclick = restoreEditor;
    $('edRole').onchange = renderMeta;
    $('edPumpPolicy').onchange = renderMeta;
    $('edMetaSave').onclick = saveMeta;
  }

  // The preset chips, from the backend's presets (built when the editor opens: they arrive with /api/fans).
  function renderPresetChips() {
    $('edPresets').replaceChildren(...Object.entries(state.fans.presets || {}).map(([key, preset]) => {
      const b = document.createElement('button');
      b.className = 'btn';
      b.textContent = presetName(key);
      b.onclick = () => morphPoints(presetPoints(key, state.fans.limits, state.editor.pointCount));
      return b;
    }));
  }

  // ---------- GPU ----------
  function renderGpu(gpu) {
    if (!gpu || !gpu.ok) {
      $('gpuName').textContent = 'GPU';
      $('gpuGrid').innerHTML = '<p class="hint"></p>';
      $('gpuGrid').firstChild.textContent = gpu && gpu.error ? tr('gpu.unavailableWhy', { error: msg(gpu.error) }) : tr('gpu.unavailable');
      $('gpuControls').hidden = true;
      $('gpuFixedBox').hidden = true;
      return;
    }
    $('gpuControls').hidden = false;
    $('gpuName').textContent = gpu.name;
    $('gpuSub').textContent = tr(gpu.fanMode === 'fixed' ? 'gpu.sub.fixed' : 'gpu.sub.auto');
    const kv = [
      ['gpu.temp', fmt(gpu.temp) + ' °C'], ['gpu.util', fmt(gpu.util) + '%'], ['gpu.power', fmt(gpu.power, 0) + ' W'],
      ['gpu.clock', fmt(gpu.clock) + ' MHz'], ['gpu.memClock', fmt(gpu.memClock) + ' MHz'],
      ['gpu.vram', gpu.memTotal ? fmt(gpu.memUsed / 1024, 1) + '/' + fmt(gpu.memTotal / 1024, 0) + ' GB' : '–'],
    ];
    $('gpuGrid').innerHTML = kv.map(([k, v]) => `<div class="kv"><div class="k">${esc(tr(k))}</div><div class="v">${v}</div></div>`).join('');
    const fanPct = gpu.fans && gpu.fans.length ? gpu.fans.join(' / ') + '%' : '–';
    $('gpuFanValue').textContent = tr('gpu.fanValue', { value: fanPct });
    if (!state.gpuDirty) {
      const fixed = gpu.fanMode === 'fixed';
      $('gpuAuto').setAttribute('aria-pressed', !fixed);
      $('gpuFixed').setAttribute('aria-pressed', fixed);
      $('gpuFixedBox').hidden = !fixed;
      $('gpuSlider').min = gpu.fanMin || 30;
      $('gpuSlider').max = gpu.fanMax || 100;
      if (fixed && gpu.fanTarget) $('gpuSlider').value = gpu.fanTarget;
    }
    $('gpuSafety').textContent = tr('gpu.safety', { n: Number($('gpuSlider').value), temp: gpu.safetyTemp });
    $('gpuNote').hidden = !gpu.safetyNote;
    $('gpuNote').textContent = msg(gpu.safetyNote);
  }

  function wireGpu() {
    $('gpuAuto').onclick = async () => {
      state.gpuDirty = false;
      try { await api('/api/gpu/fan', { mode: 'auto' }); toast(tr('gpu.toast.auto')); } catch (e) { toast(e.message, true); }
    };
    $('gpuFixed').onclick = () => {
      state.gpuDirty = true;
      $('gpuAuto').setAttribute('aria-pressed', false);
      $('gpuFixed').setAttribute('aria-pressed', true);
      $('gpuFixedBox').hidden = false;
    };
    $('gpuSlider').oninput = () => { state.gpuDirty = true; };
    $('gpuApply').onclick = async () => {
      try {
        await api('/api/gpu/fan', { mode: 'fixed', percent: Number($('gpuSlider').value) });
        state.gpuDirty = false;
        toast(tr('gpu.toast.fixed', { n: Number($('gpuSlider').value) }));
      } catch (e) { toast(e.message, true); }
    };
  }

  // ---------- alerts (item 16) ----------
  const hhmm = (ms) => time(ms, { hour: '2-digit', minute: '2-digit' });

  // An alert's title and detail are messages ({code, params, text}), translated here.
  const alertTitle = (a) => msg(a.title);
  const alertDetail = (a) => msg(a.detail);

  function alertItem(a, onAck) {
    const row = document.createElement('div');
    row.className = 'alert-item ' + (a.resolved ? 'resolved' : a.severity);
    const text = document.createElement('div');
    const title = document.createElement('strong');
    title.textContent = alertTitle(a);
    const detail = document.createElement('span');
    detail.textContent = alertDetail(a) + ' · ' + (a.resolved ? tr('alerts.resolvedAt', { time: hhmm(a.resolved) }) : tr('alerts.since', { time: hhmm(a.since) }));
    text.append(title, detail);
    row.append(text);
    if (onAck) {
      const ok = document.createElement('button');
      ok.className = 'btn';
      ok.textContent = tr('alerts.ack');
      ok.setAttribute('aria-label', tr('alerts.ack.aria', { title: alertTitle(a) }));
      ok.onclick = () => onAck(a, ok);
      row.append(ok);
    }
    return row;
  }

  // The active alerts come with every snapshot; the banner shows the ones not acknowledged yet. Rebuilt only when
  // that set changes, so a screen reader does not hear the same alert every second.
  let bannerKey = null;
  function renderAlertBanner(alerts) {
    const open = (alerts || []).filter((a) => !a.acknowledged);
    const key = open.map((a) => a.id).join('|');
    if (key === bannerKey) return;
    const first = bannerKey === null;
    bannerKey = key;
    $('alertBanner').replaceChildren(...open.map((a) => alertItem(a, acknowledge)));
    $('alertBanner').hidden = !open.length;
    if (!first) loadAlerts();
  }

  async function acknowledge(a, button) {
    button.disabled = true;
    try {
      await api('/api/alerts/ack', { id: a.id });
      a.acknowledged = true;
      renderAlertBanner(state.alerts);
    } catch (e) { toast(e.message, true); button.disabled = false; }
  }

  async function loadAlerts() {
    try {
      const { active, recent } = await api('/api/alerts');
      $('alertList').replaceChildren(...active.map((a) => alertItem(a)), ...recent.map((a) => alertItem(a)));
      $('alertsSub').textContent = active.length ? tr('alerts.active', { count: active.length }) : tr('alerts.none');
    } catch (e) { $('alertsSub').textContent = tr('alerts.error', { error: e.message }); }
  }

  async function loadAlertSettings() {
    try {
      const s = await api('/api/alerts/settings');
      $('alCpu').value = s.cpuTemp;
      $('alGpu').value = s.gpuTemp;
      $('alPcName').value = s.pcName;
      $('alNtfy').value = '';
      $('alTgToken').value = '';
      $('alTgChat').value = s.telegramChatId || '';
      $('alNtfyState').textContent = s.ntfy ? tr('alerts.ntfy.set', { value: s.ntfy }) : tr('alerts.ntfy.unset');
      $('alTgState').textContent = s.telegram ? tr('alerts.telegram.set', { value: s.telegram }) : tr('alerts.telegram.unset');
    } catch (e) { toast(e.message, true); }
  }

  // Secrets go only when typed: an empty field keeps what is saved. clear: removes both destinations.
  async function saveAlertSettings(clear) {
    // The messages that leave the PC go out in this panel's language.
    const body = { cpuTemp: Number($('alCpu').value), gpuTemp: Number($('alGpu').value), pcName: $('alPcName').value, language: language() };
    if (clear) Object.assign(body, { ntfyUrl: '', telegramToken: '', telegramChatId: '' });
    else {
      if ($('alNtfy').value.trim()) body.ntfyUrl = $('alNtfy').value.trim();
      if ($('alTgToken').value.trim()) body.telegramToken = $('alTgToken').value.trim();
      body.telegramChatId = $('alTgChat').value.trim();
    }
    try {
      await api('/api/alerts/settings', body);
      toast(tr(clear ? 'alerts.cleared' : 'alerts.saved'));
      await loadAlertSettings();
    } catch (e) { toast(e.message, true); }
  }

  async function testAlerts() {
    $('alTest').disabled = true;
    try {
      const r = await api('/api/alerts/test', {});
      toast(r.ok ? tr('alerts.testSent') : tr('alerts.testFailed', { errors: r.errors.join('; ') }), !r.ok);
    } catch (e) { toast(e.message, true); }
    finally { $('alTest').disabled = false; }
  }

  // A topic is a password for ntfy: random and long, so nobody else stumbles on it.
  function newNtfyTopic() {
    const alphabet = 'abcdefghijklmnopqrstuvwxyz234567';
    const bytes = crypto.getRandomValues(new Uint8Array(20));
    $('alNtfy').value = 'https://ntfy.sh/fanpilot-' + Array.from(bytes, (b) => alphabet[b % 32]).join('');
  }

  function wireAlerts() {
    $('alertSettings').addEventListener('toggle', () => { if ($('alertSettings').open) loadAlertSettings(); });
    $('alSave').onclick = () => saveAlertSettings(false);
    $('alClear').onclick = () => saveAlertSettings(true);
    $('alTest').onclick = testAlerts;
    $('alNtfyNew').onclick = newNtfyTopic;
  }

  // ---------- sensors table ----------
  const GROUPS = { Temperature: 'sensors.group.Temperature', Fan: 'sensors.group.Fan', Voltage: 'sensors.group.Voltage', Frequency: 'sensors.group.Frequency', Power: 'sensors.group.Power' };
  function renderSensors(snap) {
    if (!snap.sensors || !$('sensorTable').closest('details').open) return;
    let html = '';
    Object.keys(GROUPS).forEach((type) => {
      const rows = snap.sensors.filter((s) => s.type === type);
      if (!rows.length) return;
      html += `<tr class="group"><td colspan="2">${esc(tr(GROUPS[type]))}</td></tr>`;
      rows.forEach((s) => {
        const digits = s.unit === 'V' ? 3 : (s.unit === 'W' || (s.unit === 'MHz' && s.value < 1000)) ? 1 : 0;
        html += `<tr><td>${esc(sensorTitle(s))}</td><td>${fmt(s.value, digits)} ${esc(s.unit)}</td></tr>`;
      });
    });
    $('sensorTable').innerHTML = html;
  }

  // ---------- live loop ----------
  function setStatus(ok, text) {
    $('statusDot').className = 'dot ' + (ok ? 'ok' : 'bad');
    $('statusText').textContent = text;
  }

  // Problems with config.json or the state files, reported by the server; textContent, so nothing is markup.
  function renderNotices(list) {
    const box = $('notices');
    const text = (list || []).map(msg).join('\n');
    if (box.textContent !== text) box.textContent = text;
    box.hidden = !text;
  }

  function onSnapshot(snap) {
    if (!snap.t) return; // server has not sampled yet
    state.snap = snap;
    pushHistory(snap);
    const svc = snap.service || {};
    setStatus(svc.ok, tr(svc.ok ? 'status.live' : 'status.boardDown'));
    renderNotices(svc.notices);
    state.alerts = snap.alerts || [];
    renderAlertBanner(state.alerts);
    renderTiles(snap);
    renderCharts();
    renderFans();
    renderGpu(snap.gpu);
    renderSensors(snap);
    if (state.editor) renderEditor();
  }

  // EventSource retries by itself only after a network error. A stream the server refuses (FanPilot still starting,
  // or too many panels open: both 503) is closed for good, so a new one is opened a little later.
  const RECONNECT_MS = 3000;
  function connect() {
    const es = new EventSource('/api/stream');
    let lastFrame = Date.now();
    es.onmessage = (m) => {
      lastFrame = Date.now();
      try { onSnapshot(JSON.parse(m.data)); } catch (e) { /* ignore malformed frame */ }
    };
    // A snapshot arrives every second: 5 s of silence on an open stream means the readings are stuck, not live.
    const stall = setInterval(() => {
      if (es.readyState === EventSource.OPEN && Date.now() - lastFrame > 5000) setStatus(false, tr('status.stale'));
    }, 1000);
    es.onerror = async () => {
      setStatus(false, tr('status.reconnecting'));
      if (es.readyState === EventSource.CLOSED) {
        clearInterval(stall);
        setTimeout(connect, RECONNECT_MS);
      }
      // EventSource hides the status code; check whether we lost the pairing.
      try { const r = await fetch('/api/state'); if (r.status === 401) location.reload(); } catch (e) { /* offline */ }
    };
  }

  async function init() {
    // Words first: everything below writes text.
    await loadLanguage();
    translatePage();
    wireLanguageSelect($('langSelect'));
    wireEditor();
    wireGpu();
    wireAlerts();
    loadAlerts();
    Object.values(CHARTS).forEach((def) => {
      $(def.wrap).addEventListener('pointermove', (ev) => chartHover(def, ev));
      $(def.wrap).addEventListener('pointerleave', () => chartLeave(def));
    });
    $('turboBtn').onclick = toggleTurbo;
    $('profileSeg').querySelectorAll('button').forEach((b) => { b.onclick = () => setProfile(b.dataset.mode); });
    $('sensorTable').closest('details').addEventListener('toggle', () => state.snap && renderSensors(state.snap));
    $('logout').onclick = async () => {
      if (!confirm(tr('top.logout.confirm'))) return;
      await api('/api/logout', {}).catch(() => {});
      location.reload();
    };
    try {
      state.snap = await api('/api/state');
      seedHistory(await api('/api/history'));
    } catch (e) { /* stream will fill in */ }
    await loadFans();
    connect();
    setInterval(loadFans, 30000); // picks up curve changes made elsewhere (watchdog, GCC)
    loadInfo();
  }

  async function loadInfo() {
    try {
      const info = await api('/api/info');
      $('appVersion').textContent = 'FanPilot ' + info.version + (info.backend ? ' · ' + info.backend : '');
    } catch (e) { /* the footer just stays empty */ }
  }

  init();
})();
