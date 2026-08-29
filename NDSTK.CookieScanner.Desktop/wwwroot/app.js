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
import { FindingsTable } from '/components/cs-findings-table.js';
import { HistoryList } from '/components/cs-history-list.js';
import { LogPanel } from '/components/cs-log-panel.js';
import { StatTile } from '/components/cs-stat-tile.js';
import { TrendChart } from '/components/cs-trend-chart.js';

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

const findings = document.querySelector('#scan-findings');

/** @type {FindingsTable} */
const findingsTable = document.querySelector('#scan-findings-table');

const trendCard = document.querySelector('#trend-card');

/** @type {TrendChart} */
const trendChart = document.querySelector('#scan-trend');

/** @type {Record<string, StatTile>} */
const tiles = {
  entries: document.querySelector('#tile-entries'),
  violations: document.querySelector('#tile-violations'),
  review: document.querySelector('#tile-review'),
  expected: document.querySelector('#tile-expected'),
};

/** @type {HistoryList} */
const historyList = document.querySelector('#history-list');

const historyDetail = document.querySelector('#history-detail');
const historyError = document.querySelector('#history-error');

/** @type {FindingsTable} */
const historyFindingsTable = document.querySelector('#history-findings-table');

const lastScanValue = document.querySelector('#last-scan');
const keptScansValue = document.querySelector('#kept-scans');

/** Everything a running scan must not let the operator change under it. */
const inputs = [
  urlInput, maxPagesInput, localeInput, memberEmailInput,
  memberPasswordInput, clientIdInput, dryRunInput, runButton,
];

/** Whether a scan is running, as the host last reported it. */
let running = false;

/** Every kept scan the host has told us about, newest first, for every site. */
let history = [];

/**
 * ScanHistory.Keep, mirrored here rather than sent on the envelope: the count the sidebar reads is
 * the length of the list the host already sent, and this is only the denominator "N of 50" needs.
 */
const HISTORY_KEEP = 50;

function post(message) {
  host?.postMessage(message);
}

function setRunning(next) {
  running = next;

  for (const input of inputs) {
    input.disabled = next;
  }

  // Swapped rather than both shown: there is exactly one thing to do at any moment, and a disabled
  // Cancel sitting beside a disabled Run says nothing about which.
  runButton.hidden = next;
  cancelButton.hidden = !next;
  cancelButton.disabled = false;

  setStale(next);
}

/**
 * De-emphasises the previous scan's numbers while the next scan runs.
 *
 * They stay on screen: the run you are about to compare against is exactly the one that pressing
 * Run would otherwise throw away, and a scan takes the best part of a minute. Dimmed rather than
 * left at full strength, because tiles and a chart that still read as current would be claiming to
 * describe the run in progress.
 *
 * aria-busy carries the same thing to a reader who gets nothing from opacity - it is the only cue
 * there is otherwise, and this window does not convey anything by appearance alone.
 */
function setStale(stale) {
  for (const region of [trendCard, findings]) {
    region.classList.toggle('is-stale', stale);

    if (stale) {
      region.setAttribute('aria-busy', 'true');
    } else {
      // Removed rather than set to "false": an element that is not busy should say nothing about
      // it, the same reasoning as aria-current on the navigation.
      region.removeAttribute('aria-busy');
    }
  }
}

/**
 * The site the URL field names, in a form two spellings of the same site agree on.
 *
 * The history records a site as the scanned Uri's own text - "https://localhost:44351/", with the
 * trailing slash Uri adds - while the field holds whatever was typed. Comparing them raw would
 * leave the chart permanently empty for the site sitting in the box.
 */
function siteKey(site) {
  return typeof site === 'string' ? site.trim().toLowerCase().replace(/\/+$/, '') : '';
}

/**
 * Hands the chart the scans for the site currently in the URL field.
 *
 * The filter lives here rather than in the component because the URL field is this module's, and a
 * chart that reached into the form for it would be a second thing that has to know where the field
 * is. What the component decides is how much of what it is given it can draw.
 */
function showTrend() {
  const wanted = siteKey(urlInput.value);

  trendChart.entries = wanted === ''
    ? []
    : history.filter((entry) => siteKey(entry?.site) === wanted);
}

/**
 * Fills the sidebar's two footer values from the same `history` message the trend and the History
 * page both read from: the last scan's own time and entry count, and how many of the fifty kept
 * scans are on disk right now.
 */
function showHistoryFooter() {
  const [latest] = history;

  lastScanValue.textContent = latest ? describeScan(latest) : 'No scans yet';
  keptScansValue.textContent = `${history.length} of ${HISTORY_KEEP} kept`;
}

/**
 * "29 Aug 2026, 03:09 - 3 entries" - or just the count, for a date the field cannot read.
 *
 * A plain hyphen, not the middle dot this file already has one of (in the violations hint below):
 * a second literal non-ASCII byte pair here would be one more thing for a careless encoding
 * round-trip on this file to mangle, for a separator this string has no real need to match.
 */
function describeScan(entry) {
  const at = new Date(entry?.completedAt);
  const entries = Number.isFinite(entry?.entryCount) ? entry.entryCount : 0;
  const noun = entries === 1 ? 'entry' : 'entries';

  if (Number.isNaN(at.getTime())) {
    return `${entries} ${noun}`;
  }

  const when = at.toLocaleString('en-GB', {
    day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
  });

  return `${when} - ${entries} ${noun}`;
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
    const wasRunning = running;

    setRunning(message.running);

    // Asked for when the scan ENDS, not when its result arrives: the host writes the history file
    // after it posts the result and before it posts this state, so a request made on the result
    // would race that write and draw a chart missing the run that had just finished.
    if (wasRunning && message.running === false) {
      post({ type: 'listHistory' });
    }
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

    // The remembered URL is what decides which scans the chart is about, so the chart is re-filtered
    // the moment that field is filled - whichever of the two messages arrives first.
    showTrend();
  }
}

/**
 * Everything a finished scan puts on screen: the summary in the log, the four counts, the table.
 *
 * Nothing here recomputes what the host already decided. Each count is the length of a list on the
 * result and the summary is the host's own text, so the window cannot end up telling a different
 * story from the report on disk.
 */
function showResult(message) {
  // Appended as ONE entry rather than one line at a time, so the blank lines survive: the panel
  // renders a line as an <li>, and an <li> holding an empty string produces no line box and so no
  // height. Its white-space is pre-wrap, so one multi-line string keeps both the blank lines and the
  // leading spaces that put the json path under the markdown one.
  if (Array.isArray(message.summary) && message.summary.length > 0) {
    logPanel.append('info', message.summary.join('\n'));
  }

  const scan = message.scan;

  // The host always sends one. A result without it would take the tiles down with it, and the
  // summary above is worth keeping either way.
  if (scan === undefined || scan === null) {
    return;
  }

  const added = scan.outcome?.added?.length;

  tiles.entries.value = scan.candidates.length;
  // No outcome means the write-back was never attempted - not configured, or nothing to send - and
  // a hint reading "0 added last run" would be an answer to a question nobody asked.
  tiles.entries.hint = added === undefined ? '' : `${added} added last run`;

  tiles.violations.value = scan.violations.length;
  // The exit code is not recomputed here: 1 is what a violation means, and the tile says so in the
  // same breath as the number, because the number alone does not tell the operator the run failed.
  tiles.violations.hint = scan.violations.length > 0 ? 'fails the run · exit 1' : 'none';

  tiles.review.value = scan.candidates.filter((candidate) => candidate.flag === 'NeedsReview').length;
  tiles.expected.value = scan.expectedButNotObserved.length;

  findingsTable.result = scan;

  findings.hidden = false;
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

// The chart is about the site in the box, so it follows the box rather than the last run: typing a
// different site there shows that site's trend without having to scan it first.
urlInput.addEventListener('input', showTrend);

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

/**
 * Exactly one selected is the only count this task answers: `loadScan` asks the host to read that
 * one file. Zero or several both put the detail pane away rather than leave it showing a scan that
 * is no longer the one thing selected - two is Task 8's to answer, not this one's.
 */
historyList?.addEventListener('selection-changed', (event) => {
  const paths = Array.isArray(event.detail?.paths) ? event.detail.paths : [];

  if (paths.length !== 1) {
    historyDetail.hidden = true;

    return;
  }

  post({ type: 'loadScan', path: paths[0] });
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

    case 'result':
      showResult(message);

      break;

    case 'history':
      // Kept whole and filtered on the way to the chart, rather than filtered here: the History
      // page lists every site's scans, and re-asking the host for the same folder because the URL
      // field changed would be a file read per keystroke.
      history = Array.isArray(message.entries) ? message.entries : [];

      showTrend();
      showHistoryFooter();

      historyList.entries = history;

      break;

    // The answer to loadScan for a file that read back cleanly. Rendered into the SAME element the
    // Scan page uses - see cs-findings-table.js - so the colouring rule the exit code has to agree
    // with exists in exactly one place.
    case 'scan':
      historyError.hidden = true;
      historyFindingsTable.result = message.result;
      historyDetail.hidden = false;

      break;

    // loadScan (and later, compare) answering a file that would not read back - deleted or
    // corrupted since the list was drawn. Shown inline rather than left silent: the operator asked
    // for one specific scan and is owed a reason it did not appear.
    case 'error':
      historyFindingsTable.result = null;
      historyError.textContent = message.message;
      historyError.hidden = false;
      historyDetail.hidden = false;

      break;

    // Everything else - diff - belongs to a page that does not exist yet. Ignored rather than
    // logged: an unhandled type is a task not written, not a fault.
    default:
      break;
  }
});

// Last, and only once the page can answer: this is what releases the envelopes the host buffered
// while the window was still loading.
post({ type: 'ready' });

// After ready and never before it: the host only starts delivering once ready has arrived, and the
// two are answered in the order they were sent, so the settings that name the site reach the page
// ahead of the history that has to be filtered by it.
post({ type: 'listHistory' });
