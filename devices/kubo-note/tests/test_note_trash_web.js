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
  addEventListener(type, handler) { this.listeners.set(type, handler); }
  dispatch(type, event = {}) { return this.listeners.get(type)?.(event); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  showModal() { this.open = true; }
  close() { this.open = false; }
  scrollIntoView() { this.scrolled = true; }
}

function fixture({ memoryTrash, transport = 'BLE', portal = false, count = 0, hash = '', name = 'Willow', displayPages, sleepMinutes, syncTiming } = {}) {
  const elements = new Map();
  const buttons = [];
  for (const match of settings.matchAll(/<([a-z][a-z0-9]*)\b[^<>]*\bid="([^"]+)"[^<>]*>/g)) {
    const element = new Element(match[1]);
    element.hidden = /\shidden[\s>]/.test(match[0]);
    if (element.tag === 'button') buttons.push(element);
    elements.set(match[2], element);
  }

  let connected = true;
  let retention = 90;
  let trash = Array.from({ length: count }, (_, index) =>
    ({ num: index + 1, tag: 'Recording', capturedUtc: '', trashedAt: 0 }));
  const calls = [];
  const context = vm.createContext({
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
      if (options.body) assert.equal(options.headers['X-Pala-CSRF'], 'synthetic-nonce');
      calls.push(data);
      let reply = { ok: true };
      switch (data.op) {
        case 'get':
          reply = {
            ...reply, ok: connected, csrf: 'synthetic-nonce', transport, capabilities: { devicePortal: portal },
            device: connected ? { name, language: 'en', memoryTrash, displayPages, sleepMinutes, syncTiming } : undefined, wifi: [], peers: [],
          };
          break;
        case 'settings.save':
          name = data.name;
          break;
        case 'sleep.save':
          sleepMinutes = data.minutes;
          break;
        case 'sync.save':
          syncTiming = data.mode;
          break;
        case 'pages.save':
          displayPages = data.pages;
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
    refresh: () => vm.runInContext('refresh()', context),
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
