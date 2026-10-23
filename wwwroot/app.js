// FanPilot dashboard: live data over SSE (/api/stream), fan curve editor, GPU fan control.
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
  };

  // ---------- helpers ----------
  async function api(path, body) {
    const opts = body === undefined ? {} : {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-FanPilot': '1' }, body: JSON.stringify(body)
    };
    const r = await fetch(path, opts);
    if (r.status === 401) { location.reload(); throw new Error('Não pareado'); }
    const data = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(data.error || r.statusText);
    return data;
  }

  let toastTimer = 0;
  function toast(msg, isError) {
    const t = $('toast');
    t.textContent = msg;
    t.className = 'toast' + (isError ? ' error' : '');
    t.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { t.hidden = true; }, isError ? 5000 : 2200);
  }

  function el(tag, attrs, parent) {
    const e = document.createElementNS(SVGNS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }

  const fmt = (v, d = 0) => (v === null || v === undefined || Number.isNaN(v)) ? '–' : Number(v).toFixed(d).replace('.', ',');
  const pct = (pwm) => Math.round(pwm / 2.55);

  function tempStatus(t) {
    if (t === null || t === undefined) return { cls: '', label: 'sem dado' };
    if (t < 60) return { cls: 'good', label: 'normal' };
    if (t < 75) return { cls: 'warning', label: 'morno' };
    if (t < 85) return { cls: 'serious', label: 'quente' };
    return { cls: 'critical', label: 'crítico' };
  }

  function sensor(snap, type, title) {
    if (!snap || !snap.sensors) return null;
    return snap.sensors.find((s) => s.type === type && s.title === title) || null;
  }
  const cpuTempOf = (snap) => { const s = sensor(snap, 'Temperature', 'CPU'); return s ? s.value : null; };

  // ---------- tiles ----------
  function renderTiles(snap) {
    const gpu = snap.gpu || {};
    const cpuT = cpuTempOf(snap);
    const pkg = sensor(snap, 'Power', 'CPU Package');
    const clk = sensor(snap, 'Frequency', 'CPU');
    const tiles = [
      { label: 'CPU', value: fmt(cpuT), unit: '°C', status: tempStatus(cpuT), meta: clk ? fmt(clk.value / 1000, 2) + ' GHz' : '' },
      { label: gpu.name ? 'GPU' : 'GPU (indisponível)', value: fmt(gpu.temp), unit: '°C', status: tempStatus(gpu.temp), meta: gpu.util !== undefined ? gpu.util + '% uso' : '' },
      { label: 'CPU · potência', value: fmt(pkg && pkg.value, 0), unit: 'W', meta: 'pacote' },
      { label: 'GPU · potência', value: fmt(gpu.power, 0), unit: 'W', meta: gpu.powerLimit ? 'limite ' + gpu.powerLimit + ' W' : '' },
    ];
    $('tiles').innerHTML = tiles.map((t) =>
      `<div class="tile"><div class="label">${t.label}</div>` +
      `<div class="value">${t.value}<small>${t.unit}</small></div>` +
      `<div class="meta">${t.status ? `<span class="badge ${t.status.cls}">${t.status.label}</span>` : ''}${t.meta ? '<span>' + t.meta + '</span>' : ''}</div></div>`
    ).join('');
  }

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
    const cpuId = state.snap && state.snap.sensors && (state.snap.sensors.find((s) => s.type === 'Temperature' && s.title === 'CPU') || {}).id;
    const n = hist.t.length;
    const take = (src) => { const a = (src || []).slice(0, n); while (a.length < n) a.push(null); return a; };
    const series = {
      cpu: take((cpuId && hist.series[cpuId]) || hist.series['temperature-514']),
      gpu: take(hist.series['gpu-temp']),
    };
    Object.keys(hist.series).filter((k) => k.startsWith('fan-')).forEach((k) => { series[k] = take(hist.series[k]); });
    state.history = { t: hist.t.slice(), series };
  }

  const shortFanName = (title) => title.replace(/^System\s*/i, 'Sys ');
  const fmtRpm = (v) => v >= 1000 ? fmt(v / 1000, 1) + 'k' : String(Math.round(v));

  // Fans whose tachometer reads 0 for the whole window have nothing to plot.
  function spinningFans() {
    if (!state.fans) return [];
    const h = state.history;
    return state.fans.fans.map((f, i) => ({ f, i })).filter(({ i }) => (h.series['fan-' + i] || []).some((v) => v > 0));
  }

  const CHARTS = {
    temp: {
      svg: 'chart', wrap: 'chartWrap', tip: 'tip', r: 56, unit: '°C',
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
      svg: 'rpmChart', wrap: 'rpmWrap', tip: 'rpmTip', r: 82, unit: 'RPM',
      tick: fmtRpm, endText: (s, v) => `${s.label} ${Math.round(v)}`,
      domain: (vals) => [0, niceCeil(Math.max(...vals, 1000) * 1.1)],
      series: () => spinningFans().map(({ f, i }) => ({ key: 'fan-' + i, label: shortFanName(f.title), name: f.title, color: `var(--fan-${i})` })),
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
    if (!h.t.length || !series.length) return series;
    // Draw in CSS pixels so axis text keeps its real size on narrow phones.
    const g = { w: Math.max(300, Math.round($(def.wrap).clientWidth)), l: 38, r: def.r, t: 10, b: 24 };
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
    for (let m = 10; m >= 0; m -= 2) {
      const tx = el('text', { x: g.X(now - m * 60000), y: g.h - 6, 'text-anchor': m === 0 ? 'end' : 'middle' }, axis);
      tx.textContent = m === 0 ? 'agora' : '-' + m + ' min';
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
    $('rpmLegend').innerHTML = fans.map((s) => `<span><i style="background:${s.color}"></i>${s.name}</span>`).join('');
    if (state.fans) {
      const silent = state.fans.fans.filter((f, i) => !fans.some((s) => s.key === 'fan-' + i)).map((f) => f.title);
      $('rpmNote').textContent = silent.length ? `Sem leitura de RPM (0): ${silent.join(', ')} — não aparecem no gráfico.` : '';
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
    tip.innerHTML = `<div>${new Date(h.t[best]).toLocaleTimeString('pt-BR')}</div>` + g.series.map((s) => {
      const v = h.series[s.key][best];
      return `<div class="row"><i style="background:${s.color}"></i>${s.name || s.label} <b>${v === null ? '–' : (def.unit === 'RPM' ? Math.round(v) : fmt(v))} ${def.unit}</b></div>`;
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
  // PWM (0..255) a header outputs at temperature t for a hardware curve.
  function curveAt(points, style, t) {
    if (!points.length) return null;
    if (t <= points[0][0]) return points[0][1];
    for (let i = 1; i < points.length; i++) {
      const [x0, y0] = points[i - 1], [x1, y1] = points[i];
      if (t < x1) return style === 'StairStepMode' ? y0 : y0 + (y1 - y0) * (t - x0) / (x1 - x0);
    }
    return points[points.length - 1][1];
  }

  function fanDescription(f, snap) {
    const req = f.request;
    let mode;
    if (state.fans.turbo) mode = 'Turbo 100%';
    else if (f.managed && req && req.mode === 'fixed') mode = `Fixo ${req.percent}%`;
    else if (f.managed) mode = 'Curva personalizada';
    else if (f.points.every((p) => p[1] >= 255)) mode = 'Velocidade máxima (perfil do GCC)';
    else mode = 'Curva original';
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
    if (!state.fans.supported) {
      box.innerHTML = '<p class="hint"></p>';
      box.firstChild.textContent = `Esta placa-mãe (${state.fans.board || 'desconhecida'}) não oferece controle de fans pelo ` +
        'caminho que o FanPilot usa. O monitoramento continua funcionando.';
      return;
    }
    if (!state.fans.fans.length) { box.innerHTML = '<p class="hint">Controle de fans indisponível.</p>'; return; }
    const snap = state.snap || {};
    if (box.children.length !== state.fans.fans.length) {
      box.innerHTML = '';
      state.fans.fans.forEach((f, i) => {
        const b = document.createElement('button');
        b.className = 'fan';
        b.innerHTML = '<div class="name"></div><div class="rpm"></div>' +
          `<div class="duty" role="meter" aria-valuemin="0" aria-valuemax="100"><i style="background:var(--fan-${i})"></i></div>` +
          '<div class="desc"></div>';
        b.onclick = () => openEditor(i);
        box.appendChild(b);
      });
    }
    const turbo = !!state.fans.turbo;
    $('turboBtn').setAttribute('aria-pressed', turbo);
    $('turboBtn').textContent = turbo ? '⚡ Turbo ligado' : '⚡ Turbo 100%';
    $('turboBanner').hidden = !turbo;
    state.fans.fans.forEach((f, i) => {
      const b = box.children[i];
      const rpm = snap.fanRpm ? snap.fanRpm[i] : null;
      const duty = commandedPercent(f, snap);
      b.querySelector('.name').innerHTML = f.title + (turbo ? '<span class="pill">Turbo</span>' : f.managed ? '<span class="pill">FanPilot</span>' : '');
      b.querySelector('.rpm').innerHTML = rpm ? `${rpm}<small> RPM</small>` : '<small>0 RPM · sem leitura</small>';
      const meter = b.querySelector('.duty');
      meter.firstChild.style.width = (duty === null ? 0 : duty) + '%';
      meter.setAttribute('aria-valuenow', duty === null ? 0 : duty);
      meter.title = 'Velocidade comandada: ' + (duty === null ? '–' : duty + '%');
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

  async function loadFans() {
    try { state.fans = await api('/api/fans'); renderFans(); renderCharts(); }
    catch (e) { $('fans').innerHTML = `<p class="hint">Fans indisponíveis: ${e.message}</p>`; }
  }

  async function toggleTurbo() {
    if (!state.fans) return;
    const on = !state.fans.turbo;
    $('turboBtn').disabled = true;
    try {
      await api('/api/fans/turbo', { on });
      toast(on ? 'Turbo: fans da placa-mãe em 100%' : 'Turbo desligado: configurações anteriores restauradas');
      await loadFans();
    } catch (e) { toast(e.message, true); }
    finally { $('turboBtn').disabled = false; }
  }

  // ---------- fan editor ----------
  const PRESETS = {
    'Silencioso': [[0, 25], [40, 25], [50, 30], [60, 40], [65, 50], [70, 65], [75, 80], [85, 100]],
    'Padrão': [[0, 30], [35, 30], [45, 40], [55, 55], [62, 65], [68, 75], [74, 85], [82, 100]],
    'Performance': [[0, 45], [30, 45], [40, 55], [50, 70], [58, 80], [64, 90], [70, 95], [78, 100]],
    'Máximo': [[0, 100], [20, 100], [30, 100], [40, 100], [50, 100], [60, 100], [70, 100], [80, 100]],
  };
  const ED = { w: 340, h: 230, l: 34, r: 12, t: 10, b: 26 };
  const exX = (t) => ED.l + t / 100 * (ED.w - ED.l - ED.r);
  const exY = (p) => ED.t + (1 - p / 100) * (ED.h - ED.t - ED.b);

  // n points spread evenly over the same temperature range, linearly interpolated (presets are 8 points; boards vary).
  function resample(pts, n) {
    if (pts.length === n) return pts.map((p) => p.slice());
    const t0 = pts[0][0], t1 = pts[pts.length - 1][0];
    return Array.from({ length: n }, (_, k) => {
      const t = Math.round(t0 + (t1 - t0) * k / (n - 1));
      return [t, Math.round(curveAt(pts, 'SmartFanMode', t))];
    });
  }

  // Editor works in [temp, percent] with the fan's own point count; hardware readbacks may differ, so resample.
  function toEditorPoints(points, limits, n) {
    let pts = points.map(([t, pwm]) => [Math.round(t), pct(pwm)]);
    if (pts.length !== n) {
      const temps = [0, 30, 40, 50, 60, 70, 75, Math.min(85, limits.maxFullSpeedTemp)];
      pts = resample(temps.map((t) => [t, pct(curveAt(points, 'SmartFanMode', t))]), n);
    }
    pts[0][0] = 0;
    pts[pts.length - 1][1] = 100;
    return normalize(pts, limits);
  }

  function normalize(pts, limits) {
    for (let i = 0; i < pts.length; i++) {
      pts[i][1] = Math.max(limits.minDutyPercent, Math.min(100, pts[i][1]));
      if (i > 0) pts[i][1] = Math.max(pts[i][1], pts[i - 1][1]);
    }
    pts[pts.length - 1][1] = 100;
    return pts;
  }

  function openEditor(i) {
    if (state.fans.turbo) { toast('Desligue o Turbo para ajustar um fan individualmente.', true); return; }
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
    };
    $('edTitle').textContent = 'Fan · ' + f.title;
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
    $('edFixedHint').textContent = `Proteção: o chip ainda vai a 100% se o sensor passar de ${limits.fixedModeSafetyTemp} °C.`;
    $('edLimits').textContent = `Mínimo ${limits.minDutyPercent}% em qualquer ponto; 100% no máximo a ${limits.maxFullSpeedTemp} °C. A curva roda no chip da placa-mãe (continua valendo mesmo se o PC/FanPilot fechar).`;
    $('edRestore').disabled = !f.hasBackup;
    $('sheet').hidden = false;
    $('backdrop').hidden = false;
    renderEditor();
  }

  function closeEditor() {
    state.editor = null;
    $('sheet').hidden = true;
    $('backdrop').hidden = true;
  }

  function renderEditor() {
    const e = state.editor;
    if (!e) return;
    $('edModeCurve').setAttribute('aria-pressed', e.mode === 'curve');
    $('edModeFixed').setAttribute('aria-pressed', e.mode === 'fixed');
    $('edCurveBox').hidden = e.mode !== 'curve';
    $('edFixedBox').hidden = e.mode !== 'fixed';
    $('edFixedValue').textContent = e.percent + '%';

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
    const pts = e.points;
    let d = '';
    pts.forEach(([t, p], i) => {
      if (i === 0) d += `M${exX(t)} ${exY(p)}`;
      else if (e.style === 'StairStepMode') d += `H${exX(t)}V${exY(p)}`;
      else d += `L${exX(t)} ${exY(p)}`;
    });
    d += `H${exX(100)}`;
    el('path', { class: 'area', d: d + `V${exY(0)}H${exX(0)}Z` }, svg);
    el('path', { class: 'curve', d }, svg);

    const snap = state.snap || {};
    const srcT = snap.tempInputs ? snap.tempInputs[e.tempInput] : null;
    if (srcT !== null && srcT !== undefined) {
      el('line', { class: 'now', x1: exX(srcT), x2: exX(srcT), y1: ED.t, y2: ED.h - ED.b }, svg);
      const nowPct = pct(curveAt(pts.map(([t, p]) => [t, p * 2.55]), e.style, srcT));
      $('edNow').textContent = `agora: ${fmt(srcT)} °C → ${nowPct}%`;
    }
    pts.forEach(([t, p], i) => {
      const fixedAxis = i === 0 || i === pts.length - 1;
      el('circle', { class: 'pt' + (fixedAxis ? ' fixed' : ''), cx: exX(t), cy: exY(p), r: 6 }, svg);
      const hit = el('circle', { class: 'hit', cx: exX(t), cy: exY(p), r: 16, 'data-i': i }, svg);
      hit.addEventListener('pointerdown', startDrag);
    });
    $('edPoints').textContent = pts.map(([t, p]) => `${t}° ${p}%`).join(' · ');
  }

  function startDrag(ev) {
    ev.preventDefault();
    const svg = $('edSvg');
    const i = Number(ev.target.getAttribute('data-i'));
    const e = state.editor;
    const limits = state.fans.limits;
    const last = e.points.length - 1;
    svg.setPointerCapture(ev.pointerId);
    const move = (mv) => {
      const box = svg.getBoundingClientRect();
      const sx = (mv.clientX - box.left) / box.width * ED.w;
      const sy = (mv.clientY - box.top) / box.height * ED.h;
      let t = Math.round((sx - ED.l) / (ED.w - ED.l - ED.r) * 100);
      let p = Math.round((1 - (sy - ED.t) / (ED.h - ED.t - ED.b)) * 100);
      const prev = e.points[i - 1], next = e.points[i + 1];
      if (i === 0) t = 0;
      else t = Math.max(prev[0] + 1, Math.min(next ? next[0] - 1 : limits.maxFullSpeedTemp, t));
      if (i === last) p = 100;
      else p = Math.max(Math.max(limits.minDutyPercent, prev ? prev[1] : 0), Math.min(next[1], p));
      e.points[i] = [t, p];
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
    $('edApply').disabled = true;
    try {
      await api('/api/fans/' + e.index, body);
      toast('Aplicado no chip da placa-mãe');
      await loadFans();
      closeEditor();
    } catch (err) { toast(err.message, true); }
    finally { $('edApply').disabled = false; }
  }

  async function restoreEditor() {
    const e = state.editor;
    $('edRestore').disabled = true;
    try {
      await api('/api/fans/' + e.index + '/restore', {});
      toast('Curva original restaurada');
      await loadFans();
      closeEditor();
    } catch (err) { toast(err.message, true); }
    finally { $('edRestore').disabled = false; }
  }

  function wireEditor() {
    $('edClose').onclick = closeEditor;
    $('backdrop').onclick = closeEditor;
    $('edModeCurve').onclick = () => { state.editor.mode = 'curve'; renderEditor(); };
    $('edModeFixed').onclick = () => { state.editor.mode = 'fixed'; renderEditor(); };
    $('edStyle').onchange = (ev) => { state.editor.style = ev.target.value; renderEditor(); };
    $('edTempInput').onchange = (ev) => { state.editor.tempInput = Number(ev.target.value); renderEditor(); };
    $('edFixedSlider').oninput = (ev) => { state.editor.percent = Number(ev.target.value); renderEditor(); };
    $('edApply').onclick = applyEditor;
    $('edRestore').onclick = restoreEditor;
    const chips = $('edPresets');
    Object.keys(PRESETS).forEach((name) => {
      const b = document.createElement('button');
      b.className = 'btn';
      b.textContent = name;
      b.onclick = () => {
        const limits = state.fans.limits;
        const preset = PRESETS[name].map(([t, p]) => [Math.min(t, limits.maxFullSpeedTemp), p]);
        state.editor.points = normalize(resample(preset, state.editor.pointCount), limits);
        renderEditor();
      };
      chips.appendChild(b);
    });
  }

  // ---------- GPU ----------
  function renderGpu(gpu) {
    if (!gpu || !gpu.ok) {
      $('gpuName').textContent = 'GPU';
      $('gpuGrid').innerHTML = `<p class="hint">GPU NVIDIA indisponível${gpu && gpu.error ? ': ' + gpu.error : ''}</p>`;
      return;
    }
    $('gpuName').textContent = gpu.name;
    $('gpuSub').textContent = gpu.fanMode === 'fixed' ? 'fan manual' : 'fan automático';
    const kv = [
      ['Temperatura', fmt(gpu.temp) + ' °C'], ['Uso', fmt(gpu.util) + '%'], ['Potência', fmt(gpu.power, 0) + ' W'],
      ['Clock', fmt(gpu.clock) + ' MHz'], ['Memória', fmt(gpu.memClock) + ' MHz'],
      ['VRAM', gpu.memTotal ? fmt(gpu.memUsed / 1024, 1) + '/' + fmt(gpu.memTotal / 1024, 0) + ' GB' : '–'],
    ];
    $('gpuGrid').innerHTML = kv.map(([k, v]) => `<div class="kv"><div class="k">${k}</div><div class="v">${v}</div></div>`).join('');
    const fanPct = gpu.fans && gpu.fans.length ? gpu.fans.join(' / ') + '%' : '–';
    $('gpuFanValue').textContent = 'fan ' + fanPct;
    if (!state.gpuDirty) {
      const fixed = gpu.fanMode === 'fixed';
      $('gpuAuto').setAttribute('aria-pressed', !fixed);
      $('gpuFixed').setAttribute('aria-pressed', fixed);
      $('gpuFixedBox').hidden = !fixed;
      $('gpuSlider').min = gpu.fanMin || 30;
      $('gpuSlider').max = gpu.fanMax || 100;
      if (fixed && gpu.fanTarget) $('gpuSlider').value = gpu.fanTarget;
    }
    $('gpuSafety').textContent = `${$('gpuSlider').value}% · volta ao automático se ≥ ${gpu.safetyTemp} °C`;
    $('gpuNote').hidden = !gpu.safetyNote;
    $('gpuNote').textContent = gpu.safetyNote || '';
  }

  function wireGpu() {
    $('gpuAuto').onclick = async () => {
      state.gpuDirty = false;
      try { await api('/api/gpu/fan', { mode: 'auto' }); toast('Fan da GPU em automático'); } catch (e) { toast(e.message, true); }
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
        toast('Fan da GPU fixado em ' + $('gpuSlider').value + '%');
      } catch (e) { toast(e.message, true); }
    };
  }

  // ---------- sensors table ----------
  const GROUPS = { Temperature: 'Temperaturas', Fan: 'Fans', Voltage: 'Tensões', Frequency: 'Clocks', Power: 'Potência' };
  function renderSensors(snap) {
    if (!snap.sensors || !$('sensorTable').closest('details').open) return;
    let html = '';
    Object.keys(GROUPS).forEach((type) => {
      const rows = snap.sensors.filter((s) => s.type === type);
      if (!rows.length) return;
      html += `<tr class="group"><td colspan="2">${GROUPS[type]}</td></tr>`;
      rows.forEach((s) => {
        const digits = s.unit === 'V' ? 3 : (s.unit === 'W' || s.unit === 'MHz') ? 1 : 0;
        html += `<tr><td>${s.title}</td><td>${fmt(s.value, digits)} ${s.unit}</td></tr>`;
      });
    });
    $('sensorTable').innerHTML = html;
  }

  // ---------- live loop ----------
  function setStatus(ok, text) {
    $('statusDot').className = 'dot ' + (ok ? 'ok' : 'bad');
    $('statusText').textContent = text;
  }

  function onSnapshot(snap) {
    if (!snap.t) return; // server has not sampled yet
    state.snap = snap;
    pushHistory(snap);
    const svc = snap.service || {};
    setStatus(svc.ok, svc.ok ? 'ao vivo' : 'serviço Gigabyte indisponível');
    renderTiles(snap);
    renderCharts();
    renderFans();
    renderGpu(snap.gpu);
    renderSensors(snap);
    if (state.editor) renderEditor();
  }

  function connect() {
    const es = new EventSource('/api/stream');
    es.onmessage = (m) => { try { onSnapshot(JSON.parse(m.data)); } catch (e) { /* ignore malformed frame */ } };
    es.onerror = async () => {
      setStatus(false, 'reconectando…');
      // EventSource hides the status code; check whether we lost the pairing.
      try { const r = await fetch('/api/state'); if (r.status === 401) location.reload(); } catch (e) { /* offline */ }
    };
  }

  async function init() {
    wireEditor();
    wireGpu();
    Object.values(CHARTS).forEach((def) => {
      $(def.wrap).addEventListener('pointermove', (ev) => chartHover(def, ev));
      $(def.wrap).addEventListener('pointerleave', () => chartLeave(def));
    });
    $('turboBtn').onclick = toggleTurbo;
    $('sensorTable').closest('details').addEventListener('toggle', () => state.snap && renderSensors(state.snap));
    $('logout').onclick = async () => {
      if (!confirm('Desparear este aparelho? Será preciso escanear o QR code de novo.')) return;
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
  }

  init();
})();
