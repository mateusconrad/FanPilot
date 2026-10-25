// Pairing console (served to loopback only). Renders the one-time QR and manages paired devices.
import { t, dateTime, loadLanguage, translatePage, wireLanguageSelect } from './js/i18n.mjs';

const $ = (id) => document.getElementById(id);
let info = null;
let deadline = 0;
let selectedHost = null;

async function api(path, body) {
  const opts = body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-FanPilot': '1' }, body: JSON.stringify(body)
  };
  const r = await fetch(path, opts);
  if (!r.ok) {
    const data = await r.json().catch(() => ({}));
    throw new Error(data.code ? t(data.code, data.params, data.error) : data.error || r.statusText);
  }
  return r.json();
}

function render() {
  const hostSel = $('host');
  const hosts = info.urls.map((u) => new URL(u).host);
  if (!selectedHost || !hosts.includes(selectedHost)) selectedHost = hosts[0] || null;
  hostSel.innerHTML = '';
  hosts.forEach((h) => {
    const o = document.createElement('option');
    o.value = h; o.textContent = h; o.selected = h === selectedHost;
    hostSel.appendChild(o);
  });
  const url = info.urls.find((u) => new URL(u).host === selectedHost);
  if (!url) { $('qr').textContent = t('pair.noInterface'); return; }
  const qr = qrcode(0, 'M');
  qr.addData(url);
  qr.make();
  $('qr').innerHTML = qr.createSvgTag({ cellSize: 6, margin: 2, scalable: true });
  $('url').textContent = url.replace(/c=.*/, 'c=…');

  const list = $('sessions');
  list.innerHTML = '';
  if (!info.sessions.length) {
    list.innerHTML = '<p class="hint" style="margin:0"></p>';
    list.firstChild.textContent = t('pair.none');
  }
  info.sessions.forEach((s) => {
    const row = document.createElement('div');
    row.className = 'session';
    const left = document.createElement('div');
    left.innerHTML = '<div></div><div class="ua"></div>';
    left.firstChild.textContent = t('pair.seen', { ip: s.ip, when: dateTime(s.lastSeen) });
    left.lastChild.textContent = s.userAgent;
    const btn = document.createElement('button');
    btn.className = 'btn';
    btn.textContent = t('pair.revoke');
    btn.onclick = () => revoke(s.id);
    row.append(left, btn);
    list.appendChild(row);
  });
}

async function refresh() {
  try {
    info = await api('/api/pair/info');
    deadline = Date.now() + info.expiresIn * 1000;
    render();
  } catch (e) {
    $('qr').textContent = t('pair.error', { error: e.message });
  }
}

// A failure must say so: otherwise the phone looks unpaired while it still has access.
async function revoke(id) {
  $('pairError').hidden = true;
  try { await api('/api/pair/revoke', { id }); }
  catch (e) {
    $('pairError').textContent = t('pair.revokeFailed', { error: e.message });
    $('pairError').hidden = false;
  }
  refresh();
}

async function init() {
  await loadLanguage();
  translatePage();
  wireLanguageSelect($('langSelect'));
  $('host').onchange = (e) => { selectedHost = e.target.value; render(); };
  $('revokeAll').onclick = () => {
    if (!info || !info.sessions.length) return;
    revoke('*');
  };

  setInterval(() => {
    const left = Math.max(0, Math.round((deadline - Date.now()) / 1000));
    $('countdown').textContent = t('pair.countdown', { n: left });
    if (left === 0) refresh();
  }, 1000);
  // Picks up a redeemed code (it rotates immediately) and newly paired devices.
  setInterval(refresh, 4000);
  refresh();
}

init();
