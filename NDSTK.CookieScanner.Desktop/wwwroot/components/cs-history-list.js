import { LitElement, html, nothing } from '/vendor/lit.js';

/*
    Every kept scan, newest first, with a checkbox per row.

    Multi-select with an explicit checkbox rather than a modifier key: a click alone cannot say
    whether it means "look at this one" or "add this one to what is already picked", and a shift- or
    ctrl-click is a convention nobody can see by looking at the row. The checkbox is the whole state -
    there is nothing this component decides about what a selection MEANS. That is the host's call:
    one path means "show it", two mean "compare them", and neither is this file's business. It only
    ever reports what is checked, in a `selection-changed` event, and lets whoever is listening
    decide what to do about it.

    That includes the `note` - the line that tells the operator that two ticks are a comparison, which
    is otherwise undiscoverable, because two checkboxes look exactly like two checkboxes. The words
    are handed in by whoever owns the meaning, the same way cs-stat-tile is handed every word it
    shows; this element only finds it somewhere to live.
*/
export class HistoryList extends LitElement {
  static properties = {
    /**
     * Every kept scan, newest first - the `history` message's `entries`, unmodified.
     *
     * Property only, never an attribute: it is a list of objects, and an attribute would mean
     * serialising the whole history into the markup and parsing it back out again.
     */
    entries: { attribute: false },

    /**
     * One line above the table saying what the current selection does. Optional - an empty note
     * renders nothing rather than an empty line, the same as cs-stat-tile's hint.
     */
    note: {},

    /** The set of paths currently checked. Internal state, not something a caller hands in. */
    selected: { state: true },
  };

  constructor() {
    super();

    this.entries = [];
    this.note = '';
    this.selected = new Set();
  }

  /**
   * Renders into the element itself rather than into a shadow root - the same reasoning as
   * cs-findings-table: `.data-table` and `.pill` are the design system's table and the row it sits
   * over, not this component's private copy of them.
   */
  createRenderRoot() {
    return this;
  }

  /**
   * Drops a selected path the moment it stops being one of the rows on screen - a scan deleted or
   * corrupted between one `history` message and the next, or simply pruned past the fifty kept.
   * Left in place, a stale path would go on being "selected" for a row that no longer exists, and
   * `loadScan` would be asked for a file the list itself no longer lists.
   */
  willUpdate(changed) {
    if (changed.has('entries') === false) {
      return;
    }

    const known = new Set((Array.isArray(this.entries) ? this.entries : []).map((entry) => entry.path));
    const kept = new Set([...this.selected].filter((path) => known.has(path)));

    if (kept.size !== this.selected.size) {
      this.selected = kept;

      this.announce();
    }
  }

  toggle(path, checked) {
    const next = new Set(this.selected);

    if (checked) {
      next.add(path);
    } else {
      next.delete(path);
    }

    this.selected = next;

    this.announce();
  }

  /** Composed and bubbling, same as `page-shown`: whoever hosts this element listens on itself. */
  announce() {
    this.dispatchEvent(new CustomEvent('selection-changed', {
      detail: { paths: [...this.selected] },
      bubbles: true,
      composed: true,
    }));
  }

  render() {
    const entries = Array.isArray(this.entries) ? this.entries : [];

    if (entries.length === 0) {
      return html`<p class="muted">No scans yet.</p>`;
    }

    return html`
      ${this.note
        // Polite rather than assertive, and a live region rather than a plain line: the note changes
        // on every tick, and a reader who cannot see the boxes otherwise has no way of knowing that
        // checking a second one just changed what the pane below is about.
        ? html`<p class="muted history-note" aria-live="polite">${this.note}</p>`
        : nothing}
      <table class="data-table">
        <caption class="sr-only">Every kept scan - ${entries.length} in total.</caption>
        <thead>
          <tr>
            <th scope="col" class="history-check"><span class="sr-only">Select</span></th>
            <th scope="col">Completed</th>
            <th scope="col">Site</th>
            <th scope="col" class="num">Entries</th>
            <th scope="col">Result</th>
          </tr>
        </thead>
        <tbody>
          ${entries.map((entry) => row(entry, this.selected.has(entry.path), this))}
        </tbody>
      </table>
    `;
  }
}

/**
 * One scan. The row carries the numeric exit code in its own `title`, so a pointer resting anywhere
 * on the row - not only on the pill - shows the raw code a bug report would ask for, beside the word
 * the pill already says out loud.
 */
function row(entry, checked, host) {
  const when = completed(entry);
  const exitCode = whole(entry.exitCode);

  return html`
    <tr class=${checked ? 'is-selected' : ''} title=${exitCode}>
      <td class="history-check">
        <input
          type="checkbox"
          .checked=${checked}
          aria-label="Select the scan completed ${when}"
          @change=${(event) => host.toggle(entry.path, event.target.checked)}>
      </td>
      <td>${when}</td>
      <td>${site(entry)}</td>
      <td class="num">${whole(entry.entryCount)}</td>
      <td>${resultPill(entry)}</td>
    </tr>
  `;
}

/**
 * The result, as a word rather than a number - the same reasoning as the findings table's pill:
 * colour is never the only cue, and a screen reader or a monochrome screenshot gets nothing from an
 * exit code alone.
 */
function resultPill(entry) {
  const exitCode = whole(entry.exitCode);

  if (exitCode === 1) {
    const count = whole(entry.violationCount);

    return html`<span class="pill pill--violation">${count === 1 ? '1 violation' : `${count} violations`}</span>`;
  }

  if (exitCode === 2) {
    return html`<span class="pill pill--review">write-back failed</span>`;
  }

  if (exitCode === 0) {
    return html`<span class="pill pill--ok">clean</span>`;
  }

  // Not a code this build knows the meaning of - said plainly rather than guessed at.
  return html`<span class="pill">exit ${exitCode}</span>`;
}

/** A count the row can print. Anything else - null, a string, NaN - reads as nothing found. */
function whole(value) {
  return Number.isFinite(value) ? Math.max(0, Math.round(value)) : 0;
}

/** One scan's completion, in the same en-GB wording as the trend chart's dates, plus the time. */
function completed(entry) {
  const at = new Date(entry?.completedAt);

  if (Number.isNaN(at.getTime())) {
    return '';
  }

  return at.toLocaleString('en-GB', {
    day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

/** The site a scan was against, or "" for a history entry the host could not name one for. */
function site(entry) {
  return typeof entry?.site === 'string' ? entry.site : '';
}

customElements.define('cs-history-list', HistoryList);
