/*
    The shell: the router, and the bridge to the window that hosts it.

    Hash routing, never the history API: the page is served from a virtual origin by the window's own
    request handler, so a pushState route would 404 the moment anything reloaded it. `#scan` is not a
    cosmetic choice - it is the only kind of URL this document can survive.
*/

/*
    Imported by name rather than for its side effect alone. The element registers itself when this
    module loads either way, but the asset test crawls rooted import specifiers, and a side-effect
    import names no binding for it to see - which would leave a renamed component as a blank panel
    with a 404 nobody notices, the exact failure that test exists to catch.

    Keep example import syntax out of these comments, too: the crawl is a regular expression over the
    file's text, and it cannot tell a specifier in a comment from one the module really imports.
*/
import { LogPanel } from '/components/cs-log-panel.js';

const FALLBACK = 'scan';

const links = Array.from(document.querySelectorAll('.nav-link[data-page]'));
const pages = Array.from(document.querySelectorAll('.page[data-page]'));

let routed = false;

/** The page the current hash names, or the fallback if it names nothing this shell knows. */
function requestedPage() {
  const name = location.hash.replace(/^#/, '');

  return pages.some((page) => page.dataset.page === name) ? name : FALLBACK;
}

function show(name) {
  let shown = null;

  for (const page of pages) {
    const active = page.dataset.page === name;

    page.hidden = !active;

    if (active) {
      shown = page;
    }
  }

  for (const link of links) {
    if (link.dataset.page === name) {
      link.setAttribute('aria-current', 'page');
    } else {
      // Removed rather than set to "false": aria-current="false" is still an announced value.
      link.removeAttribute('aria-current');
    }
  }

  if (shown === null) {
    return;
  }

  // Not on the first route: focusing the heading as the window opens would be a jump nobody asked
  // for. On every later change it is the only signal a keyboard reader gets that the page moved.
  if (routed) {
    shown.focus();
  }

  routed = true;

  // Composed and bubbling so a component inside a shadow root can hear it on `document` too. The
  // pages listen for this instead of polling, because a page that is never opened should not work.
  shown.dispatchEvent(new CustomEvent('page-shown', {
    detail: { page: name },
    bubbles: true,
    composed: true,
  }));
}

function route() {
  const name = requestedPage();

  // An empty or unknown hash is rewritten so the address and the page never disagree. replace()
  // rather than assignment: a corrected hash is not somewhere the user should be able to go back to.
  // This fires hashchange, which re-enters here once with the hash already correct.
  if (location.hash !== `#${name}`) {
    location.replace(`#${name}`);

    return;
  }

  show(name);
}

window.addEventListener('hashchange', route);

route();

/* ---------------------------------------------------------------- the host bridge

    One JSON envelope in each direction: `window.chrome.webview.postMessage` out, the `message` event
    in. Every later page adds message types here and nothing else - there is one transport, and this
    is it.

    `chrome.webview` exists only inside the window that hosts this page, so every use of it is
    optional-chained: opened in an ordinary browser the page still renders, it simply has nobody to
    talk to.
*/

const host = window.chrome?.webview;

const scanPage = pages.find((page) => page.dataset.page === 'scan');

const form = document.querySelector('#scan-form');
const urlInput = document.querySelector('#scan-url');
const maxPagesInput = document.querySelector('#scan-max-pages');
const localeInput = document.querySelector('#scan-locale');
const memberEmailInput = document.querySelector('#scan-member-email');
const memberPasswordInput = document.querySelector('#scan-member-password');
const clientIdInput = document.querySelector('#scan-client-id');
const dryRunInput = document.querySelector('#scan-dry-run');
const runButton = document.querySelector('#scan-run');
const cancelButton = document.querySelector('#scan-cancel');
const secretStatus = document.querySelector('#secret-status');

/** @type {LogPanel} */
const logPanel = document.querySelector('#scan-log');

/** Everything a running scan must not let the operator change under it. */
const inputs = [
  urlInput, maxPagesInput, localeInput, memberEmailInput,
  memberPasswordInput, clientIdInput, dryRunInput, runButton,
];

function post(message) {
  host?.postMessage(message);
}

function setRunning(running) {
  for (const input of inputs) {
    input.disabled = running;
  }

  // Swapped rather than both shown: there is exactly one thing to do at any moment, and a disabled
  // Cancel sitting beside a disabled Run says nothing about which.
  runButton.hidden = running;
  cancelButton.hidden = !running;
  cancelButton.disabled = false;
}

/**
 * Puts the remembered options back into the fields.
 *
 * The member password is left empty, and there is nothing in the settings to fill it from - see
 * DashboardSettings. A locale the settings name but this page does not offer is left alone rather
 * than assigned, because assigning it would clear the select instead.
 */
function restore(settings) {
  urlInput.value = settings.url ?? '';
  maxPagesInput.value = settings.maxPages ?? 25;
  memberEmailInput.value = settings.memberEmail ?? '';
  clientIdInput.value = settings.clientId ?? '';
  dryRunInput.checked = settings.dryRun ?? true;

  if (Array.from(localeInput.options).some((option) => option.value === settings.locale)) {
    localeInput.value = settings.locale;
  }
}

/**
 * A state message carries only what changed, so each field is applied only when it is there: the
 * host posts `{ running }` around every scan and the fuller answer to `ready` once, at startup.
 */
function applyState(message) {
  if ('running' in message) {
    setRunning(message.running);
  }

  if ('secretIsSet' in message) {
    // The variable's name comes from the host so it is spelled in one place - the same constant the
    // engine reads it with. Left in the ordinary muted colour deliberately: report-only is a
    // supported mode, not a fault.
    secretStatus.textContent = message.secretIsSet
      ? `${message.secretVariable} is set`
      : `${message.secretVariable} is not set - write-back will be skipped`;
  }

  if (message.settings) {
    restore(message.settings);
  }
}

function requestRun() {
  if (runButton.hidden || runButton.disabled) {
    return;
  }

  // Cleared here, so one run's log is one run's log. The scrollback survives the end of a scan - it
  // is the next scan that replaces it, which is the only moment the old lines stop being the answer
  // to what is on screen.
  logPanel.clear();

  const requested = Number.parseInt(maxPagesInput.value, 10);

  post({
    type: 'run',
    url: urlInput.value,
    // A blank or unparseable field is sent as zero rather than as NaN, which JSON writes as null and
    // the host cannot read at all: the host answers a number it cannot use with the console tool's
    // own default.
    maxPages: Number.isFinite(requested) ? requested : 0,
    locale: localeInput.value,
    memberEmail: memberEmailInput.value,
    memberPassword: memberPasswordInput.value,
    clientId: clientIdInput.value,
    dryRun: dryRunInput.checked,
  });
}

function requestCancel() {
  if (cancelButton.hidden || cancelButton.disabled) {
    return;
  }

  // Disabled on the way out, not on the way back: the engine only observes a cancel between passes,
  // and a second click in the meantime would ask for something already asked for.
  cancelButton.disabled = true;

  post({ type: 'cancel' });
}

form.addEventListener('submit', (event) => {
  event.preventDefault();

  requestRun();
});

cancelButton.addEventListener('click', requestCancel);

// Ctrl+Enter runs and Escape cancels, but only while the Scan page is the one on screen: a shortcut
// that fires from another page would act on a form the operator cannot see.
window.addEventListener('keydown', (event) => {
  if (scanPage?.hidden !== false) {
    return;
  }

  if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
    event.preventDefault();

    requestRun();

    return;
  }

  if (event.key === 'Escape') {
    requestCancel();
  }
});

host?.addEventListener('message', (event) => {
  // Already an object: the host posts with PostWebMessageAsJson, so WebView2 has parsed it by the
  // time it arrives.
  const message = event.data;

  switch (message?.type) {
    case 'log':
      logPanel.append(message.level, message.message);

      break;

    case 'state':
      applyState(message);

      break;

    // Everything else - result, history, scan, diff, error - belongs to a page that does not exist
    // yet. Ignored rather than logged: an unhandled type is a task not written, not a fault.
    default:
      break;
  }
});

// Last, and only once the page can answer: this is what releases the envelopes the host buffered
// while the window was still loading.
post({ type: 'ready' });
