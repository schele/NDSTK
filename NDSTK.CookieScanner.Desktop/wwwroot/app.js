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
import { DiffView } from '/components/cs-diff-view.js';
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
const siteSelect = document.querySelector('#scan-site');
const urlInput = document.querySelector('#scan-url');
const maxPagesInput = document.querySelector('#scan-max-pages');
const localeInput = document.querySelector('#scan-locale');
const memberEmailInput = document.querySelector('#scan-member-email');
const memberPasswordInput = document.querySelector('#scan-member-password');
const clientIdInput = document.querySelector('#scan-client-id');
const dryRunInput = document.querySelector('#scan-dry-run');
const runButton = document.querySelector('#scan-run');
const cancelButton = document.querySelector('#scan-cancel');
const saveSiteButton = document.querySelector('#scan-save-site');
const deleteSiteButton = document.querySelector('#scan-delete-site');
const optionsDetails = document.querySelector('#scan-options');
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

const historyDiff = document.querySelector('#history-diff');
const historyDiffError = document.querySelector('#history-diff-error');

/** @type {DiffView} */
const historyDiffView = document.querySelector('#history-diff-view');

const lastScanValue = document.querySelector('#last-scan');
const keptScansValue = document.querySelector('#kept-scans');

/**
 * Everything a running scan must not let the operator change under it.
 *
 * The site dropdown is in here for a reason beyond tidiness: choosing a profile REFILLS the form,
 * so leaving it live during a run would let the fields the scan is reporting on be replaced under
 * it. Save site and Delete are deliberately absent - their disabled state has a second condition
 * each, so syncSiteButtons owns it rather than sharing it with this list.
 */
const inputs = [
  siteSelect, urlInput, maxPagesInput, localeInput, memberEmailInput,
  memberPasswordInput, clientIdInput, dryRunInput, runButton,
];

/** Whether a scan is running, as the host last reported it. */
let running = false;

/** Every saved profile, as the host last reported it, in the order the dropdown shows them. */
let sites = [];

/**
 * What "New site" means: the fields a profile that does not exist yet would have.
 *
 * The same defaults the window has always opened with - 25 pages, Swedish, dry run ON so the
 * obvious button to press cannot write to a live policy page. Kept here rather than as `value`
 * attributes in the markup because this is also what Delete and a failed lookup fall back to, and
 * three places reading a form's initial state out of the DOM would drift.
 */
const NEW_SITE = {
  url: '',
  maxPages: 25,
  locale: 'Sv',
  dryRun: true,
  memberEmail: '',
  memberPassword: '',
  clientId: '',
};

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

  // After the loop above and not in it: these two answer to more than `running`.
  syncSiteButtons();

  setStale(next);
}

/**
 * Whether Save site and Delete can do anything right now.
 *
 * Save needs a URL, because the URL is the profile's identity and a nameless profile is not a
 * thing the file can hold. Delete needs a profile selected, because "New site" is the absence of
 * one. Both need the scan to be over, for the same reason every field above does.
 *
 * Disabled rather than hidden, unlike Run and Cancel: those two are one action in two states, and
 * these are two actions that are momentarily unavailable. A row of buttons that changed length as
 * the form was typed into would be worse than a greyed one.
 */
function syncSiteButtons() {
  saveSiteButton.disabled = running || urlInput.value.trim() === '';
  deleteSiteButton.disabled = running || siteSelect.value === '';
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
 * "29 Aug, 03:09 - 3 entries" - or just the count, for a date the field cannot read.
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
 * Puts one profile's options into the fields - all of them, password included.
 *
 * The password IS filled, unlike every earlier version of this window: the host stores it encrypted
 * under the Windows user and sends it back decrypted, so a saved member scan is one click rather
 * than one retyped credential. See DashboardSettings for what that protects and what it does not.
 *
 * A locale the profile names but this page does not offer is left alone rather than assigned,
 * because assigning it would clear the select instead of choosing something.
 */
function fillForm(profile) {
  urlInput.value = profile.url ?? '';
  maxPagesInput.value = profile.maxPages ?? NEW_SITE.maxPages;
  memberEmailInput.value = profile.memberEmail ?? '';
  memberPasswordInput.value = profile.memberPassword ?? '';
  clientIdInput.value = profile.clientId ?? '';
  dryRunInput.checked = profile.dryRun ?? true;

  if (Array.from(localeInput.options).some((option) => option.value === profile.locale)) {
    localeInput.value = profile.locale;
  }
}

/** The saved profile a dropdown value names, or null for "New site" and for anything unknown. */
function profileFor(url) {
  return url === '' ? null : sites.find((profile) => profile.url === url) ?? null;
}

/** Everything the run card currently holds, in the shape both `run` and `saveSite` carry. */
function currentProfile() {
  const requested = Number.parseInt(maxPagesInput.value, 10);

  return {
    url: urlInput.value,
    // A blank or unparseable field is sent as zero rather than as NaN, which JSON writes as null and
    // the host cannot read at all: the host answers a number it cannot use with the console tool's
    // own default, and stores that default rather than the zero.
    maxPages: Number.isFinite(requested) ? requested : 0,
    locale: localeInput.value,
    dryRun: dryRunInput.checked,
    memberEmail: memberEmailInput.value,
    memberPassword: memberPasswordInput.value,
    clientId: clientIdInput.value,
  };
}

/**
 * Redraws the dropdown from the host's list and refills the form from whichever profile it names.
 *
 * The one place the saved sites reach the page, so `state` at startup and every `sites` answer to a
 * save, a delete or a starting run all land in the same code - there is no second path that could
 * leave the list and the fields describing different profiles.
 *
 * The form is refilled unconditionally, including after a save of the values already in it. That
 * costs nothing visible and is what makes the host's normalisation show: a URL is stored trimmed
 * and a blank page count is stored as 25, and a form still showing the untyped version would then
 * Save something subtly different the next time it was pressed.
 */
function showSites(nextSites, selectedUrl) {
  sites = Array.isArray(nextSites) ? nextSites : [];

  // Everything after the first, which is "New site" and belongs to the markup rather than to the
  // list. Rebuilt rather than diffed: the list is a handful of entries and a rebuild cannot get out
  // of step with what the host just sent.
  while (siteSelect.options.length > 1) {
    siteSelect.remove(1);
  }

  for (const profile of sites) {
    siteSelect.add(new Option(profile.url, profile.url));
  }

  const wanted = typeof selectedUrl === 'string' ? selectedUrl : '';

  // Checked against the options rather than assigned blind: a select handed a value it has no
  // option for selects NOTHING - not the first option - and the dropdown would render blank rather
  // than falling back to "New site".
  siteSelect.value = sites.some((profile) => profile.url === wanted) ? wanted : '';

  fillForm(profileFor(siteSelect.value) ?? NEW_SITE);

  // The URL field decides which scans the chart is about, and it has just been rewritten.
  showTrend();
  syncSiteButtons();
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

  // 'sites' in message, not a truthiness test: an empty array is a real answer - a first launch, or
  // the last profile just deleted - and it has to clear the dropdown rather than be ignored.
  if ('sites' in message) {
    showSites(message.sites, message.selectedUrl);
  }

  // Load's decrypt failures, one line each. Printed rather than shown beside the field they belong
  // to: they are about a file read that has already finished, and the log is where this window says
  // what happened rather than what is wrong with the form.
  if (Array.isArray(message.warnings)) {
    for (const warning of message.warnings) {
      logPanel.append('warning', warning);
    }
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

  // Spread flat, because `run` carries its options at the top level while `saveSite` nests them
  // under `profile`. Both read the form through the same function on purpose: a run and the profile
  // that run writes must describe the same thing, and two readers of the same eight fields is
  // exactly how they would come to differ.
  post({ type: 'run', ...currentProfile() });
}

/**
 * Saves what the form holds as the profile for the URL it names.
 *
 * Editing the URL of a selected profile and pressing this is a "save as": the new URL matches no
 * saved profile, so a second one appears and the original stays until it is deleted. That is the
 * useful reading - copying a set of options from staging to production is the common case - and the
 * host's Upsert is where the same decision is written down.
 */
function requestSaveSite() {
  if (saveSiteButton.disabled) {
    return;
  }

  post({ type: 'saveSite', profile: currentProfile() });
}

/**
 * Forgets the selected profile.
 *
 * The dropdown's value, not the URL field's: the field can have been edited since the profile was
 * chosen, and deleting whatever happens to be typed is not what a Delete beside a dropdown means.
 */
function requestDeleteSite() {
  if (deleteSiteButton.disabled) {
    return;
  }

  post({ type: 'deleteSite', url: siteSelect.value });
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
saveSiteButton.addEventListener('click', requestSaveSite);
deleteSiteButton.addEventListener('click', requestDeleteSite);

// Picking a site replaces the whole form, including the URL - the dropdown is the profile, not a
// bookmark for one field. The host is not told: a selection is a local act until something is saved
// or run, so browsing the saved sites cannot rewrite settings.json.
siteSelect.addEventListener('change', () => {
  fillForm(profileFor(siteSelect.value) ?? NEW_SITE);

  // The URL field lives inside Options now. A saved site needs nothing typed, so the section can
  // stay closed; "New site" has nothing to run yet, and closing it back on the operator would leave
  // them with an empty dropdown and no visible place to put the address.
  if (siteSelect.value === '') {
    optionsDetails.open = true;
  }

  showTrend();
  syncSiteButtons();
});

// The chart is about the site in the box, so it follows the box rather than the last run: typing a
// different site there shows that site's trend without having to scan it first. Save site follows
// the same keystrokes, because an empty URL is nothing to save under.
urlInput.addEventListener('input', () => {
  showTrend();
  syncSiteButtons();
});

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

// PostHistory re-reads the folder on every request rather than caching it, specifically so a scan
// run from a terminal while this window is open shows up here - and that only holds if the History
// page actually asks again on every visit, not just once at startup.
window.addEventListener('page-shown', (event) => {
  if (event.detail?.page === 'history') {
    post({ type: 'listHistory' });
  }
});

/**
 * The one path a `scan`/`error` answer is allowed to render, or null when that would not be exactly
 * one thing. `loadScan` carries no correlation id of its own, so this is what lets a reply that
 * arrives after the operator has already moved on get recognised as stale and dropped - see the
 * `scan`/`error` cases below.
 */
let selectedHistoryPath = null;

/**
 * Every path currently checked, as the list last reported it - what a `diff`/`error` answer to
 * `compare` is measured against.
 *
 * A separate variable from the one above rather than a derivation of it, because the two answer
 * different counts: exactly two is precisely the state in which `selectedHistoryPath` is null, so a
 * compare answer matched against that single path would be dropped every single time.
 */
let selectedHistoryPaths = [];

/**
 * Whether an answer's echoed pair is the pair still checked, regardless of the order it was asked
 * in.
 *
 * The host echoes `paths` in the order the page sent them - click order - so a comparison against a
 * selection the operator built the other way round has to ignore order. This is deliberately not the
 * ordering that decides "appeared": the host does that by completion time, and the two questions
 * must not be answered with the same list.
 */
function isSelectedPair(paths) {
  if (Array.isArray(paths) === false || paths.length !== 2 || selectedHistoryPaths.length !== 2) {
    return false;
  }

  const answered = [...paths].sort();
  const selected = [...selectedHistoryPaths].sort();

  return answered.every((path, index) => path === selected[index]);
}

/**
 * What the two counts this window answers mean: one selected is a scan to look at, two are a pair to
 * compare. Anything else asks the host for nothing.
 *
 * A pane is only ever HIDDEN here, never shown - the answer that arrives is what shows it. Revealing
 * one on the selection instead would put the previous scan's rows, or the previous pair's diff, on
 * screen for however long the host takes to read the files.
 */
historyList?.addEventListener('selection-changed', (event) => {
  const paths = Array.isArray(event.detail?.paths) ? event.detail.paths : [];

  selectedHistoryPaths = paths;
  selectedHistoryPath = paths.length === 1 ? paths[0] : null;

  historyList.note = describeSelection(paths);

  if (paths.length !== 1) {
    historyDetail.hidden = true;
  }

  // Both panes go away for every count that is not their own, which is what returns the pane to the
  // detail table when a diff is showing and the operator unchecks one of the two rows.
  if (paths.length !== 2) {
    historyDiff.hidden = true;
  }

  if (paths.length === 1) {
    post({ type: 'loadScan', path: paths[0] });

    return;
  }

  if (paths.length === 2) {
    post({ type: 'compare', pathA: paths[0], pathB: paths[1] });
  }
});

/**
 * What the current selection is about to do, in one line for the list to show.
 *
 * The words live here and not in the component, exactly as cs-stat-tile's do: what a count of
 * checked boxes MEANS is this module's decision, and the list only ever reports what is checked.
 * Without this the comparison is undiscoverable - two checkboxes look like two checkboxes.
 */
function describeSelection(paths) {
  if (paths.length === 0) {
    return 'Tick one scan to see what it found, or two to compare them.';
  }

  if (paths.length === 1) {
    return 'One selected - its findings are below. Tick a second to compare the two.';
  }

  if (paths.length === 2) {
    return 'Two selected - the comparison is below.';
  }

  return `${paths.length} selected - a comparison is between exactly two, so untick down to two.`;
}

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

    // The answer to saveSite and to deleteSite - and to a run STARTING, which saves the profile it
    // is about to run with before it starts. Always the host's whole list rather than the one entry
    // that changed, so the page never has to reconstruct what the file now holds from what it asked
    // for.
    case 'sites':
      showSites(message.sites, message.selectedUrl);

      break;

    case 'history':
      // Kept whole and filtered on the way to the chart, rather than filtered here: the History
      // page lists every site's scans, and re-asking the host for the same folder because the URL
      // field changed would be a file read per keystroke.
      history = Array.isArray(message.entries) ? message.entries : [];

      showTrend();
      showHistoryFooter();

      historyList.entries = history;
      historyList.note = describeSelection(selectedHistoryPaths);

      break;

    // The answer to loadScan for a file that read back cleanly. Rendered into the SAME element the
    // Scan page uses - see cs-findings-table.js - so the colouring rule the exit code has to agree
    // with exists in exactly one place.
    //
    // Dropped, not rendered, when `message.path` is not what is still selected: loadScan carries no
    // correlation id, so an answer can arrive after the operator has already unchecked that row,
    // moved to a different one, or watched it fall out of a refreshed list. Rendering it anyway
    // would force the pane back open on data for a scan that is no longer the one thing selected -
    // exactly the invariant above, reached through the asynchronous door instead of the synchronous
    // one.
    case 'scan':
      if (message.path !== selectedHistoryPath) {
        break;
      }

      historyError.hidden = true;
      historyFindingsTable.result = message.result;
      historyDetail.hidden = false;

      break;

    // The answer to compare for two files that both read back cleanly. Ordered by completion time
    // by the host - see PostDiff - so "appeared" means the same thing whichever row was ticked
    // first, and nothing here re-derives it.
    //
    // Guarded the same way `scan` is, against the pair rather than the single path: `compare` has no
    // correlation id either, and two checked rows is exactly the state in which no single path is
    // selected. `paths` is echoed in the order it was asked and compared order-insensitively,
    // because the operator can tick the same two rows in either order and still be looking at the
    // same comparison.
    case 'diff':
      if (isSelectedPair(message.paths) === false) {
        break;
      }

      historyDiffError.hidden = true;
      historyDiffView.diff = message;
      historyDiff.hidden = false;

      break;

    // loadScan or compare answering a file that would not read back - deleted or corrupted since
    // the list was drawn. Shown inline rather than left silent: the operator asked for something
    // specific and is owed a reason it did not appear.
    //
    // Which request it answers is read off its SHAPE - one `path` for loadScan, two `paths` for
    // compare - rather than off a separate field naming the command. The echo is already the
    // correlation, and a second discriminator saying the same thing is a second thing that can
    // disagree with it. Each branch then applies its own staleness rule, for the same reason `scan`
    // and `diff` have theirs: an error for a selection that has already moved on must not force a
    // pane back open.
    case 'error':
      if (Array.isArray(message.paths)) {
        if (isSelectedPair(message.paths) === false) {
          break;
        }

        historyDiffView.diff = null;
        historyDiffError.textContent = message.message;
        historyDiffError.hidden = false;
        historyDiff.hidden = false;

        break;
      }

      if (message.path !== selectedHistoryPath) {
        break;
      }

      historyFindingsTable.result = null;
      historyError.textContent = message.message;
      historyError.hidden = false;
      historyDetail.hidden = false;

      break;

    // Nothing else: every envelope the host posts has an arm above. Ignored rather than logged, the
    // same as an unrecognised command on the host's side - a type this build has never heard of is a
    // mismatched pair of halves, not a fault worth a line in the operator's log.
    default:
      break;
  }
});

// Before ready rather than after it: the answer that fills the dropdown is milliseconds away, but
// until it arrives Save site would be a live button over an empty URL field. One call settles both
// buttons from the state the markup actually starts in.
syncSiteButtons();

// Last, and only once the page can answer: this is what releases the envelopes the host buffered
// while the window was still loading.
post({ type: 'ready' });

// After ready and never before it: the host only starts delivering once ready has arrived, and the
// two are answered in the order they were sent, so the profiles that name the site reach the page
// ahead of the history that has to be filtered by it.
post({ type: 'listHistory' });
