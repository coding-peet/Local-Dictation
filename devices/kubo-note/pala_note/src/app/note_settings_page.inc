R"PALAHTML(<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="referrer" content="no-referrer">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Note settings</title>
<style>
:root {
  color-scheme: light dark;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
  /* Local Dictation semantic colors from ThemeManager, in light and dark. */
  --page: #fef7ff;
  --card: #fffbfe;
  --container: #f3edf7;
  --text: #1d1b20;
  --muted: #49454f;
  --line: #cac4d0;
  --input-line: #79747e;
  --accent: #6750a4;
  --on-accent: #ffffff;
  --danger: #8c1d18;
  --success: #267340;
}
@media (prefers-color-scheme: dark) {
  :root {
    --page: #141218;
    --card: #1d1b20;
    --container: #211f26;
    --text: #e6e0e9;
    --muted: #cac4d0;
    --line: #49454f;
    --input-line: #938f99;
    --accent: #d0bcff;
    --on-accent: #381e72;
    --danger: #ffb4ab;
    --success: #75ca91;
  }
}
* { box-sizing: border-box; }
[hidden] { display: none !important; }
body { margin: 0; background: var(--page); color: var(--text); line-height: 1.5; }
main { width: min(100% - 2rem, 48rem); margin: 2rem auto 3rem; }
header, .row { display: flex; align-items: center; justify-content: space-between; gap: .75rem; }
header { margin-bottom: 1rem; }
h1, h2 { line-height: 1.2; margin: 0; }
h1 { font-size: clamp(1.65rem, 4vw, 2.25rem); font-weight: 750; letter-spacing: -.025em; }
h2 { font-size: 1.35rem; margin-bottom: .85rem; }
section { background: var(--card); padding: clamp(.875rem, 3.2vw, 1.4rem); border: 1px solid var(--line); border-radius: 1.5rem; margin-bottom: .875rem; }
#trash-disclosure summary { cursor: pointer; }
#trash-disclosure summary h2 { display: inline-block; margin: 0; }
#trash-disclosure[open] summary { margin-bottom: .85rem; }
#viewer { color: var(--accent); font-weight: 650; }
p { margin: .25rem 0 .75rem; }
.muted { color: var(--muted); }
#status { margin: 0; }
#status:not(:empty) { margin: .25rem 0 .85rem; }
#status[data-error="true"], .danger { color: var(--danger); }
form, fieldset { display: grid; gap: .75rem; }
fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
#bridge-address-form, #bridge-order { margin-top: .85rem; }
#note-health { display: grid; gap: .45rem; margin: 0 0 .85rem; padding: 0 0 .85rem; border-bottom: 1px solid var(--line); }
#note-health dd { margin: 0; font-variant-numeric: tabular-nums; }
.weather-help { border-top: 1px solid var(--line); margin-top: .9rem; padding-top: .75rem; }
.weather-help summary { cursor: pointer; font-weight: 650; }
.weather-help[open] summary { margin-bottom: .5rem; }
.weather-icons { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 10.75rem), 1fr)); gap: .5rem; list-style: none; padding: 0; }
.weather-icons li { display: flex; align-items: center; gap: .4rem; margin: 0; padding: .25rem; min-width: 0; font-size: .9rem; }
.weather-icons img { flex: none; background: #fff; border-radius: 4px; image-rendering: pixelated; }
.setting-row {
  display: grid;
  grid-template-columns: min(15rem, 44%) minmax(0, 1fr);
  align-items: center;
  gap: .75rem;
}
#bridge-processing-form { margin-top: 1.15rem; padding-top: 1.15rem; border-top: 1px solid var(--line); }
#kubo-processing-fields, #legacy-processing-fields { display: grid; gap: .75rem; }
.setting-with-help, .bridge-codex-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: .75rem;
  align-items: center;
}
.bridge-codex-row p, #kubo-google-status { margin: 0; }
.bridge-codex-row button { white-space: nowrap; }
.bridge-switch-label { display: flex; justify-content: space-between; align-items: center; gap: .75rem; cursor: pointer; }
.bridge-switch { position: relative; flex: 0 0 3.25rem; height: 2rem; }
.bridge-switch input { position: absolute; inset: 0; margin: 0; min-height: 0; opacity: 0; cursor: pointer; }
.bridge-switch-track {
  display: block;
  height: 100%;
  border: 2px solid var(--input-line);
  border-radius: 999px;
  background: var(--container);
  pointer-events: none;
}
.bridge-switch-track::after {
  content: '';
  position: absolute;
  top: 50%;
  inset-inline-start: .45rem;
  transform: translateY(-50%);
  display: grid;
  place-items: center;
  width: 1rem;
  height: 1rem;
  border-radius: 50%;
  background: var(--input-line);
}
.bridge-switch input:checked + .bridge-switch-track { border-color: var(--accent); background: var(--accent); }
.bridge-switch input:checked + .bridge-switch-track::after {
  content: '\2713';
  inset-inline-start: auto;
  inset-inline-end: .2rem;
  width: 1.6rem;
  height: 1.6rem;
  background: var(--on-accent);
  color: var(--accent);
}
.bridge-switch input:focus-visible + .bridge-switch-track { outline: 2px solid var(--accent); outline-offset: 2px; }
.context-help { position: relative; }
.context-help[open] { z-index: 2; }
.context-help summary {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  border-radius: 50%;
  color: var(--muted);
  cursor: pointer;
  list-style: none;
}
.context-help summary::-webkit-details-marker { display: none; }
.context-help summary:hover, .context-help[open] summary { background: var(--container); color: var(--text); }
.help-popup {
  position: absolute;
  z-index: 1;
  inset-inline-end: 0;
  margin: .25rem 0 0;
  width: min(22rem, calc(100vw - 4.5rem));
  padding: .7rem .85rem;
  border: 1px solid var(--input-line);
  border-radius: .7rem;
  background: var(--card);
  color: var(--text);
  font-size: .9rem;
  white-space: pre-line;
  box-shadow: 0 .35rem 1rem #0003;
}
label { display: grid; gap: .3rem; min-width: 0; font-weight: 550; }
input, select, button { font: inherit; min-height: 2.65rem; border-radius: .55rem; }
input, select { color: var(--text); background: var(--card); border: 1px solid var(--input-line); padding: .48rem .62rem; min-width: 0; width: 100%; }
button { cursor: pointer; border-radius: 999px; font-weight: 650; background: var(--card); border: 1px solid var(--line); color: var(--text); padding: .42rem .8rem; }
button:hover { border-color: var(--accent); }
:where(a, button, input, select, summary):focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
:where(button, input):disabled { opacity: .6; cursor: default; }
.primary { background: var(--accent); border-color: var(--accent); color: var(--on-accent); }
.actions { display: flex; flex-wrap: wrap; gap: .45rem; }
.form-footer { display: flex; align-items: center; flex-wrap: wrap; gap: .65rem 1rem; }
.password-feedback {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: .5rem;
  flex: 1 1 7rem;
  max-width: 24rem;
  min-width: 0;
  margin-inline-start: auto;
}
.password-feedback .help-popup { inset-block-end: calc(100% + .25rem); margin: 0; }
ul { margin: 0 0 .8rem; padding: 0; list-style: none; }
li { min-height: 3rem; border-bottom: 1px solid var(--line); padding: .35rem 0; }
li:last-child { border-bottom: 0; }
li span { overflow-wrap: anywhere; }
li .actions { flex-shrink: 0; }
#wifi-list { margin-bottom: 1.1rem; border-bottom: 1px solid var(--line); }
#wifi-list:empty { display: none; }
#display-pages-list .row { flex-wrap: wrap; }
#display-pages-list .actions { display: grid; grid-template-columns: repeat(2, 7.25rem); }
#display-pages-list button { width: 100%; padding-inline: .55rem; white-space: nowrap; }
.unsaved { margin: 0; font-size: .875rem; color: var(--danger); }
.password-entry { position: relative; min-width: 0; }
.password-entry input { padding-inline-end: 2.8rem; }
.password-entry input::-ms-reveal { display: none; }
#note-password-form[data-passwords-match="true"] :is(#note-password-new, #note-password-repeat) {
  border-color: var(--success);
}
.password-eye {
  position: absolute;
  inset: .2rem .3rem .2rem auto;
  width: 2.2rem;
  min-height: 0;
  padding: 0;
  border-color: transparent;
  color: var(--muted);
  background: transparent;
}
.password-eye[aria-pressed="true"] { color: var(--accent); background: var(--container); }
.password-strength { display: grid; gap: .25rem; font-size: .8rem; }
.strength-caption { color: var(--muted); }
.strength-caption strong:not(:empty)::before { content: " · "; }
.strength-track { height: .35rem; border-radius: 1rem; background: var(--line); overflow: hidden; }
.strength-track::before { content: ''; display: block; height: 100%; border-radius: inherit; }
.password-strength[data-strength="1"] .strength-track::before { width: 33%; background: #d34c4c; }
.password-strength[data-strength="2"] .strength-track::before { width: 66%; background: #c68e13; }
.password-strength[data-strength="3"] .strength-track::before { width: 100%; background: #3a9b62; }
.page-down { grid-column: 1; }
.page-up { grid-column: 2; }
.display-home { padding: .65rem .8rem; border-radius: .75rem; background: var(--container); }
.page-toggle { display: flex; align-items: center; gap: .5rem; }
.page-toggle input { width: 1.1rem; min-height: 1.1rem; margin: 0; accent-color: var(--accent); }
dialog { width: min(27rem, 100% - 2rem); color: var(--text); background: var(--card); border: 1px solid var(--line); border-radius: .9rem; padding: 1.15rem; }
dialog::backdrop { background: #0009; }
.pair-code { font: bold 2rem/1.25 ui-monospace, monospace; letter-spacing: .12em; }
dialog .actions { justify-content: end; }
@media (max-width: 32rem) {
  main { margin-top: 1.1rem; }
  .form-footer { gap: .55rem .65rem; }
  .bridge-codex-row button { max-width: 9rem; white-space: normal; }
  #display-pages-list .actions { grid-template-columns: repeat(2, 6.5rem); }
  #display-pages-list button { padding-inline: .35rem; }
}
@media (max-width: 25rem) {
  .setting-row { grid-template-columns: 1fr; gap: .3rem; }
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
  <button id="refresh" type="button" hidden>Refresh</button>
</header>
<p id="status" role="status" aria-live="polite"></p>

<section id="access-login" hidden>
  <h2>Sign in to Note</h2>
  <p id="access-login-help" class="muted" hidden>Set the Note password over a paired USB connection.</p>
  <form id="access-login-form">
    <div class="setting-row">
      <label for="access-password">Note password</label>
      <div class="password-entry">
        <input id="access-password" type="password" minlength="8" maxlength="48" autocomplete="current-password" required>
        <button id="access-password-eye" type="button" class="password-eye" aria-controls="access-password" aria-label="Show password" title="Show password" aria-pressed="false"><span aria-hidden="true">&#128065;&#65038;</span></button>
      </div>
    </div>
    <div class="actions"><button class="primary" type="submit">Sign in</button></div>
  </form>
</section>

<div id="settings-content" hidden>
<section id="note" hidden>
  <h2>Note</h2>
  <p id="recorder-archive" hidden><a href="/archive">Archive recordings</a></p>
  <dl id="note-health" hidden>
    <div id="note-battery-row" class="setting-row" hidden><dt>Battery</dt><dd id="note-battery"></dd></div>
    <div id="note-sd-row" class="setting-row" hidden><dt>SD card</dt><dd id="note-sd"></dd></div>
    <div id="note-fw-row" class="setting-row" hidden><dt>Firmware</dt><dd id="note-fw"></dd></div>
    <div id="note-audio-row" class="setting-row" hidden><dt>Waiting for transcription</dt><dd id="note-audio"></dd></div>
    <div id="note-delivery-row" class="setting-row" hidden><dt>Waiting for delivery</dt><dd id="note-delivery"></dd></div>
    <div id="note-ram-row" class="setting-row" hidden><dt>Internal RAM</dt><dd id="note-ram"></dd></div>
    <div id="note-psram-row" class="setting-row" hidden><dt>PSRAM</dt><dd id="note-psram"></dd></div>
  </dl>
  <form id="note-form">
    <label class="setting-row">Name <input id="note-name" maxlength="12" required></label>
    <label class="setting-row">Language
      <select id="note-language">
        <option value="en">English</option><option value="de">Deutsch</option>
        <option value="sk">Slovenčina</option><option value="cs">Čeština</option>
        <option value="fr">Français</option><option value="es">Español</option>
        <option value="it">Italiano</option><option value="pl">Polski</option>
        <option value="nl">Nederlands</option><option value="pt">Português</option>
        <option value="ru">Русский</option><option value="uk">Українська</option>
      </select>
    </label>
    <label class="setting-row" id="note-codex-limit-row" hidden>Codex limit on Home
      <select id="note-codex-limit">
        <option value="5h">5 hours (weekly fallback)</option>
        <option value="weekly">Weekly</option>
      </select>
    </label>
    <div class="actions"><button class="primary" type="submit">Save Note settings</button></div>
  </form>
  <details class="weather-help">
    <summary>Weather icons</summary>
    <p class="muted">The forecast appears on the left of Home beside today's low and high.
      The Moon is on the right. A full Moon is an empty circle and a new Moon is solid black.
      White shows the illuminated part.</p>
    <!-- Icon pixels rendered from drawWeatherGlyph in the Note firmware. -->
    <ul class="weather-icons" aria-label="Weather icon legend">
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAcElEQVR42pVSWw7AMAgSs/tfmX2sL6hNM3+aGhREwdCAJzJuMRH4HspPEETN5bQYXXo8u04ryn0QoTNEq1ZIVvIE0hGAOwdT6k7OxMoymss0orRBdFydlthNvHuaoasjqYtbd8tKtOy2OoKfF3Sy7gWh3iEnLxrYOwAAAABJRU5ErkJggg==" width="66" height="40" alt=""><span>Clear</span></li>
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAfUlEQVR42o2RQRLDMAgDV4z//2X1gJuCQ6bhYowFSLKMDIAgkyMCLADZmQwIDDlohEQee9MEiQoYIQs1gkZ0yqsL2IJqV+zOa4Y5ZkTbOfEJ/oX8cLksXqXSwl++q5ZuIPnHQ+OvWW+YvkfMP9+0jFbgylA3Nfl2mHRaAnwA0IMrISJFvrMAAAAASUVORK5CYII=" width="66" height="40" alt=""><span>Partly cloudy</span></li>
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAZUlEQVR42p2RSxLAIAhDE8f7XzldOFY+rdKyUvKEIBQO0U4AergTgN4JDpWO8V0kpBqW4JTET06pp+N0ZJwyPNTy21cqQaNoSw0Mw5rTKiHW9+J2IOuRaZpbkd1q+JLNnH9m2cUFDAUbJDra6nEAAAAASUVORK5CYII=" width="66" height="40" alt=""><span>Overcast</span></li>
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAKklEQVR42mP8z0AAMDFQroIFQjFikYLaz0gXd1DRL9i8M0T9gidaBo1fAOi/Bydb7eUaAAAAAElFTkSuQmCC" width="66" height="40" alt=""><span>Fog</span></li>
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAbElEQVR42pWRSw4AIQhDW+P9r9xZGEY+apQFMfCsBSmEIIBY6qktAAxMC7yEouEJWkvcaqyCWh3NkXPKdFHTb5+lAg3RVh5wDO+c3hLinuiHRQz75pFlmr8j/6tpJYc5N7Ow5ERQOb/t4yCBDyTMISUlr8Z+AAAAAElFTkSuQmCC" width="66" height="40" alt=""><span>Rain</span></li>
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAbUlEQVR42p1R0RKAIAyCzv//ZXpIG9pmXXviJhswKURxAG82eGlhAsDhK8aoWDDyojIIhmRLlPsrLw61jq8C7z6+MsSa0TYxMXkkVNwhUmSBNzl/ZOkiTHAwxOlvzZ/cqJ7YdkB307H5UFzWMU5ehSYfE/70XAAAAABJRU5ErkJggg==" width="66" height="40" alt=""><span>Snow</span></li>
      <li><img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACEAAAAUCAAAAAAmOAutAAAAdUlEQVR42oWRSQ7AIAwDbcT/v+weSpS1lBMiQ+IBCmkRQD7apSwATMxKvITWIxK0kvjZY1rUtLVEISnLRXne7UcNepuuNgBkCtyT8mI7AofwB2hAfvXsdGDLyFcweFnFbenTGP2plvTPBbPLRbYSHSi27N+DB+67JSqqZ6hSAAAAAElFTkSuQmCC" width="66" height="40" alt=""><span>Thunderstorm</span></li>
    </ul>
    <p class="muted">Drizzle, freezing rain and rain showers use Rain. Without a
      forecast for today, the weather icon and temperatures are blank.</p>
  </details>
</section>

<section id="wireless-radios" hidden>
  <h2>Wireless connections</h2>
  <form id="radios-form">
    <label class="bridge-switch-label setting-row">
      <span>Enable Wi-Fi</span>
      <span class="bridge-switch">
        <input id="wifi-enabled" type="checkbox" role="switch" aria-describedby="radios-hint">
        <span class="bridge-switch-track" aria-hidden="true"></span>
      </span>
    </label>
    <label class="bridge-switch-label setting-row">
      <span>Enable Bluetooth</span>
      <span class="bridge-switch">
        <input id="bluetooth-enabled" type="checkbox" role="switch" aria-describedby="radios-hint">
        <span class="bridge-switch-track" aria-hidden="true"></span>
      </span>
    </label>
    <p id="radios-hint" class="muted">Turning off the current connection closes it. USB can turn either radio back on.</p>
    <div class="actions"><button type="submit" class="primary">Save wireless connections</button></div>
  </form>
</section>

<section id="wireless-priority" hidden>
  <h2>Sync connection priority</h2>
  <form id="wireless-form">
    <div class="setting-row">
      <label for="wireless-choice">Preferred wireless connection</label>
      <div class="setting-with-help">
        <select id="wireless-choice" aria-describedby="wireless-hint">
          <option value="wifi">Wi-Fi first</option>
          <option value="bluetooth">Bluetooth first</option>
        </select>
        <details class="context-help">
          <summary aria-label="About sync priority">&#9432;</summary>
          <p id="wireless-hint" class="help-popup">USB has first priority. This setting controls wireless recording sync.</p>
        </details>
      </div>
    </div>
    <div class="actions"><button type="submit" class="primary">Save priority</button></div>
  </form>
</section>

<section id="wifi">
  <h2>Wi-Fi networks</h2>
  <p id="wifi-unavailable" class="muted">Connect the Note and select Refresh to manage its Wi-Fi networks.</p>
  <p id="wifi-empty" class="muted" hidden>No saved networks.</p>
  <ul id="wifi-list"></ul>
  <form id="wifi-form">
    <fieldset id="wifi-fields" disabled>
      <label class="setting-row">Network name (SSID) <input id="wifi-name" maxlength="32" required autocomplete="off" spellcheck="false"></label>
      <div class="setting-row">
        <label for="wifi-secret">Password</label>
        <div class="password-entry">
          <input id="wifi-secret" type="password" maxlength="63" autocomplete="new-password" aria-describedby="wifi-password-hint wifi-secret-strength-label">
          <button id="wifi-secret-eye" type="button" class="password-eye" aria-controls="wifi-secret" aria-label="Show password" title="Show password" aria-pressed="false"><span aria-hidden="true">&#128065;&#65038;</span></button>
        </div>
      </div>
      <p id="wifi-password-hint" class="muted" hidden>Leave the password blank when editing a network to keep its saved password.</p>
      <div class="form-footer">
        <button class="primary" type="submit">Save network</button>
        <div class="password-feedback">
          <div id="wifi-secret-strength" class="password-strength" data-strength="0">
            <span class="strength-track" aria-hidden="true"></span>
            <span class="strength-caption">Strength <strong id="wifi-secret-strength-label" role="status"></strong></span>
          </div>
          <details class="context-help">
            <summary aria-label="How password strength is estimated">&#9432;</summary>
            <p id="wifi-secret-strength-help" class="help-popup"></p>
          </details>
        </div>
      </div>
    </fieldset>
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

<section id="sync-timing" hidden>
  <h2>Recording sync</h2>
  <form id="sync-timing-form">
    <div class="setting-row">
      <label for="sync-mode">Automatic sync</label>
      <div class="setting-with-help">
        <select id="sync-mode" aria-describedby="sync-mode-hint">
          <option value="0">Before sleep (default)</option>
          <option value="1">Immediately on wake</option>
          <option value="2">After recording</option>
          <option value="3">Manual only</option>
        </select>
        <details class="context-help">
          <summary aria-label="About recording sync">&#9432;</summary>
          <p id="sync-mode-hint" class="help-popup">To sync manually, tap the upper button three times on Home.</p>
        </details>
      </div>
    </div>
    <div class="actions"><button type="submit" class="primary">Save sync timing</button></div>
  </form>
</section>

<section id="restart-note" hidden>
  <h2>Restart Note</h2>
  <p id="restart-status" class="muted" aria-live="polite"></p>
  <p id="restart-estimate" class="muted">The storage check may take a long time. Recording is unavailable until it finishes.</p>
  <div class="actions">
    <button type="button" id="restart-normal">Restart</button>
    <button type="button" id="restart-audit">Restart and check storage</button>
  </div>
</section>

<section id="display-pages" hidden>
  <h2>Display pages</h2>
  <p class="muted">Tap the upper button to move up through enabled pages.
    Tap the lower button to return to Home from any display page.</p>
  <form id="display-pages-form">
    <fieldset id="display-pages-fields">
      <ul id="display-pages-list"></ul>
      <div class="row display-home"><strong>Home</strong><span class="muted">Always on bottom</span></div>
      <label class="page-toggle" id="page-wrap-field" hidden>
        <input id="page-wrap" type="checkbox"> Return to Home after the top page
      </label>
      <div class="actions"><button type="submit" class="primary">Save display pages</button></div>
    </fieldset>
  </form>
</section>

<section id="sleep-timeout" hidden>
  <h2>Sleep timeout</h2>
  <form id="sleep-form">
    <fieldset id="sleep-fields">
      <label class="setting-row" for="sleep-minutes">Idle minutes
        <input id="sleep-minutes" type="number" min="1" max="99" step="1" required aria-describedby="sleep-status">
      </label>
      <p id="sleep-status" class="muted"></p>
      <div class="actions">
        <button type="submit" class="primary">Save timeout</button>
        <button type="button" id="sleep-default">Default</button>
      </div>
    </fieldset>
  </form>
</section>

<section id="bridge" hidden>
  <h2>Bridge</h2>
  <form id="bridge-form" hidden>
    <label class="setting-row">Bridge name <input id="bridge-name" maxlength="24"></label>
    <label class="setting-row">Weather location <input id="weather-location" maxlength="120" placeholder="City, country code"></label>
    <label class="setting-row">Holiday country <select id="holiday-country">
      <option value="SK">Slovakia</option>
      <option value="CZ">Czechia</option>
    </select></label>
    <div class="actions"><button class="primary" type="submit">Save Bridge settings</button></div>
  </form>
  <form id="bridge-processing-form" hidden>
    <div class="setting-with-help">
      <label class="bridge-switch-label">
        <span id="processing-label">Deliver transcripts</span>
        <span class="bridge-switch">
          <input id="kubo-processing" type="checkbox" role="switch" aria-describedby="kubo-processing-hint">
          <span class="bridge-switch-track" aria-hidden="true"></span>
        </span>
      </label>
      <details class="context-help">
        <summary id="processing-help" aria-label="About transcript delivery">&#9432;</summary>
        <p id="kubo-processing-hint" class="help-popup">Route saved transcripts to Google Tasks. Local transcription and the archive work independently.</p>
      </details>
    </div>
    <div id="kubo-processing-fields" hidden>
      <label class="setting-row">Default Google Tasks list <input id="kubo-task-list" maxlength="120" placeholder="Notes"></label>
      <div id="legacy-processing-fields">
        <label class="setting-row">Personal Google Calendar ID <input id="kubo-calendar" maxlength="240" placeholder="primary" spellcheck="false"></label>
        <div class="bridge-codex-row">
          <p class="muted">Run <code>codex login</code> for note recognition and sorting.</p>
          <button id="kubo-check-login" type="button">Check Codex sign-in</button>
        </div>
      </div>
      <p id="kubo-google-status" class="muted"></p>
    </div>
    <div class="actions"><button class="primary" type="submit">Save processing parameters</button></div>
  </form>
  <form id="bridge-address-form" hidden>
    <div class="setting-row">
      <label for="bridge-address">Fallback IPv4 address</label>
      <div class="setting-with-help">
        <input id="bridge-address" maxlength="21" autocomplete="off" spellcheck="false" aria-describedby="bridge-address-hint">
        <details class="context-help">
          <summary aria-label="About the fallback Bridge address">&#9432;</summary>
          <p id="bridge-address-hint" class="help-popup">Leave blank to use automatic discovery. Enter the private IPv4 address of the legacy or sole Wi-Fi Bridge. Include a port if needed, for example 192.168.1.31:8443.</p>
        </details>
      </div>
    </div>
    <div class="actions"><button class="primary" type="submit">Save address</button></div>
  </form>
  <div id="bridge-order" hidden>
    <p class="muted">The Note tries the last working Bridge first, then this order.</p>
    <ul id="bridge-list"></ul>
    <button id="save-order" class="primary" type="button">Save order</button>
  </div>
</section>

<section id="note-password" hidden>
  <h2>Note password</h2>
  <p id="note-password-open" class="muted" hidden>Password protection is off.</p>
  <form id="note-protection-form" hidden>
    <label class="bridge-switch-label setting-row">
      <span>Require password</span>
      <span class="bridge-switch">
        <input id="note-protection" type="checkbox" role="switch" aria-describedby="note-protection-help">
        <span class="bridge-switch-track" aria-hidden="true"></span>
      </span>
    </label>
    <p id="note-protection-help" class="muted">Applies to Web Companion and Bluetooth settings.</p>
    <div class="actions"><button class="primary" type="submit">Save protection</button></div>
  </form>
  <form id="note-password-form">
    <div class="setting-row">
      <label for="note-password-old">Current password</label>
      <div class="password-entry">
        <input id="note-password-old" type="password" maxlength="48" autocomplete="current-password">
        <button id="note-password-old-eye" type="button" class="password-eye" aria-controls="note-password-old" aria-label="Show password" title="Show password" aria-pressed="false"><span aria-hidden="true">&#128065;&#65038;</span></button>
      </div>
    </div>
    <div class="setting-row">
      <label for="note-password-new">New password</label>
      <div class="password-entry">
        <input id="note-password-new" type="password" minlength="8" maxlength="48" autocomplete="new-password" aria-describedby="note-password-new-strength-label" required>
        <button id="note-password-new-eye" type="button" class="password-eye" aria-controls="note-password-new" aria-label="Show password" title="Show password" aria-pressed="false"><span aria-hidden="true">&#128065;&#65038;</span></button>
      </div>
    </div>
    <div class="setting-row">
      <label for="note-password-repeat">Repeat new password</label>
      <div class="password-entry">
        <input id="note-password-repeat" type="password" minlength="8" maxlength="48" autocomplete="new-password" required>
        <button id="note-password-repeat-eye" type="button" class="password-eye" aria-controls="note-password-repeat" aria-label="Show password" title="Show password" aria-pressed="false"><span aria-hidden="true">&#128065;&#65038;</span></button>
      </div>
    </div>
    <div class="form-footer">
      <div class="actions">
        <button class="primary" type="submit">Save password</button>
        <button id="note-password-logout" type="button">Sign out</button>
      </div>
      <div class="password-feedback">
        <div id="note-password-new-strength" class="password-strength" data-strength="0">
          <span class="strength-track" aria-hidden="true"></span>
          <span class="strength-caption">Strength <strong id="note-password-new-strength-label" role="status"></strong></span>
        </div>
        <details class="context-help">
          <summary aria-label="How password strength is estimated">&#9432;</summary>
          <p id="note-password-new-strength-help" class="help-popup"></p>
        </details>
      </div>
    </div>
  </form>
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
      <label class="setting-row">Retention (days)
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
    <label class="setting-row">Type MOVE ALL to confirm
      <input id="move-all-confirmation" name="confirmation" required pattern="MOVE ALL" autocomplete="off" spellcheck="false">
    </label>
    <div class="actions"><button class="danger" type="submit">Move all to Trash</button></div>
  </form>
</section>
</div>

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
let noteSession = '';
let passwordAccess = '';
let busy = false;
let bridgeOrder = [];
let displayPages = [];
const displayPageNames = {
  T: 'Tasks', E: 'Events', C: 'Current month', N: 'Next month', M: 'Month after next',
};
let savedNetworks = [];
let keepsWifiPassword = false;
let trashCount = 0;
let trashIndex = 0;
let trashRead = false;
const trashPageSize = 5;

let savedNoteName = '';
let savedTarget = null;
let savedTransport = '';
const drafts = new Map();
const newPasswordFields = ['note-password-old', 'note-password-new', 'note-password-repeat'];

function trackDraft(op, id, values, onNote = true) {
  const form = el(id);
  const notice = document.createElement('p');
  notice.className = 'unsaved';
  notice.role = 'status';
  notice.hidden = true;
  form.append(notice);
  const draft = { values, onNote, notice, saved: JSON.stringify(values()), revision: 0 };
  drafts.set(op, draft);
  const edited = () => markDraft(op);
  form.addEventListener('input', edited);
  form.addEventListener('change', edited);
}

function isDirty(draft) {
  return JSON.stringify(draft.values()) !== draft.saved;
}

function showDraftNotice(draft) {
  const pending = isDirty(draft);
  const target = draft.onNote ? (savedNoteName || 'Note') : 'Bridge';
  const message = pending ? 'Changes are not saved to ' + target + '. Select Save.' : '';
  if (draft.notice.textContent !== message) draft.notice.textContent = message;
  draft.notice.hidden = !pending;
}

function markDraft(op) {
  const draft = drafts.get(op);
  draft.revision++;
  showDraftNotice(draft);
}

function acceptDraft(op) {
  const draft = drafts.get(op);
  draft.saved = JSON.stringify(draft.values());
  showDraftNotice(draft);
}

function showFormValues(op, render) {
  const draft = drafts.get(op);
  if (!isDirty(draft)) {
    render();
    acceptDraft(op);
  }
}

function passwordStrength(value) {
  const text = value.trim().toLowerCase();
  if (!value) return 0;
  const variety = new Set(text).size;
  if (text.length < 10 || variety < 4 || /(?:123456|abcdef|qwerty|password|heslo)/.test(text) ||
      /^(.{1,5})\1+$/.test(text)) return 1;
  return text.length >= 16 && variety >= 8 ? 3 : 2;
}

function showPasswordStrength(id) {
  const meter = el(id + '-strength');
  if (!meter) return;
  const level = passwordStrength(el(id).value);
  meter.dataset.strength = String(level);
  el(id + '-strength-label').textContent = ['', 'Weak', 'Fair', 'Strong'][level];
}

function showPasswordMatch() {
  const password = el('note-password-new').value;
  const repeat = el('note-password-repeat').value;
  el('note-password-form').dataset.passwordsMatch = String(password !== '' && password === repeat);
}

function showPassword(id, visible) {
  el(id).type = visible ? 'text' : 'password';
  const eye = el(id + '-eye');
  const caption = visible ? 'Hide password' : 'Show password';
  eye.ariaPressed = String(visible);
  eye.ariaLabel = eye.title = caption;
}

function clearPasswords(...ids) {
  for (const id of ids) {
    el(id).value = '';
    showPassword(id, false);
    showPasswordStrength(id);
  }
  showPasswordMatch();
}

function status(message, error = false) {
  el('status').textContent = message;
  el('status').dataset.error = String(error);
}

async function request(op, fields) {
  const headers = { Accept: 'application/json', 'X-Pala-Settings': '1' };
  if (noteSession) headers['X-Pala-Note-Session'] = noteSession;
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
  if (op === 'auth.login' && result.ok) noteSession = result.session;
  if (result.ok && (op === 'auth.logout' || op === 'auth.save')) {
    noteSession = '';
    if (savedTransport === 'BLE') show({ access: 'locked', transport: 'BLE' });
  }
  if (result.access === 'locked' || result.access === 'setup') {
    noteSession = '';
    if (result.transport === 'Wi-Fi') window.location.assign('/login');
    if (op) show(result);
  }
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
  const name = typeof label === 'string' ? document.createElement('span') : label;
  if (typeof label === 'string') name.textContent = label;
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
      markDraft('bridge.order');
      drawBridgeOrder();
    };
    const controls = [];
    if (index > 0) controls.push(button('Move up', () => move(-1)));
    if (index + 1 < bridgeOrder.length) controls.push(button('Move down', () => move(1)));
    row(list, peer.name, controls);
  });
}

function drawDisplayPages() {
  const list = el('display-pages-list');
  list.replaceChildren();
  // The Note stores pages in the order visited from Home, starting at the bottom.
  for (let index = displayPages.length - 1; index >= 0; index--) {
    const page = displayPages[index];
    const id = page.toUpperCase();
    const name = displayPageNames[id];
    const label = document.createElement('label');
    label.className = 'page-toggle';
    const toggle = document.createElement('input');
    toggle.type = 'checkbox';
    toggle.checked = page === id;
    toggle.addEventListener('change', () => {
      displayPages[index] = toggle.checked ? id : id.toLowerCase();
      markDraft('pages.save');
    });
    label.append(toggle, name);

    const move = delta => {
      const other = index + delta;
      [displayPages[index], displayPages[other]] = [displayPages[other], displayPages[index]];
      markDraft('pages.save');
      drawDisplayPages();
    };
    const controls = [];
    if (index > 0) {
      const down = button('Move down', () => move(-1));
      down.className = 'page-down';
      down.ariaLabel = 'Move ' + name + ' down';
      controls.push(down);
    }
    if (index + 1 < displayPages.length) {
      const up = button('Move up', () => move(1));
      up.className = 'page-up';
      up.ariaLabel = 'Move ' + name + ' up';
      controls.push(up);
    }
    row(list, label, controls);
  }
}

function showNoteHealth(device) {
  const percent = value => Number.isInteger(value) && value >= 0 && value <= 100;
  const usageText = reading => Array.isArray(reading) && reading.length === 2 &&
    reading.every(percent) && reading[0] <= reading[1]
    ? reading[0] + '% current · ' + reading[1] + '% peak since restart' : '';
  const battery = device && percent(device.batteryPercent) ? device.batteryPercent + '%' : '';
  const ram = device ? usageText(device.ramUsage) : '';
  const psram = device ? usageText(device.psramUsage) : '';

  const firmware = device && typeof device.firmware === 'string' ? device.firmware : '';
  const sd = device && Number.isInteger(device.sdTotalMiB) && Number.isInteger(device.sdUsedMiB)
    ? (device.sdUsedMiB / 1024).toFixed(2) + ' / ' + (device.sdTotalMiB / 1024).toFixed(2) + ' GiB' : '';
  const audio = device && Number.isInteger(device.pendingAudio) ? String(device.pendingAudio) : '';
  const delivery = device && Number.isInteger(device.pendingDelivery) ? String(device.pendingDelivery) : '';
  for (const [id, content] of [['note-battery', battery], ['note-ram', ram], ['note-psram', psram],
                               ['note-sd', sd], ['note-fw', firmware], ['note-audio', audio], ['note-delivery', delivery]]) {
    el(id).textContent = content;
    el(id + '-row').hidden = !content;
  }
  el('note-health').hidden = !(battery || ram || psram || sd || firmware || audio || delivery);
}

function show(data) {
  const caps = data.capabilities || {};
  const attached = Boolean(data.ok && data.device);
  const recorder = attached && data.device.recorderFirst === true;
  el('recorder-archive').hidden = !recorder || caps.devicePortal !== true;
  el('restart-audit').hidden = recorder;
  // A temporary BLE disconnect must not reveal settings from a locked page.
  if (data.access === 'locked' || data.access === 'setup') passwordAccess = data.access;
  else if (attached) passwordAccess = '';

  const locked = passwordAccess !== '';
  el('access-login').hidden = !locked;
  el('access-login-form').hidden = passwordAccess === 'setup';
  el('access-login-help').hidden = passwordAccess !== 'setup';
  el('settings-content').hidden = locked;
  el('refresh').hidden = locked;
  if (locked) {
    document.title = 'Note settings';
    el('settings-title').textContent = 'Note settings';
    el('connection').textContent = 'Note locked';
    el('viewer').hidden = true;
    clearPasswords('access-password', 'wifi-secret', ...newPasswordFields);
    el('wifi-name').value = '';
    for (const [op, draft] of drafts) if (draft.onNote) acceptDraft(op);
    for (const id of ['confirm', 'trash-dialog', 'pair-dialog']) {
      if (el(id).open) el(id).close();
    }
    el('trash-disclosure').open = false;
    trashRead = false;
    return;
  }

  const usb = data.transport === 'USB';
  const passwordSupported = attached && typeof data.device.webPasswordConfigured === 'boolean';
  const passwordRequired = !attached || data.device.webPasswordRequired !== false;
  const canSetProtection = usb && attached && typeof data.device.webPasswordRequired === 'boolean';
  el('note-password').hidden = !passwordSupported;
  el('note-password-open').hidden = passwordRequired;
  el('note-protection-form').hidden = !canSetProtection;
  el('note-password-form').hidden = !usb && !passwordRequired;
  el('note-password-old').required = !usb && passwordRequired;
  el('note-password-logout').hidden = usb || !passwordRequired;
  if (canSetProtection) showFormValues('auth.policy', () => {
    el('note-protection').checked = passwordRequired;
  });
  const wifiAvailable = attached && Array.isArray(data.wifi);
  const wifi = wifiAvailable ? data.wifi : [];
  const peers = Array.isArray(data.peers) ? data.peers : [];
  const canPair = caps.pair === true;
  const canManagePeers = attached && caps.peers !== false && Array.isArray(data.peers);
  const name = attached && typeof data.device.name === 'string' ? data.device.name.trim() : '';
  const target = attached ? (data.targetKey || (caps.devicePortal === true ? 'local' : null)) : null;
  if (attached && (!target || target !== savedTarget)) {
    clearPasswords('access-password', 'wifi-secret', ...newPasswordFields);
    el('wifi-name').value = '';
    for (const [op, draft] of drafts) if (draft.onNote) acceptDraft(op);
  }
  if (attached) {
    savedTarget = target;
    savedTransport = data.transport;
    savedNoteName = name;
  }
  const title = name ? name + ' settings' : 'Note settings';
  document.title = title;
  el('settings-title').textContent = title;
  el('viewer').textContent = 'Back to ' + (name || 'Note');
  el('connection').textContent = 'Note disconnected';
  if (attached) el('connection').textContent = data.transport ? 'Connected via ' + data.transport : 'Connected';
  el('note').hidden = !attached;
  showNoteHealth(attached ? data.device : null);
  el('restart-note').hidden = !attached || data.device.r !== 1;
  if (el('restart-note').hidden) displayedRestart = 0;
  el('restart-status').textContent = '';
  const pages = attached ? data.device.displayPages : null;
  const legacyPages = typeof pages === 'string' && pages.length === 4;
  const expectedPages = legacyPages ? 'TECN' : 'TECNM';
  const hasPages = typeof pages === 'string' && pages.length === expectedPages.length &&
    [...expectedPages].every(page => pages.toUpperCase().includes(page));
  el('display-pages').hidden = recorder || !hasPages;
  const pageWrap = attached ? data.device.pageWrap : null;
  el('page-wrap-field').hidden = !hasPages || typeof pageWrap !== 'boolean';
  if (hasPages) showFormValues('pages.save', () => {
    displayPages = Array.from(pages);
    el('page-wrap').checked = pageWrap === true;
  });
  const wifiEnabled = attached ? data.device.wifiEnabled : null;
  const bluetoothEnabled = attached ? data.device.bluetoothEnabled : null;
  const hasRadioSettings = typeof wifiEnabled === 'boolean' && typeof bluetoothEnabled === 'boolean';
  el('wireless-radios').hidden = !hasRadioSettings;
  if (hasRadioSettings) showFormValues('radios.save', () => {
    el('wifi-enabled').checked = wifiEnabled;
    el('bluetooth-enabled').checked = bluetoothEnabled;
  });
  const preferBluetooth = attached ? data.device.preferBluetooth : null;
  el('wireless-priority').hidden = typeof preferBluetooth !== 'boolean';
  if (typeof preferBluetooth === 'boolean') showFormValues('wireless.save', () => {
    el('wireless-choice').value = preferBluetooth ? 'bluetooth' : 'wifi';
  });
  drawDisplayPages();
  const syncTiming = attached ? data.device.syncTiming : null;
  const hasSyncTiming = Number.isInteger(syncTiming) && syncTiming >= 0 && syncTiming <= 3;
  el('sync-timing').hidden = !hasSyncTiming;
  if (hasSyncTiming) showFormValues('sync.save', () => { el('sync-mode').value = String(syncTiming); });
  const sleepMinutes = attached ? data.device.sleepMinutes : null;
  const hasSleepTimeout = Number.isInteger(sleepMinutes) && sleepMinutes >= 0 && sleepMinutes <= 99;
  el('sleep-timeout').hidden = !hasSleepTimeout;
  if (hasSleepTimeout) {
    showFormValues('sleep.save', () => { el('sleep-minutes').value = sleepMinutes ? String(sleepMinutes) : ''; });
    el('sleep-status').textContent = sleepMinutes
      ? 'Current timeout: ' + sleepMinutes + ' min on Home and in Web Companion.'
      : 'Default: 1 min on Home, up to 3 min with queued work, and 5 min in Web Companion.';
  }
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
  el('pair-ble').hidden = caps.pairBle === false || bluetoothEnabled === false;
  el('pair-help').hidden = !canPair;
  el('pair-help').textContent = attached ? 'Follow the confirmation shown on the Note.'
    : 'To connect over Bluetooth, briefly press both buttons on the Note, then select Refresh.';
  el('peer-empty').hidden = !canManagePeers || peers.length > 0;
  el('wifi-empty').hidden = !wifiAvailable || wifi.length > 0;
  el('note-codex-limit-row').hidden = !attached || typeof data.device.weeklyCodexLimit !== 'boolean';
  if (attached) showFormValues('settings.save', () => {
    el('note-name').value = name;
    el('note-language').value = data.device.language || 'en';
    el('note-codex-limit').value = data.device.weeklyCodexLimit === true ? 'weekly' : '5h';
  });
  el('wifi-list').replaceChildren();
  wifi.forEach(({ ssid }) => row(el('wifi-list'), ssid, [
    button('Edit', () => {
      el('wifi-name').value = ssid;
      clearPasswords('wifi-secret');
      acceptDraft('wifi.save');
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
  const processing = bridge?.processing;
  el('bridge-processing-form').hidden = !processing;
  if (processing) {
    el('processing-label').textContent = recorder ? 'Deliver transcripts' : 'Process transcripts';
    el('processing-help').ariaLabel = recorder ? 'About transcript delivery' : 'About transcript processing';
    el('kubo-processing-hint').textContent = recorder
      ? 'Route saved transcripts to Google Tasks. Local transcription and the archive work independently.'
      : 'Process transcripts for configured destinations. Local transcripts are saved on the Note.';
    el('legacy-processing-fields').hidden = recorder;
    showFormValues('bridge.processing.save', () => {
      el('kubo-processing').checked = processing.mode === 'kubo';
      el('kubo-processing-fields').hidden = !el('kubo-processing').checked;
      el('kubo-task-list').value = processing.taskList || '';
      el('kubo-calendar').value = processing.calendarId || '';
    });
    el('kubo-google-status').textContent = 'Google Tasks sign-in ' +
      (processing.tasksLoginConfigured ? 'configured' : 'required') + '.' +
      (recorder ? '' : ' Calendar sign-in ' + (processing.calendarLoginConfigured ? 'configured' : 'required') + '.');
  }
  el('bridge-form').hidden = !bridge;
  if (bridge) showFormValues('bridge.save', () => {
    el('bridge-name').value = bridge.name || '';
    el('weather-location').value = bridge.weatherLocation || '';
    el('holiday-country').value = bridge.holidayCountry || 'SK';
  });
  const ordered = Array.isArray(data.bridgeOrder) ? data.bridgeOrder.map(id => peers.find(peer => peer.id === id)).filter(Boolean) : [];
  showFormValues('bridge.order', () => { bridgeOrder = ordered; });
  const hasOrder = caps.bridgeOrder === true && ordered.length > 1;
  el('bridge-order').hidden = !hasOrder;
  const hasAddress = attached && typeof data.device.bridgeAddress === 'string';
  el('bridge-address-form').hidden = !hasAddress;
  if (hasAddress) showFormValues('bridge.address', () => { el('bridge-address').value = data.device.bridgeAddress; });
  el('bridge').hidden = !bridge && !hasOrder && !hasAddress;
  drawBridgeOrder();
  for (const draft of drafts.values()) showDraftNotice(draft);
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
  return !el('settings-content').hidden && !el('trash').hidden && el('trash-disclosure').open;
}

function openLinkedTrash() {
  if (el('settings-content').hidden || el('trash').hidden || location.hash !== '#trash') return;
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
    showFormValues('trash.retention', () => { el('trash-days').value = String(trash.days); });
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
    const revision = drafts.get(op)?.revision;
    await request(op, fields);
    if (revision !== undefined && drafts.get(op).revision === revision) acceptDraft(op);
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

async function readStorageAudit() {
  if (el('restart-note').hidden) return;
  try {
    const { storageAudit } = await request('restart.status', {});
    if (!storageAudit) return;
    const { notes, estimateSeconds, state, checked, firstNote, repaired } = storageAudit;
    const count = new Intl.NumberFormat();
    const timing = estimateSeconds
      ? 'Estimated ' + count.format(Math.ceil(estimateSeconds / 60)) + ' min based on the previous check. Repairs can take longer.'
      : 'The first check will measure its duration.';
    el('restart-estimate').textContent = count.format(notes) + ' cataloged notes. ' + timing +
      ' Recording is unavailable during the storage check.';
    const incomplete = state === 'incomplete';
    if (incomplete && !Number.isInteger(checked)) {
      el('restart-status').textContent = 'The previous storage check did not finish. Confirm a new check to run it again.';
    } else if ((incomplete || state === 'complete') && Number.isInteger(checked)) {
      const issues = [
        ['WAV', storageAudit.wavIssues], ['metadata', storageAudit.metadataIssues],
        ['catalog', storageAudit.catalogIssues], ['sync queue', storageAudit.queueIssues]
      ].filter(([, quantity]) => quantity > 0).map(([kind, quantity]) => count.format(quantity) + ' ' + kind);
      el('restart-status').textContent = (incomplete ? 'Check incomplete. ' : 'Last check: ') + count.format(checked) + ' notes. ' +
        (issues.length ? 'Reported ' + issues.join(', ') + ' issues.' : 'No issues found.') +
        (firstNote ? ' First affected note: #' + firstNote + '.' : '') +
        (repaired ? ' Rebuilt ' + count.format(repaired) + ' derived indexes.' : '');
    }
  } catch {
    el('restart-status').textContent = 'Storage check status unavailable.';
  }
}

let displayedRestart = 0;
async function checkRestartOutcome(ticket) {
  if (displayedRestart !== ticket || el('restart-note').hidden) return;
  let message = 'Confirmation is no longer active. Check the Note.';
  try {
    const { confirmation } = await request('restart.status', {});
    if (displayedRestart !== ticket || el('restart-note').hidden) return;
    if (confirmation && confirmation.ticket === ticket) {
      if (confirmation.state === 'pending') {
        setTimeout(() => checkRestartOutcome(ticket), 3000);
        return;
      }
      if (confirmation.state === 'expired') message = 'Confirmation expired. The Note did not restart.';
      if (confirmation.state === 'cancelled') message = 'Restart cancelled on the Note.';
      if (confirmation.state === 'confirmed') message = 'Restart confirmed on the Note.';
    }
  } catch {
    message = 'Note unavailable. Reconnect to check its status.';
  }
  if (displayedRestart === ticket && !el('restart-note').hidden) {
    el('restart-status').textContent = message;
    displayedRestart = 0;
  }
}

function requestRestart(mode) {
  return run(async () => {
    const response = await request('restart.request', { mode });
    if (response.pending !== true) throw new Error('The Note did not open the restart confirmation.');
    const prompt = 'Confirm or cancel on the Note display within 30 seconds of the prompt appearing.';
    displayedRestart = response.ticket || 0;
    if (displayedRestart) setTimeout(() => checkRestartOutcome(response.ticket), 31000);
    el('restart-status').textContent = prompt;
    status(prompt);
  });
}

async function showSettings(data) {
  const wasHidden = el('trash').hidden;
  show(data);
  await readStorageAudit();
  if (el('settings-content').hidden || el('trash').hidden) {
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
  el('display-pages-fields').disabled = true;
  el('sleep-fields').disabled = true;
  document.querySelectorAll('main > :not(dialog) button').forEach(control => { control.disabled = true; });
  el('status').dataset.error = 'false';
  try {
    await action();
  } catch (error) {
    if (!passwordAccess) el('refresh').hidden = false;
    status(error.message || 'Could not reach Note.', true);
  } finally {
    busy = false;
    el('display-pages-fields').disabled = false;
    el('sleep-fields').disabled = false;
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

function save(op, fields, message, disconnect = false) {
  return run(async () => {
    status(op === 'auth.login' ? 'Signing in…' : 'Saving…');
    const revision = drafts.get(op)?.revision;
    if (op === 'auth.save' && savedTransport === 'USB') {
      // Renew the page's write permission before sending the password.
      const current = await request();
      if (!current.ok) throw new Error(current.error || 'Wake the Note and check its USB connection.');
      if (!savedTarget || current.targetKey !== savedTarget || current.transport !== 'USB') {
        clearPasswords(...newPasswordFields);
        await showSettings(current);
        throw new Error('The Note or its connection changed. Enter the password again.');
      }
    }
    await request(op, fields);
    if (revision !== undefined && drafts.get(op).revision === revision) {
      if (op === 'wifi.save') clearPasswords('wifi-secret');
      if (op === 'auth.save') clearPasswords(...newPasswordFields);
      acceptDraft(op);
    }
    if (disconnect) {
      await showSettings({ ok: false, capabilities: {} });
      status(message);
      return;
    }
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
el('restart-normal').addEventListener('click', () => requestRestart('restart'));
el('restart-audit').addEventListener('click', () => requestRestart('audit'));
el('display-pages-form').addEventListener('submit', event => {
  event.preventDefault();
  if (!el('display-pages').hidden) {
    const fields = { pages: displayPages.join('') };
    if (!el('page-wrap-field').hidden) fields.wrap = el('page-wrap').checked;
    save('pages.save', fields, 'Display pages saved.');
  }
});
el('wireless-form').addEventListener('submit', event => {
  event.preventDefault();
  const choice = el('wireless-choice').value;
  if (!el('wireless-priority').hidden && ['wifi', 'bluetooth'].includes(choice)) {
    save('wireless.save', { preferBluetooth: choice === 'bluetooth' }, 'Sync priority saved.');
  }
});
el('radios-form').addEventListener('submit', event => {
  event.preventDefault();
  if (el('wireless-radios').hidden) return;
  const wifiEnabled = el('wifi-enabled').checked;
  const bluetoothEnabled = el('bluetooth-enabled').checked;
  const disconnect = (!wifiEnabled && savedTransport.startsWith('Wi-Fi')) ||
    (!bluetoothEnabled && savedTransport === 'BLE');
  save('radios.save', { wifiEnabled, bluetoothEnabled }, disconnect
    ? 'Wireless connections saved. Reconnect by USB to turn the radio back on.'
    : 'Wireless connections saved.', disconnect);
});
el('sync-timing-form').addEventListener('submit', event => {
  event.preventDefault();
  const mode = el('sync-mode').value;
  if (!el('sync-timing').hidden && ['0', '1', '2', '3'].includes(mode)) {
    save('sync.save', { mode: Number(mode) }, 'Sync timing saved.');
  }
});
el('sleep-form').addEventListener('submit', event => {
  event.preventDefault();
  const minutes = el('sleep-minutes').valueAsNumber;
  if (!el('sleep-timeout').hidden && Number.isInteger(minutes) && minutes >= 1 && minutes <= 99) {
    save('sleep.save', { minutes }, 'Sleep timeout saved.');
  }
});
el('sleep-default').addEventListener('click', () => {
  if (!el('sleep-timeout').hidden) save('sleep.save', { minutes: 0 }, 'Default sleep timeout restored.');
});
el('access-login-form').addEventListener('submit', event => {
  event.preventDefault();
  if (el('access-login').hidden) return;
  const password = el('access-password').value;
  clearPasswords('access-password');
  save('auth.login', { password }, 'Signed in.');
});
el('note-protection-form').addEventListener('submit', event => {
  event.preventDefault();
  if (el('note-protection-form').hidden || savedTransport !== 'USB') return;
  save('auth.policy', { required: el('note-protection').checked }, 'Password protection saved.');
});
el('note-password-form').addEventListener('submit', event => {
  event.preventDefault();
  if (el('note-password').hidden) return;
  const old = el('note-password-old');
  const password = el('note-password-new');
  const repeat = el('note-password-repeat');
  const fields = { old: old.value, password: password.value, repeat: repeat.value };
  save('auth.save', fields, 'Note password saved.');
});
el('note-password-logout').addEventListener('click', () => save('auth.logout', {}, 'Signed out.'));
el('note-form').addEventListener('submit', event => {
  event.preventDefault();
  const fields = { name: el('note-name').value.trim(), language: el('note-language').value };
  if (!el('note-codex-limit-row').hidden) fields.weeklyCodexLimit = el('note-codex-limit').value === 'weekly';
  save('settings.save', fields, 'Note settings saved.');
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
  save('bridge.save', {
    name: el('bridge-name').value.trim(), weatherLocation: el('weather-location').value.trim(),
    holidayCountry: el('holiday-country').value
  }, 'Bridge settings saved.');
});
el('kubo-processing').addEventListener('change', () => {
  el('kubo-processing-fields').hidden = !el('kubo-processing').checked;
});
el('bridge-processing-form').addEventListener('submit', event => {
  event.preventDefault();
  save('bridge.processing.save', {
    mode: el('kubo-processing').checked ? 'kubo' : 'transcribe',
    taskList: el('kubo-task-list').value.trim(),
    calendarId: el('kubo-calendar').value.trim()
  }, 'Bridge processing parameters saved.');
});
el('kubo-check-login').addEventListener('click', () => run(async () => {
  status('Checking Codex sign-in…');
  await request('bridge.processing.check', {});
  status('Codex CLI is signed in with ChatGPT.');
}));
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
const values = (...ids) => () => ids.map(id => el(id).value);
trackDraft('settings.save', 'note-form', values('note-name', 'note-language', 'note-codex-limit'));
trackDraft('wifi.save', 'wifi-form', () => [el('wifi-name').value, Boolean(el('wifi-secret').value)]);
trackDraft('auth.policy', 'note-protection-form', () => [el('note-protection').checked]);
trackDraft('auth.save', 'note-password-form', () => newPasswordFields.map(id => Boolean(el(id).value)));
trackDraft('pages.save', 'display-pages-form', () => [displayPages.join(''), el('page-wrap').checked === true]);
trackDraft('sync.save', 'sync-timing-form', values('sync-mode'));
trackDraft('wireless.save', 'wireless-form', values('wireless-choice'));
trackDraft('radios.save', 'radios-form', () => [el('wifi-enabled').checked, el('bluetooth-enabled').checked]);
trackDraft('sleep.save', 'sleep-form', values('sleep-minutes'));
trackDraft('bridge.save', 'bridge-form', values('bridge-name', 'weather-location', 'holiday-country'), false);
trackDraft('bridge.processing.save', 'bridge-processing-form', () =>
  [el('kubo-processing').checked === true, el('kubo-task-list').value, el('kubo-calendar').value], false);
trackDraft('bridge.address', 'bridge-address-form', values('bridge-address'));
trackDraft('bridge.order', 'bridge-order', () => bridgeOrder.map(peer => peer.id));
trackDraft('trash.retention', 'trash-retention-form', values('trash-days'));

for (const id of ['access-password', 'wifi-secret', ...newPasswordFields]) {
  el(id + '-eye').addEventListener('click', () => showPassword(id, el(id).type === 'password'));
}
const strengthHelp = [
  'Weak: fewer than 10 characters, fewer than 4 different characters, a repeated block up to 5 characters long, or one of these patterns: 123456, abcdef, qwerty, password, heslo.',
  'Strong: at least 16 characters and 8 different characters after the weak checks. Fair: the remaining passwords.',
  'The estimate ignores case and surrounding spaces. Save accepts supported passwords at every rating.',
].join('\n\n');
for (const id of ['note-password-new', 'wifi-secret']) {
  el(id + '-strength-help').textContent = strengthHelp;
  el(id).addEventListener('input', () => showPasswordStrength(id));
}
for (const id of ['note-password-new', 'note-password-repeat']) {
  el(id).addEventListener('input', showPasswordMatch);
  el(id).addEventListener('change', showPasswordMatch);
}
refresh();
</script>
</body>
</html>
)PALAHTML"
