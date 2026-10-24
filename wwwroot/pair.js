// Pairing console (served to loopback only). Renders the one-time QR and manages paired devices.
(function () {
  const $ = (id) => document.getElementById(id);
  let info = null;
  let deadline = 0;
  let selectedHost = null;

  async function api(path, body) {
    const opts = body === undefined ? {} : {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-FanPilot': '1' }, body: JSON.stringify(body)
    };
    const r = await fetch(path, opts);
    if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || r.statusText);
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
    if (!url) { $('qr').textContent = 'Nenhuma interface de rede encontrada.'; return; }
    const qr = qrcode(0, 'M');
    qr.addData(url);
    qr.make();
    $('qr').innerHTML = qr.createSvgTag({ cellSize: 6, margin: 2, scalable: true });
    $('url').textContent = url.replace(/c=.*/, 'c=…');

    const list = $('sessions');
    list.innerHTML = '';
    if (!info.sessions.length) list.innerHTML = '<p class="hint" style="margin:0">Nenhum aparelho pareado.</p>';
    info.sessions.forEach((s) => {
      const row = document.createElement('div');
      row.className = 'session';
      const left = document.createElement('div');
      left.innerHTML = '<div></div><div class="ua"></div>';
      left.firstChild.textContent = s.ip + ' · visto ' + new Date(s.lastSeen).toLocaleString('pt-BR');
      left.lastChild.textContent = s.userAgent;
      const btn = document.createElement('button');
      btn.className = 'btn';
      btn.textContent = 'Desparear';
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
      $('qr').textContent = 'Erro: ' + e.message;
    }
  }

  // A failure must say so: otherwise the phone looks unpaired while it still has access.
  async function revoke(id) {
    $('pairError').hidden = true;
    try { await api('/api/pair/revoke', { id }); }
    catch (e) {
      $('pairError').textContent = 'Não foi possível desparear: ' + e.message;
      $('pairError').hidden = false;
    }
    refresh();
  }

  $('host').onchange = (e) => { selectedHost = e.target.value; render(); };
  $('revokeAll').onclick = () => {
    if (!info || !info.sessions.length) return;
    revoke('*');
  };

  setInterval(() => {
    const left = Math.max(0, Math.round((deadline - Date.now()) / 1000));
    $('countdown').textContent = 'novo código em ' + left + ' s';
    if (left === 0) refresh();
  }, 1000);
  // Picks up a redeemed code (it rotates immediately) and newly paired devices.
  setInterval(refresh, 4000);
  refresh();
})();
