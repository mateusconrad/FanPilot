// Pairing console (served to loopback only). Renders the one-time QR and manages paired devices and the integration
// tokens (item 23).
import { t, dateTime, loadLanguage, translatePage, wireLanguageSelect } from './js/i18n.mjs';

const $ = (id) => document.getElementById(id);
let info = null;
let deadline = 0;
let selectedHost = null;
let tokensShown = null; // the token list last drawn: redrawn only when it changes, not every 4 s under the pointer
let newTokenId = null;  // the token whose value is on screen, the one time it is

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

function renderTokens() {
  const key = JSON.stringify(info.tokens);
  if (key === tokensShown) return;
  tokensShown = key;
  const list = $('tokens');
  list.innerHTML = '';
  if (!info.tokens.length) {
    list.innerHTML = '<p class="hint" style="margin:0"></p>';
    list.firstChild.textContent = t('pair.tokens.none');
  }
  info.tokens.forEach((tk) => {
    const row = document.createElement('div');
    row.className = 'session';
    const left = document.createElement('div');
    left.innerHTML = '<div class="token-name"></div><div class="ua"></div>';
    left.firstChild.textContent = tk.name;
    left.lastChild.textContent = t('pair.tokens.meta', {
      created: dateTime(tk.created),
      used: tk.lastUsed ? t('pair.tokens.used', { when: dateTime(tk.lastUsed) }) : t('pair.tokens.unused'),
    });
    const btn = document.createElement('button');
    btn.className = 'btn';
    btn.textContent = t('pair.tokens.revoke');
    btn.setAttribute('aria-label', t('pair.tokens.revoke') + ': ' + tk.name);
    btn.onclick = () => revokeToken(tk.id);
    row.append(left, btn);
    list.appendChild(row);
  });
}

function tokenError(e) {
  $('tokenError').textContent = t('pair.tokens.failed', { error: e.message });
  $('tokenError').hidden = false;
}

// The value comes back this once: it stays on screen until the page closes or the token is revoked.
async function createToken(ev) {
  ev.preventDefault();
  $('tokenError').hidden = true;
  try {
    const made = await api('/api/pair/tokens', { name: $('tokenName').value });
    newTokenId = made.id;
    $('tokenValue').textContent = made.token;
    $('tokenCopy').textContent = t('pair.tokens.copy');
    $('tokenNew').hidden = false;
    $('tokenName').value = '';
  } catch (e) { tokenError(e); }
  refresh();
}

async function revokeToken(id) {
  $('tokenError').hidden = true;
  try {
    await api('/api/pair/tokens/revoke', { id });
    if (id === newTokenId) { $('tokenNew').hidden = true; $('tokenValue').textContent = ''; newTokenId = null; }
  } catch (e) { tokenError(e); }
  refresh();
}

async function copyToken() {
  try {
    await navigator.clipboard.writeText($('tokenValue').textContent);
    $('tokenCopy').textContent = t('pair.tokens.copied');
  } catch (e) {
    // No clipboard (an old browser): the value is selected for Ctrl+C.
    getSelection().selectAllChildren($('tokenValue'));
  }
}

async function refresh() {
  try {
    info = await api('/api/pair/info');
    deadline = Date.now() + info.expiresIn * 1000;
    render();
    renderTokens();
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
  $('tokenForm').onsubmit = createToken;
  $('tokenCopy').onclick = copyToken;

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
