import { LitElement, html } from '/vendor/lit.js';

/*
    Everything one scan found, one row per candidate.

    The Scan page shows the run that just finished and the History page shows a run reloaded from
    disk, and both use this element - so the rule that decides which rows are red exists once. A
    second table would be a second copy of that rule, and the two would disagree on the day it
    mattered.
*/
export class FindingsTable extends LitElement {
  static properties = {
    /**
     * One parsed ScanResult, or null.
     *
     * Property only, never an attribute: it is an object, and an attribute would mean serialising a
     * whole scan into the markup and parsing it back out again.
     */
    result: { attribute: false },
  };

  constructor() {
    super();

    this.result = null;
  }

  /**
   * Renders into the element itself rather than into a shadow root - see the note in
   * cs-stat-tile.js. The .data-table and .pill rules in app.css are the design system's table, and
   * the row tints have to match the pills exactly; a copy of them behind a shadow boundary is a
   * copy free to drift.
   */
  createRenderRoot() {
    return this;
  }

  render() {
    const result = this.result;

    // Both lists are checked, not just candidates: the rule below reads violations unconditionally,
    // and a result without one is a shape this element cannot report on rather than one it should
    // half-render.
    if (result === null || Array.isArray(result.candidates) === false
      || Array.isArray(result.violations) === false) {
      return html`<p class="muted">No scan loaded.</p>`;
    }

    // Declared on the catalogue's word rather than seen. Absent from a scan recorded before the
    // field existed, and from any run with nothing to add that way, so it is read as empty.
    const fromCatalogue = Array.isArray(result.declaredFromCatalogue) ? result.declaredFromCatalogue : [];

    if (result.candidates.length === 0 && fromCatalogue.length === 0) {
      return html`<p class="muted">This scan found no cookies or storage entries.</p>`;
    }

    // A row is red when the cookie is a violation, which is NOT the same as its flag being one.
    // candidates is the earliest-per-name reduction; violations is computed over the raw
    // observations, deliberately, because a violation is a property of one sighting. A cookie first
    // set in a pass that granted its category and set again in one that did not is a violation the
    // flag knows nothing about - colouring by flag alone would leave the window disagreeing with the
    // exit code CI gates on.
    const violations = new Set(result.violations.map(v => v.name.toLowerCase()));
    const isViolation = c => c.flag === 'Violation' || violations.has(c.name.toLowerCase());

    // The caption is a name for the table itself: the card's heading is not attached to it, and a
    // screen reader landing in the grid otherwise has no idea what it is reading or how much of it
    // there is. Visually hidden, because the heading above already says it to everyone else.
    return html`
      <table class="data-table">
        <caption class="sr-only">
          All entries declared - ${result.candidates.length + fromCatalogue.length} in total,
          ${fromCatalogue.length} of them from the catalogue rather than observed.
        </caption>
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col">Storage</th>
            <th scope="col">Category</th>
            <th scope="col">First seen in</th>
            <th scope="col">Duration</th>
            <th scope="col">State</th>
          </tr>
        </thead>
        <tbody>
          ${result.candidates.map(candidate => row(candidate, isViolation(candidate)))}
          ${fromCatalogue.map(catalogueRow)}
        </tbody>
      </table>
    `;
  }
}

/**
 * One candidate, tinted by what the rule above decided about it.
 *
 * A violation outranks a review: a cookie can be both, and the one that fails the run is the one
 * the row has to say.
 */
function row(candidate, violation) {
  const review = violation === false && candidate.flag === 'NeedsReview';

  let tint = '';

  if (violation) {
    tint = 'is-violation';
  } else if (review) {
    tint = 'needs-review';
  }

  return html`
    <tr class=${tint}>
      <td class="mono">${candidate.name}</td>
      <td>${candidate.storageType}</td>
      <td>${candidate.category}</td>
      <td>${candidate.firstSeenPass}</td>
      <td>${candidate.duration}</td>
      <td>${pill(violation, review)}</td>
    </tr>
  `;
}

/**
 * One declaration the catalogue supplied because the crawl cannot reach it - a cookie the site
 * writes from a POST the scan refuses to make.
 *
 * Listed in the same table as the sightings because the question this table answers is "what will
 * the policy page say", and two tables made the answer read as the observed count alone. No tint:
 * it is neither a violation nor a doubtful categorisation, and the provenance is spelled out in the
 * pass column and again in its own pill rather than left to a colour.
 */
function catalogueRow(declaration) {
  return html`
    <tr>
      <td class="mono">${declaration.name}</td>
      <td>${declaration.storageType}</td>
      <td>${declaration.category}</td>
      <td>not observed</td>
      <td>${declaration.duration}</td>
      <td><span class="pill">From catalogue</span></td>
    </tr>
  `;
}

/**
 * The state, as a word.
 *
 * The tint is the second cue and never the only one: a red row says nothing to a reader who cannot
 * see red, to a monochrome screenshot pasted into a ticket, or to anyone reading the page aloud.
 */
function pill(violation, review) {
  if (violation) {
    return html`<span class="pill pill--violation">Violation</span>`;
  }

  if (review) {
    return html`<span class="pill pill--review">Needs review</span>`;
  }

  return html`<span class="pill">OK</span>`;
}

customElements.define('cs-findings-table', FindingsTable);
