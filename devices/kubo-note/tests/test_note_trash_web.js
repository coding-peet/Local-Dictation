const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const settings = fs.readFileSync(path.join(__dirname, '..', 'pala_note', 'src', 'app', 'note_settings_page.inc'), 'utf8');
const script = settings.split('<script>')[1].split('</script>')[0].replace(/\brefresh\(\);\s*$/, '');

class Element {
  constructor(tag = '') {
    this.tag = tag;
    this.listeners = new Map();
    this.children = [];
    this.dataset = {};
    this.value = '';
  }
  addEventListener(type, handler) {
    const handlers = this.listeners.get(type) ?? [];
    handlers.push(handler);
    this.listeners.set(type, handlers);
  }
  dispatch(type, event = {}) {
    return Promise.all((this.listeners.get(type) ?? []).map(handler => handler(event)));
  }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  showModal() { this.open = true; }
  close() { this.open = false; }
  scrollIntoView() { this.scrolled = true; }
}

function fixture({ memoryTrash, transport = 'BLE', portal = false, count = 0, hash = '', name = 'Willow', displayPages, sleepMinutes, syncTiming, pageWrap, preferBluetooth, bridge, recorderFirst, webPasswordConfigured, webPasswordRequired, weeklyCodexLimit, wifiEnabled, bluetoothEnabled, health, storageAudit, confirmation, locked = false } = {}) {
  const elements = new Map();
  const buttons = [];
  for (const match of settings.matchAll(/<([a-z][a-z0-9]*)\b[^<>]*\bid="([^"]+)"[^<>]*>/g)) {
    const element = new Element(match[1]);
    element.hidden = /\shidden[\s>]/.test(match[0]);
    element.type = /\btype="([^"]+)"/.exec(match[0])?.[1] || '';
    if (element.tag === 'button') buttons.push(element);
    elements.set(match[2], element);
  }

  let connected = true;
  let targetKey = 'note-a';
  let writePermission = 'synthetic-nonce';
  let retention = 90;
  let trash = Array.from({ length: count }, (_, index) =>
    ({ num: index + 1, tag: 'Recording', capturedUtc: '', trashedAt: 0 }));
  const calls = [];
  const timers = [];
  const context = vm.createContext({
    setTimeout: callback => timers.push(callback),
    location: { hash },
    window: new Element(),
    document: {
      getElementById: id => elements.get(id),
      createElement: tag => {
        const element = new Element(tag);
        if (tag === 'button') buttons.push(element);
        return element;
      },
      querySelectorAll: () => buttons,
    },
    fetch: async (_url, options) => {
      assert.equal(options.headers['X-Pala-Settings'], '1');
      const data = options.body ? JSON.parse(options.body) : { op: 'get' };
      if (options.body && options.headers['X-Pala-CSRF'] !== writePermission) {
        return { ok: false, headers: { get: () => 'application/json' },
          json: async () => ({ ok: false, error: 'Refresh the Note settings page.' }) };
      }
      calls.push(data);
      let reply = { ok: true };
      switch (data.op) {
        case 'get':
          if (locked && connected) {
            reply = { ok: false, access: 'locked', transport, csrf: writePermission, bridge,
              capabilities: { pair: true, enableBlePairing: true }, error: 'Enter the Note password.' };
            break;
          }
          reply = {
            ...reply, ok: connected, csrf: writePermission, transport, capabilities: { devicePortal: portal },
            targetKey: connected ? targetKey : undefined,
            device: connected ? { name, language: 'en', memoryTrash, displayPages, sleepMinutes, syncTiming,
              pageWrap, preferBluetooth, recorderFirst, webPasswordConfigured, webPasswordRequired, weeklyCodexLimit, wifiEnabled, bluetoothEnabled,
              r: storageAudit ? 1 : undefined, ...health } : undefined,
            wifi: [], peers: [], bridge,
          };
          break;
        case 'auth.login':
          if (data.password === 'correct-fixture-password') {
            locked = false;
            reply.session = 'fixture-session';
          } else {
            reply = { ok: false, access: 'locked', transport, error: 'Incorrect password.' };
          }
          break;
        case 'auth.logout':
          locked = true;
          break;
        case 'bridge.save':
          bridge = { name: data.name, weatherLocation: data.weatherLocation, holidayCountry: data.holidayCountry };
          break;
        case 'settings.save':
          name = data.name;
          if ('weeklyCodexLimit' in data) weeklyCodexLimit = data.weeklyCodexLimit;
          break;
        case 'auth.policy':
          webPasswordRequired = data.required;
          break;
        case 'auth.save':
          webPasswordConfigured = true;
          break;
        case 'sleep.save':
          sleepMinutes = data.minutes;
          break;
        case 'sync.save':
          syncTiming = data.mode;
          break;
        case 'pages.save':
          displayPages = data.pages;
          if ('wrap' in data) pageWrap = data.wrap;
          break;
        case 'wireless.save':
          preferBluetooth = data.preferBluetooth;
          break;
        case 'radios.save':
          wifiEnabled = data.wifiEnabled;
          bluetoothEnabled = data.bluetoothEnabled;
          break;
        case 'restart.status':
          reply.storageAudit = storageAudit;
          if (confirmation) reply.confirmation = confirmation;
          break;
        case 'restart.request':
          reply.pending = true;
          if (confirmation) reply.ticket = confirmation.ticket;
          break;
        case 'trash.status':
          reply = { ...reply, days: retention, count: trash.length };
          break;
        case 'trash.entry':
          reply = { ...reply, entry: trash[data.index] };
          break;
        case 'trash.restore':
          trash = trash.filter(entry => entry.num !== data.num);
          break;
        case 'trash.retention':
          retention = data.days;
          break;
        case 'trash.empty':
          assert.equal(data.confirmation, 'EMPTY TRASH');
          trash.shift();
          reply.more = trash.length > 0;
          break;
        default:
          throw new Error('Unexpected fixture operation: ' + data.op);
      }
      return { ok: true, headers: { get: () => 'application/json' }, json: async () => reply };
    },
  });
  vm.runInContext(script, context);
  const el = id => elements.get(id);
  return {
    calls, el,
    title: () => vm.runInContext('document.title', context),
    disconnect() { connected = false; },
    selectOtherNote() { targetKey = 'note-b'; },
    expireWritePermission() { writePermission = 'renewed-nonce'; },
    refresh: () => vm.runInContext('refresh()', context),
    runTimer: () => timers.shift()(),
    async setTrashOpen(open) {
      el('trash-disclosure').open = open;
      await el('trash-disclosure').dispatch('toggle');
    },
    async dispatch(id, event) {
      let prevented = false;
      await el(id).dispatch(event, { preventDefault: () => { prevented = true; } });
      while (vm.runInContext('busy', context)) await new Promise(resolve => setImmediate(resolve));
      return prevented;
    },
  };
}

test('BLE lock exposes only password authentication until successful sign-in', async () => {
  const page = fixture({ locked: true, bridge: { name: 'Private Bridge' }, wifiEnabled: true,
    bluetoothEnabled: true, preferBluetooth: false, webPasswordConfigured: true });
  assert.equal(page.el('settings-content').hidden, true);
  await page.refresh();
  assert.equal(page.el('access-login').hidden, false);
  assert.equal(page.el('refresh').hidden, true);
  assert.equal(page.el('settings-content').hidden, true);
  assert.notEqual(page.el('bridge-name').value, 'Private Bridge');
  page.el('access-password').value = 'incorrect-fixture-password';
  await page.dispatch('access-login-form', 'submit');
  assert.equal(page.el('settings-content').hidden, true);
  page.el('access-password').value = 'correct-fixture-password';
  const signingIn = page.dispatch('access-login-form', 'submit');
  assert.equal(page.el('status').textContent, 'Signing in…');
  await signingIn;
  assert.equal(page.el('status').textContent, 'Signed in.');
  assert.equal(page.el('access-login').hidden, true);
  assert.equal(page.el('refresh').hidden, false);
  assert.equal(page.el('settings-content').hidden, false);
  assert.equal(page.el('wireless-radios').hidden, false);
  assert.equal(page.el('wireless-priority').hidden, false);
  assert.equal(page.el('wifi').hidden, false);
  await page.dispatch('note-password-logout', 'click');
  assert.equal(page.el('settings-content').hidden, true);
  page.disconnect();
  await page.refresh();
  assert.equal(page.el('settings-content').hidden, true);
});

test('Note settings show live battery and heap peaks and clear them on disconnect', async () => {
  const page = fixture({ health: { batteryPercent: 0, ramUsage: [42, 70], psramUsage: [17, 35] } });
  await page.refresh();
  assert.equal(page.el('note-health').hidden, false);
  assert.equal(page.el('note-battery').textContent, '0%');
  assert.equal(page.el('note-ram').textContent, '42% current · 70% peak since restart');
  assert.equal(page.el('note-psram').textContent, '17% current · 35% peak since restart');
  page.disconnect();
  await page.refresh();
  assert.equal(page.el('note-health').hidden, true);
  assert.equal(page.el('note-ram').textContent, '');
});

test('restart choices report interrupted audit and only request a local confirmation', async () => {
  const page = fixture({ storageAudit: { state: 'incomplete', notes: 50_000, estimateSeconds: 500 } });
  await page.refresh();
  assert.equal(page.el('restart-note').hidden, false);
  assert.match(page.el('restart-status').textContent, /did not finish/);
  assert.match(page.el('restart-estimate').textContent, /50,000 cataloged notes.*9 min/);
  await page.dispatch('restart-normal', 'click');
  await page.dispatch('restart-audit', 'click');
  assert.deepEqual(page.calls.filter(call => call.op.startsWith('restart.')), [
    { op: 'restart.status' },
    { op: 'restart.request', mode: 'restart' },
    { op: 'restart.request', mode: 'audit' },
  ]);
  assert.match(page.el('restart-status').textContent, /Confirm or cancel on the Note display/);
  page.disconnect();
  await page.refresh();
  assert.equal(page.el('restart-note').hidden, true);
});

test('expired and cancelled restart requests show their actual outcome', async () => {
  for (const [state, message] of [['expired', /expired/], ['cancelled', /cancelled/]]) {
    const page = fixture({ storageAudit: { state: 'never', notes: 4 }, confirmation: { ticket: 7, state } });
    await page.refresh();
    await page.dispatch('restart-normal', 'click');
    await page.runTimer();
    assert.match(page.el('restart-status').textContent, message);
  }
});

test('wireless controls, sync connection priority and network list are grouped in that order', () => {
  const sections = [...settings.matchAll(/<section id="([^"]+)"/g)].map(match => match[1]);
  const position = sections.indexOf('wireless-radios');
  assert.deepEqual(sections.slice(position, position + 3), ['wireless-radios', 'wireless-priority', 'wifi']);
});

test('unsaved display edits stay through another form save and use the saved Note name', async () => {
  const page = fixture({ name: 'Wren-8', displayPages: 'TECNM', pageWrap: true });
  const notice = id => page.el(id).children.find(child => child.className === 'unsaved');
  await page.refresh();
  const list = page.el('display-pages-list');
  list.children[0].children[1].children[0].dispatch('click');
  assert.equal(notice('display-pages-form').textContent, 'Changes are not saved to Wren-8. Select Save.');
  page.el('note-name').value = 'Fern-2';
  await page.dispatch('note-form', 'input');
  assert.match(notice('note-form').textContent, /Wren-8/);
  await page.dispatch('note-form', 'submit');
  assert.equal(notice('note-form').hidden, true);
  assert.match(notice('display-pages-form').textContent, /Fern-2/);
  assert.equal(list.children[1].children[0].children[1], 'Month after next');
  await page.dispatch('display-pages-form', 'submit');
  assert.equal(notice('display-pages-form').hidden, true);
  assert.equal(page.calls.find(call => call.op === 'pages.save').pages, 'TECMN');
  page.el('page-wrap').checked = false;
  await page.dispatch('display-pages-form', 'change');
  assert.equal(notice('display-pages-form').hidden, false);
  page.el('page-wrap').checked = true;
  await page.dispatch('display-pages-form', 'change');
  assert.equal(notice('display-pages-form').hidden, true);
});

test('Note form saves the selected Codex limit and hides the selector on older firmware', async () => {
  for (const weeklyCodexLimit of [false, undefined]) {
    const page = fixture({ name: 'Kubo', weeklyCodexLimit });
    await page.refresh();
    assert.equal(page.el('note-codex-limit-row').hidden, weeklyCodexLimit === undefined);
    assert.equal(page.el('note-codex-limit').value, '5h');
    page.el('note-codex-limit').value = 'weekly';
    await page.dispatch('note-form', 'change');
    await page.dispatch('note-form', 'submit');
    const write = page.calls.find(call => call.op === 'settings.save');
    assert.equal(write.weeklyCodexLimit, weeklyCodexLimit === undefined ? undefined : true);
    assert.equal(page.el('note-codex-limit').value, weeklyCodexLimit === undefined ? '5h' : 'weekly');
  }
});

test('saved Note identity, not its name, separates drafts and password entries', async () => {
  const page = fixture({ name: 'Same name', displayPages: 'TECNM', webPasswordConfigured: false });
  await page.refresh();
  page.el('note-name').value = 'Unsaved';
  await page.dispatch('note-form', 'input');
  page.el('note-password-new').value = 'local test password';
  await page.dispatch('note-password-new-eye', 'click');
  page.selectOtherNote();
  await page.refresh();
  assert.equal(page.el('note-name').value, 'Same name');
  assert.equal(page.el('note-password-new').value, '');
  assert.equal(page.el('note-password-new').type, 'password');
});

test('new password fields highlight only an exact nonempty match', async () => {
  const page = fixture({ transport: 'USB', webPasswordConfigured: false });
  await page.refresh();
  const form = page.el('note-password-form');
  const password = page.el('note-password-new');
  const repeat = page.el('note-password-repeat');
  assert.notEqual(form.dataset.passwordsMatch, 'true');

  password.value = 'Modrý test';
  await page.dispatch('note-password-new', 'input');
  assert.notEqual(form.dataset.passwordsMatch, 'true');
  repeat.value = 'Modrý test';
  await page.dispatch('note-password-repeat', 'input');
  assert.equal(form.dataset.passwordsMatch, 'true');

  password.value = 'modrý test';
  await page.dispatch('note-password-new', 'input');
  assert.notEqual(form.dataset.passwordsMatch, 'true');
  repeat.value = 'modrý test';
  await page.dispatch('note-password-repeat', 'change');
  assert.equal(form.dataset.passwordsMatch, 'true');

  await page.dispatch('note-password-form', 'submit');
  assert.equal(password.value, '');
  assert.equal(repeat.value, '');
  assert.notEqual(form.dataset.passwordsMatch, 'true');
});

test('USB password save renews an expired permission and stays bound to the original Note', async () => {
  const password = 'synthetic password';
  for (const differentNote of [false, true]) {
    const page = fixture({ transport: 'USB', webPasswordConfigured: true });
    await page.refresh();
    page.el('note-password-new').value = page.el('note-password-repeat').value = password;
    await page.dispatch('note-password-form', 'input');
    page.expireWritePermission();
    if (differentNote) page.selectOtherNote();
    await page.dispatch('note-password-form', 'submit');

    const writes = page.calls.filter(call => call.op === 'auth.save');
    if (differentNote) {
      assert.equal(writes.length, 0);
      assert.match(page.el('status').textContent, /connection changed/);
    } else {
      assert.deepEqual(writes, [{ op: 'auth.save', old: '', password, repeat: password }]);
      assert.equal(page.el('status').textContent, 'Note password saved.');
    }
  }
});

test('Recorder shows delivery and hides legacy calendar and processing controls', async () => {
  const bridge = { processing: { mode: 'kubo', calendarId: 'example', tasksLoginConfigured: true } };
  const usb = fixture({ transport: 'USB', recorderFirst: true, bridge });
  await usb.refresh();
  assert.equal(usb.el('processing-label').textContent, 'Deliver transcripts');
  assert.equal(usb.el('legacy-processing-fields').hidden, true);
  assert.equal(usb.el('recorder-archive').hidden, true);
  assert.doesNotMatch(usb.el('kubo-google-status').textContent, /Calendar/);
  const portal = fixture({ transport: 'Wi-Fi', portal: true, recorderFirst: true });
  await portal.refresh();
  assert.equal(portal.el('recorder-archive').hidden, false);
});

test('password opt-out is visible over paired USB and wireless settings can show open status', async () => {
  const usb = fixture({ transport: 'USB', webPasswordConfigured: false, webPasswordRequired: true });
  await usb.refresh();
  assert.equal(usb.el('note-protection-form').hidden, false);
  assert.equal(usb.el('note-protection').checked, true);
  usb.el('note-protection').checked = false;
  await usb.dispatch('note-protection-form', 'change');
  await usb.dispatch('note-protection-form', 'submit');
  assert.deepEqual(usb.calls.find(call => call.op === 'auth.policy'), { op: 'auth.policy', required: false });
  assert.equal(usb.el('note-password-open').hidden, false);
  for (const transport of ['BLE', 'Wi-Fi']) {
    const wireless = fixture({ transport, webPasswordConfigured: false, webPasswordRequired: false });
    await wireless.refresh();
    assert.equal(wireless.el('note-protection-form').hidden, true);
    assert.equal(wireless.el('note-password-form').hidden, true);
    await wireless.dispatch('note-protection-form', 'submit');
    assert.equal(wireless.calls.some(call => call.op === 'auth.policy'), false);
  }
});

test('each password eye is independent and a weak supported password can be saved', async () => {
  const page = fixture({ transport: 'USB', webPasswordConfigured: false });
  await page.refresh();
  for (const id of ['access-password', 'note-password-old', 'note-password-new', 'note-password-repeat', 'wifi-secret']) {
    await page.dispatch(id + '-eye', 'click');
    assert.equal(page.el(id).type, 'text');
    assert.equal(page.el(id + '-eye').ariaPressed, 'true');
    await page.dispatch(id + '-eye', 'click');
    assert.equal(page.el(id).type, 'password');
  }
  for (const [password, label] of [['12345678', 'Weak'], ['Taco-62andTea', 'Fair'], ['Opera wanders beyond blue hills', 'Strong']]) {
    page.el('note-password-new').value = password;
    await page.dispatch('note-password-new', 'input');
    assert.equal(page.el('note-password-new-strength-label').textContent, label);
  }
  page.el('note-password-new').value = page.el('note-password-repeat').value = '12345678';
  await page.dispatch('note-password-form', 'input');
  await page.dispatch('note-password-form', 'submit');
  assert.deepEqual(page.calls.find(call => call.op === 'auth.save'),
    { op: 'auth.save', old: '', password: '12345678', repeat: '12345678' });
  assert.equal(page.el('note-password-new').value, '');
  assert.equal(page.el('note-password-new-strength').dataset.strength, '0');
});

test('holiday country defaults to Slovakia and persists independently of weather', async () => {
  const page = fixture({ bridge: { name: 'Desk Bridge', weatherLocation: '' } });
  await page.refresh();
  assert.equal(page.el('holiday-country').value, 'SK');
  assert.equal(page.el('weather-location').value, '');
  page.el('holiday-country').value = 'CZ';
  await page.dispatch('bridge-form', 'submit');
  assert.deepEqual(page.calls.find(call => call.op === 'bridge.save'), {
    op: 'bridge.save', name: 'Desk Bridge', weatherLocation: '', holidayCountry: 'CZ',
  });
  assert.equal(page.el('holiday-country').value, 'CZ');
});

test('settings uses the connected Note name and changes only after saving', async () => {
  const page = fixture({ name: 'Wren-8', portal: true, transport: 'Wi-Fi' });
  await page.refresh();
  assert.equal(page.title(), 'Wren-8 settings');
  assert.equal(page.el('settings-title').textContent, 'Wren-8 settings');
  assert.equal(page.el('viewer').textContent, 'Back to Wren-8');
  assert.match(page.el('connection').textContent, /Wi-Fi/);
  page.el('note-name').value = 'Fern-2';
  assert.equal(page.title(), 'Wren-8 settings');
  await page.dispatch('note-form', 'submit');
  assert.equal(page.title(), 'Fern-2 settings');
  assert.equal(page.el('settings-title').textContent, 'Fern-2 settings');
  assert.equal(page.el('viewer').textContent, 'Back to Fern-2');
  page.disconnect();
  await page.refresh();
  assert.equal(page.title(), 'Note settings');
  assert.equal(page.el('settings-title').textContent, 'Note settings');
  assert.equal(page.el('viewer').hidden, true);
  assert.equal(page.el('connection').textContent, 'Note disconnected');
});

test('loopback settings uses the Note name and the transport supplied by Bridge', async () => {
  for (const transport of ['USB', 'BLE', '']) {
    const page = fixture({ name: 'Swan', transport });
    await page.refresh();
    assert.equal(page.title(), 'Swan settings');
    assert.equal(page.el('viewer').hidden, true);
    assert.equal(page.el('connection').textContent, transport ? 'Connected via ' + transport : 'Connected');
  }
});

test('old Companion and loopback builds hide destructive controls', async () => {
  for (const testCase of [{ portal: true, transport: 'Wi-Fi' }, { transport: 'USB' }, { transport: 'BLE' }]) {
    const old = fixture(testCase);
    await old.refresh();
    assert.equal(old.el('trash').hidden, true);
    assert.equal(old.el('memories').hidden, true);
    assert.deepEqual(old.calls, [{ op: 'get' }]);
    old.el('move-all-confirmation').value = 'MOVE ALL';
    assert.equal(await old.dispatch('move-all-form', 'submit'), true);
  }
});

test('bulk move is a same-origin Companion form with exact confirmation', async () => {
  for (const transport of ['USB', 'BLE']) {
    const loopback = fixture({ memoryTrash: true, transport });
    await loopback.refresh();
    assert.equal(loopback.el('trash').hidden, false);
    assert.equal(loopback.el('memories').hidden, true);
  }
  const companion = fixture({ memoryTrash: true, portal: true, transport: 'Wi-Fi' });
  await companion.refresh();
  assert.equal(companion.el('trash').hidden, false);
  assert.equal(companion.el('memories').hidden, false);
  assert.match(settings, /id="move-all-form" action="\/memories\/delete-all" method="post"/);
  companion.el('move-all-confirmation').value = 'DELETE ALL';
  assert.equal(await companion.dispatch('move-all-form', 'submit'), true);
  companion.el('move-all-confirmation').value = 'MOVE ALL';
  assert.equal(await companion.dispatch('move-all-form', 'submit'), false);
});

test('Trash only reads while open, and its page link opens the disclosure', async () => {
  const page = fixture({ memoryTrash: true, count: 1 });
  await page.refresh();
  assert.deepEqual(page.calls, [{ op: 'get' }]);
  await page.setTrashOpen(true);
  assert.equal(page.calls.filter(call => call.op === 'trash.status').length, 1);
  await page.refresh();
  assert.equal(page.calls.filter(call => call.op === 'trash.status').length, 2);
  await page.setTrashOpen(false);
  await page.refresh();
  assert.equal(page.calls.filter(call => call.op === 'trash.status').length, 2);
  const linked = fixture({ memoryTrash: true, hash: '#trash', count: 1 });
  await linked.refresh();
  assert.equal(linked.el('trash-disclosure').open, true);
  assert.equal(linked.el('trash').scrolled, true);
  assert.equal(linked.calls.filter(call => call.op === 'trash.status').length, 1);
});

test('Trash lists a bounded page, restores by note number, and saves retention', async () => {
  const page = fixture({ memoryTrash: true, count: 7 });
  await page.refresh();
  await page.setTrashOpen(true);
  assert.equal(page.el('trash-days').value, '90');
  assert.deepEqual(page.calls.filter(call => call.op === 'trash.entry').map(call => call.index), [0, 1, 2, 3, 4]);
  assert.equal(page.el('trash-more').hidden, false);
  await page.dispatch('trash-more', 'click');
  assert.equal(page.el('trash-list').children.length, 7);
  assert.equal(page.el('trash-more').hidden, true);
  const restore = page.el('trash-list').children[0].children[1].children[0];
  await restore.dispatch('click');
  assert.deepEqual(page.calls.find(call => call.op === 'trash.restore'), { op: 'trash.restore', num: 1 });
  assert.match(page.el('trash-count').textContent, /^6 recordings/);
  page.el('trash-days').valueAsNumber = 17;
  await page.dispatch('trash-retention-form', 'submit');
  assert.equal(page.el('trash-days').value, '17');
  assert.deepEqual(page.calls.find(call => call.op === 'trash.retention'),
                   { op: 'trash.retention', days: 17 });
});

test('Empty Trash needs exact input and waits for the last batch', async () => {
  const page = fixture({ memoryTrash: true, count: 3 });
  await page.refresh();
  await page.setTrashOpen(true);
  await page.dispatch('trash-empty', 'click');
  assert.equal(page.el('trash-dialog').open, true);
  page.el('trash-confirmation').value = 'EMPTY';
  await page.dispatch('trash-empty-form', 'submit');
  assert.equal(page.el('trash-dialog').open, true);
  assert.equal(page.calls.filter(call => call.op === 'trash.empty').length, 0);
  page.el('trash-confirmation').value = 'EMPTY TRASH';
  await page.dispatch('trash-empty-form', 'submit');
  assert.equal(page.el('trash-dialog').open, false);
  assert.equal(page.calls.filter(call => call.op === 'trash.empty').length, 3);
  assert.equal(page.el('trash-count').textContent, 'No recordings in Trash.');
  assert.equal(page.el('status').textContent, 'Trash emptied.');
});

test('display pages run upwards from fixed Home and save that order', async () => {
  assert.match(settings, /<ul id="display-pages-list"><\/ul>\s*<div class="row display-home"><strong>Home<\/strong><span class="muted">Always on bottom<\/span>/);
  const page = fixture({ displayPages: 'TECN' });
  await page.refresh();
  assert.equal(page.el('display-pages').hidden, false);
  const list = page.el('display-pages-list');
  const [next, current, events, tasks] = list.children;
  assert.deepEqual(list.children.map(item => item.children[0].children[1]),
    ['Next month', 'Current month', 'Events', 'Tasks']);
  assert.deepEqual(next.children[1].children.map(control => control.textContent), ['Move down']);
  assert.deepEqual(tasks.children[1].children.map(control => control.textContent), ['Move up']);
  const currentToggle = current.children[0].children[0];
  const tasksToggle = tasks.children[0].children[0];
  const eventsToggle = events.children[0].children[0];
  currentToggle.checked = false;
  currentToggle.dispatch('change');
  tasksToggle.checked = false;
  tasksToggle.dispatch('change');
  eventsToggle.checked = false;
  eventsToggle.dispatch('change');
  for (let index = 0; index < 3; index++) {
    const controls = list.children[index].children[1].children;
    controls.find(control => control.textContent === 'Move down').dispatch('click');
  }
  assert.deepEqual(page.calls, [{ op: 'get' }]);

  const saving = page.dispatch('display-pages-form', 'submit');
  assert.equal(page.el('display-pages-fields')?.disabled, true);
  await saving;
  assert.equal(page.el('display-pages-fields').disabled, false);
  assert.deepEqual(page.calls, [{ op: 'get' }, { op: 'pages.save', pages: 'Ntec' }, { op: 'get' }]);
  assert.deepEqual(list.children.map(item => item.children[0].children[1]),
    ['Current month', 'Events', 'Tasks', 'Next month']);
  assert.deepEqual(list.children.map(item => item.children[0].children[0].checked), [false, false, false, true]);
});

test('older firmware hides the display pages section', async () => {
  const page = fixture();
  await page.refresh();
  assert.equal(page.el('display-pages').hidden, true);
  assert.deepEqual(page.calls, [{ op: 'get' }]);
});

test('the third month can be enabled and reordered alongside other pages', async () => {
  const page = fixture({ displayPages: 'TECNm' });
  await page.refresh();
  const list = page.el('display-pages-list');
  const monthAfterNext = list.children[0];
  assert.equal(monthAfterNext.children[0].children[1], 'Month after next');
  const checkbox = monthAfterNext.children[0].children[0];
  assert.equal(checkbox.checked, false);
  checkbox.checked = true;
  checkbox.dispatch('change');
  monthAfterNext.children[1].children[0].dispatch('click');
  list.children[1].children[1].children.find(control => control.textContent === 'Move up').dispatch('click');
  assert.equal(list.children[0].children[0].children[1], 'Month after next');
  list.children[0].children[1].children[0].dispatch('click');
  await page.dispatch('display-pages-form', 'submit');
  assert.deepEqual(page.calls, [{ op: 'get' }, { op: 'pages.save', pages: 'TECMN' }, { op: 'get' }]);
  assert.equal(list.children[1].children[0].children[1], 'Month after next');
  assert.equal(list.children[1].children[0].children[0].checked, true);
});

test('sleep settings save a range-bound choice and restore the firmware default', async () => {
  const page = fixture({ sleepMinutes: 0 });
  await page.refresh();
  assert.equal(page.el('sleep-timeout').hidden, false);
  assert.equal(page.el('sleep-minutes').value, '');
  page.el('sleep-minutes').valueAsNumber = 99;
  await page.dispatch('sleep-form', 'submit');
  assert.equal(page.el('sleep-minutes').value, '99');
  await page.dispatch('sleep-default', 'click');
  assert.equal(page.el('sleep-minutes').value, '');
  assert.deepEqual(page.calls, [
    { op: 'get' }, { op: 'sleep.save', minutes: 99 }, { op: 'get' },
    { op: 'sleep.save', minutes: 0 }, { op: 'get' },
  ]);
});

test('sleep settings are hidden if the connected firmware lacks them', async () => {
  const page = fixture();
  await page.refresh();
  assert.equal(page.el('sleep-timeout').hidden, true);
});


test('page order buttons place down before up without reversing their actions', async () => {
  const page = fixture({ displayPages: 'TECNM' });
  await page.refresh();
  const list = page.el('display-pages-list');
  const middle = list.children[2].children[1].children;
  assert.deepEqual(middle.map(control => control.textContent), ['Move down', 'Move up']);
});

test('sync timing saves and reads back and is hidden with older firmware', async () => {
  const page = fixture({ syncTiming: 0 });
  await page.refresh();
  assert.equal(page.el('sync-timing').hidden, false);
  assert.equal(page.el('sync-mode').value, '0');
  page.el('sync-mode').value = '3';
  await page.dispatch('sync-timing-form', 'submit');
  assert(page.calls.some(call => call.op === 'sync.save' && call.mode === 3));
  assert.equal(page.el('sync-mode').value, '3');
  const old = fixture();
  await old.refresh();
  assert.equal(old.el('sync-timing').hidden, true);
});

test('end-of-pages setting and wireless priority round-trip independently', async () => {
  const page = fixture({ displayPages: 'TECNM', pageWrap: true, preferBluetooth: false });
  await page.refresh();
  assert.equal(page.el('page-wrap-field').hidden, false);
  assert.equal(page.el('page-wrap').checked, true);
  page.el('page-wrap').checked = false;
  await page.dispatch('display-pages-form', 'submit');
  assert(page.calls.some(call => call.op === 'pages.save' && call.wrap === false));
  assert.equal(page.el('page-wrap').checked, false);

  assert.equal(page.el('wireless-choice').value, 'wifi');
  page.el('wireless-choice').value = 'bluetooth';
  await page.dispatch('wireless-form', 'submit');
  assert(page.calls.some(call => call.op === 'wireless.save' && call.preferBluetooth === true));
  assert.equal(page.el('wireless-choice').value, 'bluetooth');
  const old = fixture({ displayPages: 'TECNM' });
  await old.refresh();
  assert.equal(old.el('page-wrap-field').hidden, true);
  assert.equal(old.el('wireless-priority').hidden, true);
});

test('Wi-Fi can be switched off from USB while Bluetooth stays on', async () => {
  const page = fixture({ transport: 'USB', wifiEnabled: true, bluetoothEnabled: true });
  await page.refresh();
  assert.equal(page.el('wireless-radios').hidden, false);
  assert.equal(page.el('wifi-enabled').checked, true);
  assert.equal(page.el('bluetooth-enabled').checked, true);
  page.el('wifi-enabled').checked = false;
  await page.dispatch('radios-form', 'submit');
  assert(page.calls.some(call => call.op === 'radios.save' && !call.wifiEnabled && call.bluetoothEnabled));
  assert.equal(page.el('wifi-enabled').checked, false);
  assert.equal(page.el('bluetooth-enabled').checked, true);
  const old = fixture({ transport: 'USB' });
  await old.refresh();
  assert.equal(old.el('wireless-radios').hidden, true);
});

test('disabling the active Bluetooth connection acknowledges the save without reconnecting', async () => {
  const page = fixture({ transport: 'BLE', wifiEnabled: true, bluetoothEnabled: true });
  await page.refresh();
  const firstSaveCall = page.calls.length;
  page.el('bluetooth-enabled').checked = false;
  await page.dispatch('radios-form', 'submit');
  assert.deepEqual(page.calls.slice(firstSaveCall).map(call => call.op), ['radios.save']);
  assert.equal(page.el('wireless-radios').hidden, true);
  assert.match(page.el('status').textContent, /Reconnect by USB/);
});

test('Settings reflects new cached SD usage after a month deletion', async () => {
  const health = { sdUsedMiB: 1536, sdTotalMiB: 2048 };
  const page = fixture({ health, portal: true, recorderFirst: true });
  await page.refresh();
  assert.equal(page.el('note-sd').textContent, '1.50 / 2.00 GiB');
  assert.equal(page.el('note-sd-row').hidden, false);

  health.sdUsedMiB = 512;
  await page.refresh();
  assert.equal(page.el('note-sd').textContent, '0.50 / 2.00 GiB');
  assert.ok(page.calls.every(call => call.op === 'get'));

  delete health.sdUsedMiB;
  delete health.sdTotalMiB;
  await page.refresh();
  assert.equal(page.el('note-sd').textContent, '');
  assert.equal(page.el('note-sd-row').hidden, true);
});
