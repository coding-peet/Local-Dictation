R"PALAHTML(<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="referrer" content="no-referrer">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Note settings</title>
<style>
:root {
  color-scheme: light;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
  --page: #f6f4ef;
  --card: #fffdfa;
  --text: #22211f;
  --muted: #635e56;
  --line: #d9d4c9;
  --accent: #ef573f;
  --focus: #a93121;
  --danger: #9a2d36;
}
* { box-sizing: border-box; }
[hidden] { display: none !important; }
body { margin: 0; background: var(--page); color: var(--text); line-height: 1.5; }
main { width: min(100% - 2rem, 48rem); margin: 2rem auto 3rem; }
header, .row { display: flex; align-items: center; justify-content: space-between; gap: .75rem; }
header { margin-bottom: 1rem; }
h1, h2 { line-height: 1.2; margin: 0; }
h1 { font-size: clamp(2rem, 6vw, 3.5rem); font-weight: 820; letter-spacing: -.048em; }
h2 { font-size: 1.35rem; margin-bottom: .85rem; }
section { background: var(--card); padding: clamp(.875rem, 3.2vw, 1.4rem); border: 1px solid var(--line); border-radius: 1.5rem; margin-bottom: .875rem; }
#trash-disclosure summary { cursor: pointer; }
#trash-disclosure summary h2 { display: inline-block; margin: 0; }
#trash-disclosure[open] summary { margin-bottom: .85rem; }
#viewer { color: var(--focus); font-weight: 650; }
p { margin: .25rem 0 .75rem; }
.muted { color: var(--muted); }
#status { min-height: 1.5em; margin: .25rem 0 .85rem; }
#status[data-error="true"], .danger { color: var(--danger); }
form, fieldset, .fields { display: grid; gap: .75rem; }
fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
#bridge-address-form, #bridge-order { margin-top: .85rem; }
.fields { grid-template-columns: repeat(2, minmax(0, 1fr)); }
label { display: grid; gap: .3rem; min-width: 0; font-weight: 550; }
input, select, button { font: inherit; min-height: 2.65rem; border-radius: .55rem; }
input, select { color: var(--text); background: var(--card); border: 1px solid var(--line); padding: .48rem .62rem; min-width: 0; width: 100%; }
button { cursor: pointer; border-radius: 999px; font-weight: 650; background: var(--card); border: 1px solid var(--line); color: var(--text); padding: .42rem .8rem; }
button:hover { border-color: var(--focus); }
:where(a, button, input, select, summary):focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
:where(button, input):disabled { opacity: .6; cursor: default; }
.primary { background: var(--accent); border-color: var(--accent); color: var(--text); }
.actions { display: flex; flex-wrap: wrap; gap: .45rem; }
ul { margin: 0 0 .8rem; padding: 0; list-style: none; }
li { min-height: 3rem; border-bottom: 1px solid var(--line); padding: .35rem 0; }
li:last-child { border-bottom: 0; }
li span { overflow-wrap: anywhere; }
li .actions { flex-shrink: 0; }
dialog { width: min(27rem, 100% - 2rem); color: var(--text); background: var(--card); border: 1px solid var(--line); border-radius: .9rem; padding: 1.15rem; }
dialog::backdrop { background: #0009; }
.pair-code { font: bold 2rem/1.25 ui-monospace, monospace; letter-spacing: .12em; }
dialog .actions { justify-content: end; }
@media (max-width: 32rem) {
  main { margin-top: 1.1rem; }
  .fields { grid-template-columns: 1fr; }
}
</style>
</head>
<body>
<main>
<header>
  <div>
    <a id="viewer" href="/" hidden>Back to Note</a>
    <h1 id="settings-title">Note settings</h1>
    <div id="connection" class="muted"></div>
  </div>
  <button id="refresh" type="button">Refresh</button>
</header>
<p id="status" role="status" aria-live="polite"></p>

<section id="wifi">
  <h2>Wi-Fi networks</h2>
  <p id="wifi-unavailable" class="muted">Connect the Note and select Refresh to manage its Wi-Fi networks.</p>
  <p id="wifi-empty" class="muted" hidden>No saved networks.</p>
  <ul id="wifi-list"></ul>
  <form id="wifi-form">
    <fieldset id="wifi-fields" disabled>
      <div class="fields">
        <label>Network name (SSID) <input id="wifi-name" maxlength="32" required autocomplete="off" spellcheck="false"></label>
        <label>Password <input id="wifi-secret" type="password" maxlength="63" autocomplete="new-password" aria-describedby="wifi-password-hint"></label>
      </div>
      <p id="wifi-password-hint" class="muted" hidden>Leave the password blank when editing a network to keep its saved password.</p>
      <div class="actions"><button class="primary" type="submit">Save network</button></div>
    </fieldset>
  </form>
</section>

<section id="note" hidden>
  <h2>Note</h2>
  <form id="note-form">
    <div class="fields">
      <label>Name <input id="note-name" maxlength="10" required></label>
      <label>Language
        <select id="note-language">
          <option value="en">English</option><option value="de">Deutsch</option>
          <option value="sk">Slovenčina</option><option value="cs">Čeština</option>
          <option value="fr">Français</option><option value="es">Español</option>
          <option value="it">Italiano</option><option value="pl">Polski</option>
          <option value="nl">Nederlands</option><option value="pt">Português</option>
          <option value="ru">Русский</option><option value="uk">Українська</option>
        </select>
      </label>
    </div>
    <div class="actions"><button class="primary" type="submit">Save Note settings</button></div>
  </form>
</section>

<section id="pairing" hidden>
  <h2>Paired devices</h2>
  <p id="peer-empty" class="muted" hidden>No paired devices.</p>
  <ul id="peer-list"></ul>
  <p id="pair-help" class="muted">Follow the confirmation shown on the Note.</p>
  <div id="pair-actions" class="actions">
    <button id="pair-usb" type="button">Pair over USB</button>
    <button id="pair-ble" type="button">Pair over Bluetooth</button>
  </div>
  <button id="pair-enable" type="button" hidden>Enable Bluetooth pairing</button>
</section>

<section id="bridge" hidden>
  <h2>Bridge</h2>
  <form id="bridge-form" hidden>
    <div class="fields">
      <label>Bridge name <input id="bridge-name" maxlength="24"></label>
      <label>Weather location <input id="weather-location" maxlength="120" placeholder="City, country code"></label>
    </div>
    <div class="actions"><button class="primary" type="submit">Save Bridge settings</button></div>
  </form>
  <form id="bridge-address-form" hidden>
    <label>Fallback IPv4 address
      <input id="bridge-address" maxlength="15" inputmode="decimal" autocomplete="off" spellcheck="false" aria-describedby="bridge-address-hint">
    </label>
    <p id="bridge-address-hint" class="muted">Leave blank to use automatic discovery. A fallback address must be on the Note's private Wi-Fi network.</p>
    <div class="actions"><button class="primary" type="submit">Save address</button></div>
  </form>
  <div id="bridge-order" hidden>
    <p class="muted">The Note tries the last working Bridge first, then this order.</p>
    <ul id="bridge-list"></ul>
    <button id="save-order" class="primary" type="button">Save order</button>
  </div>
</section>

<section id="trash" hidden>
  <details id="trash-disclosure">
    <summary><h2>Trash</h2></summary>
    <p id="trash-count" class="muted" role="status">Reading Trash…</p>
    <ul id="trash-list"></ul>
    <div class="actions">
      <button id="trash-more" type="button" hidden>Show more</button>
      <button id="trash-empty" class="danger" type="button" hidden>Empty Trash…</button>
    </div>
    <form id="trash-retention-form">
      <label>Keep trashed recordings for this many days
        <input id="trash-days" type="number" min="1" max="3650" step="1" value="90" required>
      </label>
      <div class="actions"><button class="primary" type="submit">Save retention</button></div>
    </form>
  </details>
</section>

<section id="memories" hidden>
  <h2>Move all recordings to Trash</h2>
  <p>Move all recordings, their transcripts and associated memories to Trash on the Note.</p>
  <form id="move-all-form" action="/memories/delete-all" method="post">
    <label>Type MOVE ALL to confirm
      <input id="move-all-confirmation" name="confirmation" required pattern="MOVE ALL" autocomplete="off" spellcheck="false">
    </label>
    <div class="actions"><button class="danger" type="submit">Move all to Trash</button></div>
  </form>
</section>

<dialog id="trash-dialog" aria-labelledby="trash-dialog-title">
  <form id="trash-empty-form">
    <h2 id="trash-dialog-title">Empty Trash?</h2>
    <p>Trashed recordings, transcripts and associated memories will be permanently deleted from this Note.</p>
    <label>Type EMPTY TRASH to confirm
      <input id="trash-confirmation" required pattern="EMPTY TRASH" autocomplete="off" spellcheck="false">
    </label>
    <div class="actions">
      <button id="trash-cancel" type="button" autofocus>Cancel</button>
      <button class="danger" type="submit">Permanently delete</button>
    </div>
  </form>
</dialog>

<dialog id="pair-dialog" aria-labelledby="pair-title">
  <h2 id="pair-title">Pair Note</h2>
  <p id="pair-prompt"></p>
  <p id="pair-code" class="pair-code" aria-live="polite" hidden></p>
  <div class="actions">
    <button id="pair-cancel" type="button" autofocus>Cancel</button>
    <button id="pair-match" class="primary" type="button" hidden>Codes match</button>
  </div>
</dialog>
<dialog id="confirm" aria-labelledby="confirm-title">
  <form method="dialog">
    <h2 id="confirm-title">Remove?</h2>
    <p id="confirm-text"></p>
    <div class="actions">
      <button value="cancel" autofocus>Cancel</button>
      <button class="danger" value="remove">Remove</button>
    </div>
  </form>
</dialog>
</main>
<script>
'use strict';
const el = id => document.getElementById(id);
const endpoint = '/api/note-settings';
let csrf = '';
let busy = false;
let bridgeOrder = [];
let savedNetworks = [];
let keepsWifiPassword = false;
let trashCount = 0;
let trashIndex = 0;
let trashRead = false;
const trashPageSize = 5;

function status(message, error = false) {
  el('status').textContent = message;
  el('status').dataset.error = String(error);
}

async function request(op, fields) {
  const headers = { Accept: 'application/json', 'X-Pala-Settings': '1' };
  const options = { cache: 'no-store', headers };
  if (op) {
    headers['Content-Type'] = 'application/json';
    if (csrf) headers['X-Pala-CSRF'] = csrf;
    options.method = 'POST';
    options.body = JSON.stringify({ op, ...fields });
  }
  const response = await fetch(endpoint, options);
  if (!(response.headers.get('Content-Type') || '').includes('application/json')) {
    throw new Error('The settings service returned an unexpected response.');
  }
  const result = await response.json();
  if (typeof result.csrf === 'string') csrf = result.csrf;
  if (op && (!response.ok || !result.ok)) {
    throw new Error(result.error || 'The setting could not be saved.');
  }
  return result;
}

function button(label, action, danger = false) {
  const control = document.createElement('button');
  control.type = 'button';
  control.textContent = label;
  control.disabled = busy;
  if (danger) control.className = 'danger';
  control.addEventListener('click', action);
  return control;
}

function row(list, label, controls) {
  const item = document.createElement('li');
  item.className = 'row';
  const name = document.createElement('span');
  name.textContent = label;
  const actions = document.createElement('div');
  actions.className = 'actions';
  actions.append(...controls);
  item.append(name, actions);
  list.append(item);
}

function confirmRemoval(title, name, remove) {
  const dialog = el('confirm');
  el('confirm-title').textContent = title;
  el('confirm-text').textContent = name;
  dialog.returnValue = 'cancel';
  dialog.addEventListener('close', () => {
    if (dialog.returnValue === 'remove') remove();
  }, { once: true });
  dialog.showModal();
}

function drawBridgeOrder() {
  const list = el('bridge-list');
  list.replaceChildren();
  bridgeOrder.forEach((peer, index) => {
    const move = delta => {
      const other = index + delta;
      [bridgeOrder[index], bridgeOrder[other]] = [bridgeOrder[other], bridgeOrder[index]];
      drawBridgeOrder();
    };
    const controls = [];
    if (index > 0) controls.push(button('Move up', () => move(-1)));
    if (index + 1 < bridgeOrder.length) controls.push(button('Move down', () => move(1)));
    row(list, peer.name, controls);
  });
}

function show(data) {
  const caps = data.capabilities || {};
  const attached = Boolean(data.ok && data.device);
  const wifiAvailable = attached && Array.isArray(data.wifi);
  const wifi = wifiAvailable ? data.wifi : [];
  const peers = Array.isArray(data.peers) ? data.peers : [];
  const canPair = caps.pair === true;
  const canManagePeers = attached && caps.peers !== false && Array.isArray(data.peers);
  const name = attached && typeof data.device.name === 'string' ? data.device.name.trim() : '';
  const title = name ? name + ' settings' : 'Note settings';
  document.title = title;
  el('settings-title').textContent = title;
  el('viewer').textContent = 'Back to ' + (name || 'Note');
  el('connection').textContent = 'Note disconnected';
  if (attached) el('connection').textContent = data.transport ? 'Connected via ' + data.transport : 'Connected';
  el('note').hidden = !attached;
  savedNetworks = wifi.map(network => network.ssid);
  keepsWifiPassword = wifiAvailable && data.device.keepsWifiPassword === true;
  el('wifi-password-hint').hidden = !keepsWifiPassword;
  el('wifi-fields').disabled = !wifiAvailable;
  el('wifi-unavailable').hidden = wifiAvailable;
  const devicePortal = attached && caps.devicePortal === true;
  const trashAvailable = attached && data.device.memoryTrash === true;
  el('viewer').hidden = !devicePortal;
  el('trash').hidden = !trashAvailable;
  el('memories').hidden = !devicePortal || !trashAvailable;
  if (!trashAvailable) {
    trashCount = trashIndex = 0;
    el('trash-list').replaceChildren();
    el('trash-more').hidden = true;
    el('trash-empty').hidden = true;
  }
  const canEnableBle = caps.enableBlePairing === true;
  el('pairing').hidden = !canPair && !canManagePeers && !canEnableBle;
  el('pair-enable').hidden = !canEnableBle;
  el('pair-actions').hidden = !canPair;
  el('pair-usb').hidden = caps.pairUsb === false;
  el('pair-ble').hidden = caps.pairBle === false;
  el('pair-help').hidden = !canPair;
  el('pair-help').textContent = attached ? 'Follow the confirmation shown on the Note.'
    : 'To connect over Bluetooth, briefly press both buttons on the Note, then select Refresh.';
  el('peer-empty').hidden = !canManagePeers || peers.length > 0;
  el('wifi-empty').hidden = !wifiAvailable || wifi.length > 0;
  if (attached) {
    el('note-name').value = name;
    el('note-language').value = data.device.language || 'en';
  }
  el('wifi-list').replaceChildren();
  wifi.forEach(({ ssid }) => row(el('wifi-list'), ssid, [
    button('Edit', () => {
      el('wifi-name').value = ssid;
      el('wifi-secret').value = '';
      el('wifi-secret').focus();
    }),
    button('Remove', () => confirmRemoval('Remove Wi-Fi network?', ssid,
      () => save('wifi.remove', { ssid }, 'Network removed.')), true)
  ]));
  el('peer-list').replaceChildren();
  if (canManagePeers) peers.forEach(({ id, name }) => row(el('peer-list'), name, [
    button('Remove', () => confirmRemoval('Remove paired device?', name,
      () => save('peers.remove', { id }, 'Pairing removed.')), true)
  ]));
  const bridge = data.bridge;
  el('bridge-form').hidden = !bridge;
  if (bridge) {
    el('bridge-name').value = bridge.name || '';
    el('weather-location').value = bridge.weatherLocation || '';
  }
  bridgeOrder = Array.isArray(data.bridgeOrder) ? data.bridgeOrder.map(id => peers.find(peer => peer.id === id)).filter(Boolean) : [];
  const hasOrder = caps.bridgeOrder === true && bridgeOrder.length > 1;
  el('bridge-order').hidden = !hasOrder;
  const hasAddress = attached && typeof data.device.bridgeAddress === 'string';
  el('bridge-address-form').hidden = !hasAddress;
  if (hasAddress) el('bridge-address').value = data.device.bridgeAddress;
  el('bridge').hidden = !bridge && !hasOrder && !hasAddress;
  drawBridgeOrder();
}

function trashLabel(entry) {
  const parts = ['Note ' + entry.num];
  if (entry.tag) parts.push(entry.tag);
  const captured = entry.capturedUtc ? new Date(entry.capturedUtc) : null;
  if (captured && Number.isFinite(captured.getTime())) parts.push(captured.toLocaleString());
  const trashed = entry.trashedAt > 0 ? new Date(entry.trashedAt * 1000) : null;
  if (trashed && Number.isFinite(trashed.getTime())) parts.push('Moved ' + trashed.toLocaleString());
  return parts.join(' · ');
}

function trashIsOpen() {
  return !el('trash').hidden && el('trash-disclosure').open;
}

function openLinkedTrash() {
  if (el('trash').hidden || location.hash !== '#trash') return;
  el('trash-disclosure').open = true;
  el('trash').scrollIntoView();
}

function readOpenedTrash() {
  if (!busy && trashIsOpen() && !trashRead) return run(readTrash);
}

async function appendTrashPage() {
  const end = Math.min(trashIndex + trashPageSize, trashCount);
  while (trashIsOpen() && trashIndex < end) {
    const { entry } = await request('trash.entry', { index: trashIndex });
    row(el('trash-list'), trashLabel(entry), [
      button('Restore', () => changeTrash('trash.restore', { num: entry.num }, 'Recording restored.'))
    ]);
    trashIndex += 1;
  }
  el('trash-more').hidden = trashIndex >= trashCount;
}

async function readTrash() {
  if (!trashIsOpen()) return;
  trashRead = true;
  trashCount = trashIndex = 0;
  el('trash-list').replaceChildren();
  el('trash-more').hidden = true;
  el('trash-empty').hidden = true;
  el('trash-count').textContent = 'Reading Trash…';
  try {
    const trash = await request('trash.status', {});
    trashCount = trash.count;
    el('trash-days').value = String(trash.days);
    el('trash-count').textContent = trashCount === 0 ? 'No recordings in Trash.' :
      trashCount === 1 ? '1 recording in Trash.' : trashCount + ' recordings in Trash.';
    el('trash-empty').hidden = trashCount === 0;
    await appendTrashPage();
  } catch (error) {
    el('trash-count').textContent = 'Could not read Trash. Select Refresh to retry.';
    throw error;
  }
}

function changeTrash(op, fields, message) {
  return run(async () => {
    await request(op, fields);
    try {
      await readTrash();
      status(message);
    } catch {
      status(message + ' Select Refresh to read Trash.');
    }
  });
}

function emptyTrash() {
  return run(async () => {
    const { count } = await request('trash.status', {});
    status('Emptying Trash…');
    try {
      for (let batch = 0; batch <= count; batch += 1) {
        const { more } = await request('trash.empty', { confirmation: 'EMPTY TRASH' });
        if (more === false) {
          try {
            await readTrash();
            status('Trash emptied.');
          } catch {
            status('Trash emptied. Select Refresh to read its current state.');
          }
          return;
        }
        if (more !== true) break;
      }
      throw new Error('Trash did not finish emptying. Select Refresh to see what remains.');
    } catch (error) {
      await readTrash().catch(() => {});
      throw new Error((error.message || 'Trash could not be emptied.') + ' Check the remaining recordings in Trash.');
    }
  });
}

async function showSettings(data) {
  const wasHidden = el('trash').hidden;
  show(data);
  if (el('trash').hidden) {
    el('trash-disclosure').open = false;
    trashRead = false;
    return;
  }
  if (wasHidden) openLinkedTrash();
  await readTrash();
}

async function run(action) {
  if (busy) return;
  busy = true;
  document.querySelectorAll('main > :not(dialog) button').forEach(control => { control.disabled = true; });
  el('status').dataset.error = 'false';
  try {
    await action();
  } catch (error) {
    status(error.message || 'Could not reach Note.', true);
  } finally {
    busy = false;
    document.querySelectorAll('main > :not(dialog) button').forEach(control => { control.disabled = false; });
    await readOpenedTrash();
  }
}

function refresh() {
  return run(async () => {
    status('Reading settings…');
    const data = await request();
    await showSettings(data);
    status(data.ok ? '' : data.error || 'Connect or pair the Note to continue.', !data.ok);
  });
}

function save(op, fields, message) {
  return run(async () => {
    status('Saving…');
    await request(op, fields);
    if (op === 'wifi.save') el('wifi-secret').value = '';
    try {
      await showSettings(await request());
      status(message);
    } catch {
      status(message + ' Use Refresh to read the current settings.');
    }
  });
}

function pair(transport) {
  return run(async () => {
    const dialog = el('pair-dialog');
    const prompt = el('pair-prompt');
    const code = el('pair-code');
    const match = el('pair-match');
    let ticket;
    let cancelled = false;
    let confirmed = false;
    let submitted = false;
    let completed = false;
    const cancel = () => { cancelled = true; };
    el('pair-cancel').onclick = cancel;
    match.onclick = () => { confirmed = true; match.disabled = true; };
    dialog.oncancel = event => { event.preventDefault(); cancel(); };
    code.hidden = true;
    match.hidden = true;
    match.disabled = false;
    prompt.textContent = transport === 'usb'
      ? 'Connect the Note with a data cable and follow the confirmation on its display.'
      : 'Briefly press both buttons on the Note to enable Bluetooth, then compare the codes.';
    dialog.showModal();
    status('');
    try {
      const started = await request('pair.start', { transport });
      ticket = started.ticket;
      if (typeof ticket !== 'string' || !ticket) throw new Error('The Bridge did not start pairing.');
      const deadline = Date.now() + 100000;
      while (Date.now() < deadline) {
        if (cancelled) return status('Pairing cancellation requested.');
        if (confirmed && !submitted) {
          submitted = true;
          await request('pair.confirm', { ticket, accepted: true });
          code.hidden = true;
          match.hidden = true;
          prompt.textContent = 'Confirm on the Note if prompted.';
        }
        const result = await request('pair.status', { ticket });
        if (result.state === 'paired') {
          completed = true;
          try { await showSettings(await request()); } catch { /* Pairing has already been saved. */ }
          return status('Pairing added.');
        }
        if (result.state === 'cancelled') return status('Pairing cancelled.');
        if (result.state === 'failed') throw new Error(result.error || 'Pairing failed.');
        if (result.state === 'confirm' && !submitted) {
          if (typeof result.code !== 'string' || !/^\d{6}$/.test(result.code)) {
            throw new Error('The Bridge did not return a valid comparison code.');
          }
          prompt.textContent = 'Confirm only if this code matches the code on the Note.';
          code.textContent = result.code;
          code.hidden = false;
          match.hidden = false;
        }
        await new Promise(resolve => setTimeout(resolve, 600));
      }
      throw new Error('Pairing timed out.');
    } finally {
      dialog.close();
      if (ticket && !completed) {
        await request('pair.confirm', { ticket, accepted: false }).catch(() => {});
      }
    }
  });
}

el('refresh').addEventListener('click', refresh);
el('note-form').addEventListener('submit', event => {
  event.preventDefault();
  save('settings.save', { name: el('note-name').value.trim(), language: el('note-language').value }, 'Note settings saved.');
});
el('wifi-form').addEventListener('submit', event => {
  event.preventDefault();
  const ssid = el('wifi-name').value;
  const password = el('wifi-secret').value;
  if (!password && !keepsWifiPassword && savedNetworks.includes(ssid)) {
    status('Enter the password to update this saved network.', true);
    el('wifi-secret').focus();
    return;
  }
  save('wifi.save', { ssid, password }, 'Network saved.');
});
el('bridge-form').addEventListener('submit', event => {
  event.preventDefault();
  save('bridge.save', { name: el('bridge-name').value.trim(), weatherLocation: el('weather-location').value.trim() }, 'Bridge settings saved.');
});
el('bridge-address-form').addEventListener('submit', event => {
  event.preventDefault();
  save('bridge.address', { address: el('bridge-address').value.trim() }, 'Bridge address saved.');
});
el('move-all-form').addEventListener('submit', event => {
  if (el('memories').hidden || el('move-all-confirmation').value !== 'MOVE ALL') event.preventDefault();
});
window.addEventListener('hashchange', openLinkedTrash);
el('trash-disclosure').addEventListener('toggle', () => {
  if (!trashIsOpen()) {
    trashRead = false;
    return;
  }
  return readOpenedTrash();
});
el('trash-more').addEventListener('click', () => run(appendTrashPage));
el('trash-retention-form').addEventListener('submit', event => {
  event.preventDefault();
  if (trashIsOpen()) changeTrash('trash.retention', { days: el('trash-days').valueAsNumber }, 'Trash retention saved.');
});
el('trash-empty').addEventListener('click', () => {
  el('trash-confirmation').value = '';
  el('trash-dialog').showModal();
});
el('trash-cancel').addEventListener('click', () => el('trash-dialog').close());
el('trash-empty-form').addEventListener('submit', event => {
  event.preventDefault();
  if (!trashIsOpen() || el('trash-confirmation').value !== 'EMPTY TRASH') return;
  el('trash-dialog').close();
  emptyTrash();
});
el('save-order').addEventListener('click', () => save('bridge.order', { ids: bridgeOrder.map(peer => peer.id) }, 'Bridge order saved.'));
el('pair-usb').addEventListener('click', () => pair('usb'));
el('pair-ble').addEventListener('click', () => pair('ble'));
el('pair-enable').addEventListener('click', () => save('pair.enableBle', {}, 'Bluetooth pairing is enabled for 90 seconds.'));
refresh();
</script>
</body>
</html>
)PALAHTML"
