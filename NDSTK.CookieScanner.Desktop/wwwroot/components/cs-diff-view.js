import { LitElement, html, nothing } from '/vendor/lit.js';

/*
    What changed between two kept scans, as the host worked it out.

    Deliberately dumb about arithmetic, the same way cs-stat-tile is: the three groups are the three
    lists the host sent, in the order it sent them, and nothing here re-derives what appeared or what
    is newer. The host ordered the pair by completion time before it diffed - that is what makes
    "appeared" mean "in the newer scan" no matter which row was ticked first - and a second opinion
    about that here would be a second place for the rule to be wrong.

    What this element DOES own is the wording, including the banner. A diff is only honest next to
    the options the two runs were made with: a member scan finds the sign-in cookie and a public scan
    does not, which is a fact about the run rather than about the site. The host sends the two option
    summaries; naming the difference out loud is this file's job, alongside every other piece of text
    in this window.
*/
export class DiffView extends LitElement {
  static properties = {
    /**
     * One `diff` envelope, or null.
     *
     * Property only, never an attribute: it is an object, and an attribute would mean serialising
     * two whole scans' worth of changes into the markup and parsing it back out again.
     */
    diff: { attribute: false },
  };

  constructor() {
    super();

    this.diff = null;
  }

  /**
   * Renders into the element itself rather than into a shadow root - the same reasoning as
   * cs-findings-table and cs-history-list: `.data-table`, `.pill` and `.muted` are the design
   * system's, and a copy of them behind a shadow boundary is a copy free to drift.
   */
  createRenderRoot() {
    return this;
  }

  render() {
    const diff = this.diff;

    if (diff === null || typeof diff !== 'object') {
      return html`<p class="muted">No comparison loaded.</p>`;
    }

    const appeared = list(diff.appeared);
    const disappeared = list(diff.disappeared);
    const recategorised = list(diff.recategorised);

    // All three empty is one sentence rather than three headings over three "nothing" lines: the
    // answer to "what changed" is "nothing", and saying it three times says it less clearly.
    const unchanged =
      appeared.length === 0 && disappeared.length === 0 && recategorised.length === 0;

    return html`
      ${header(diff)}
      ${banners(diff)}
      ${unchanged
        ? html`<p class="diff-unchanged">Nothing changed between these two scans.</p>`
        : html`
            <div class="diff-groups">
              ${entryGroup('Appeared', 'teal', appeared, 'Nothing appeared.')}
              ${entryGroup('Disappeared', 'red', disappeared, 'Nothing disappeared.')}
              ${changeGroup('Recategorised', 'blue', recategorised, 'Nothing was recategorised.')}
            </div>
          `}
    `;
  }
}

/**
 * Which two scans this is, by completion time, and which way round they are.
 *
 * The sentence under them is not decoration: "appeared" is only unambiguous once the reader knows
 * which of the two the word is about, and the pair is ordered by the clock rather than by the order
 * the boxes were ticked.
 */
function header(diff) {
  return html`
    <header class="diff-head">
      <div class="diff-sides">
        <p class="diff-side">
          <span class="eyebrow">Older</span>
          <span class="diff-side-value">${sideLine(diff.older)}</span>
        </p>
        <p class="diff-side">
          <span class="eyebrow">Newer</span>
          <span class="diff-side-value">${sideLine(diff.newer)}</span>
        </p>
      </div>
      <p class="muted">
        Appeared means present in the newer scan and not in the older one. The two are ordered by
        when they finished, not by the order they were picked.
      </p>
    </header>
  `;
}

/** One side of the pair: when it finished, what it was against, and how much it found. */
function sideLine(side) {
  const when = completed(side?.completedAt);
  const entries = whole(side?.entryCount);
  const noun = entries === 1 ? 'entry' : 'entries';
  const where = typeof side?.site === 'string' ? side.site : '';

  return [when, where, `${entries} ${noun}`].filter((part) => part !== '').join(' - ');
}

/**
 * The warnings that explain a difference the rows alone would blame on the site.
 *
 * Rendered above the groups, because they change what the groups mean. Every one of them says what
 * it means in words: the tint is a second cue and never the only one.
 */
function banners(diff) {
  const shown = [];

  if (diff.siteDiffers === true) {
    shown.push(html`
      <p class="diff-banner diff-banner--red">
        These two scans are of different sites - ${site(diff.older)} and ${site(diff.newer)}. This is
        a comparison of two sites rather than of one site over time, so almost every row below is
        that difference rather than a change.
      </p>
    `);
  }

  // Not recorded outranks differ: without both summaries the host cannot have decided they differ,
  // and saying "the options matched" about a file that never wrote any down would be a claim
  // nothing supports.
  if (diff.optionsKnown === false) {
    shown.push(html`
      <p class="diff-banner diff-banner--amber">
        The options were not recorded for one of these scans, so this comparison cannot say whether
        the two ran the same way. Anything below may be a difference in how the scan was run rather
        than a change to the site.
      </p>
    `);
  } else if (diff.optionsDiffer === true) {
    shown.push(html`
      <p class="diff-banner diff-banner--amber">
        These scans ran with different options: ${optionDifferences(diff)}. A finding below that
        looks like a change to the site may be that difference instead.
      </p>
    `);
  }

  return shown.length === 0 ? nothing : shown;
}

/**
 * Every recorded option the two runs disagree on, in plain words.
 *
 * Member sign-in first, deliberately: it is the one that adds a whole cookie to one side of the
 * diff, and it is the reason the scan record carries these summaries at all.
 */
function optionDifferences(diff) {
  const older = diff.older?.options ?? {};
  const newer = diff.newer?.options ?? {};
  const parts = [];

  if (older.memberScanEnabled !== newer.memberScanEnabled) {
    parts.push(`member sign-in ${onOff(older.memberScanEnabled)} in the older scan, `
      + `${onOff(newer.memberScanEnabled)} in the newer`);
  }

  if (older.maxPages !== newer.maxPages) {
    parts.push(`max pages ${whole(older.maxPages)} in the older scan, `
      + `${whole(newer.maxPages)} in the newer`);
  }

  if (older.locale !== newer.locale) {
    parts.push(`locale ${older.locale} in the older scan, ${newer.locale} in the newer`);
  }

  if (older.dryRun !== newer.dryRun) {
    parts.push(`dry run ${onOff(older.dryRun)} in the older scan, ${onOff(newer.dryRun)} in the newer`);
  }

  // The host decided they differ, so this cannot normally come back empty - only a summary carrying
  // a field this build has never heard of would get here. Said plainly rather than left as a banner
  // that announces a difference and then names none.
  return parts.length === 0
    ? 'in a way this window is too old to name'
    : parts.join('; ');
}

/** Appeared and Disappeared: whole candidates, in the shape the findings table shows them. */
function entryGroup(title, tone, entries, empty) {
  return group(
    title,
    tone,
    entries,
    empty,
    html`
      <tr>
        <th scope="col">Name</th>
        <th scope="col">Storage</th>
        <th scope="col">Category</th>
        <th scope="col">Duration</th>
      </tr>
    `,
    (entry) => html`
      <tr>
        <td class="mono">${entry?.name}</td>
        <td>${entry?.storageType}</td>
        <td>${entry?.category}</td>
        <td>${entry?.duration}</td>
      </tr>
    `);
}

/**
 * Recategorised: one cookie that is in both scans under two different categories.
 *
 * "Was" and "Now" rather than the payload's own "from" and "to": the reader is looking at an older
 * scan and a newer one, and those two words say which is which without having to be told.
 */
function changeGroup(title, tone, changes, empty) {
  return group(
    title,
    tone,
    changes,
    empty,
    html`
      <tr>
        <th scope="col">Name</th>
        <th scope="col">Was</th>
        <th scope="col">Now</th>
      </tr>
    `,
    (change) => html`
      <tr>
        <td class="mono">${change?.name}</td>
        <td>${change?.from}</td>
        <td>${change?.to}</td>
      </tr>
    `);
}

/**
 * One labelled group: the word, the count, and either the rows or the one sentence that replaces
 * them.
 *
 * The heading keeps its count even when the group is empty. The tint is what the eye finds first and
 * it carries nothing on its own - the word says which group this is and the pill says how much is in
 * it, so a monochrome screenshot or a screen reader loses nothing.
 */
function group(title, tone, items, empty, columns, row) {
  const label = `${items.length} ${items.length === 1 ? 'entry' : 'entries'}`;

  return html`
    <section class="diff-group diff-group--${tone}">
      <h3 class="diff-group-title">
        ${title}
        <span class="pill diff-count">${label}</span>
      </h3>
      ${items.length === 0
        ? html`<p class="muted">${empty}</p>`
        : html`
            <div class="diff-scroll">
              <table class="data-table">
                <caption class="sr-only">${title} - ${label}.</caption>
                <thead>${columns}</thead>
                <tbody>${items.map(row)}</tbody>
              </table>
            </div>
          `}
    </section>
  `;
}

/** Whatever the host sent, as something safe to iterate. */
function list(value) {
  return Array.isArray(value) ? value : [];
}

/** A count the view can print. Anything else - null, a string, NaN - reads as nothing found. */
function whole(value) {
  return Number.isFinite(value) ? Math.max(0, Math.round(value)) : 0;
}

/** A recorded flag, as a word. Anything that is not exactly true reads as off. */
function onOff(value) {
  return value === true ? 'on' : 'off';
}

/** The site one side was against, or "" for a record that names none. */
function site(side) {
  return typeof side?.site === 'string' ? side.site : '';
}

/** One scan's completion, in the same en-GB wording the history list uses. */
function completed(value) {
  const at = new Date(value);

  if (Number.isNaN(at.getTime())) {
    return '';
  }

  return at.toLocaleString('en-GB', {
    day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

customElements.define('cs-diff-view', DiffView);
