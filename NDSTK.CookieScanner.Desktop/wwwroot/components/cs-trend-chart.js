import { LitElement, html } from '/vendor/lit.js';

/*
    Entries and violations across the scans kept for one site.

    Hand-rolled SVG, no charting library: at most twenty points, two polylines and two end dots. A
    library would be a megabyte of build step for a shape this file draws in forty lines - and there
    is no build step to put it in.

    Two things about it are not decoration and must not be "simplified" away:

    1. X is the scan index, never the time. Scans are irregular - a cluster of one afternoon's runs
       and then nothing for a week - and a time axis would stack that afternoon into a single
       vertical smear while the empty week ate the card.
    2. The shape is the SECOND cue. The svg carries role="img" and an aria-label naming both series,
       their range and the count, and the numbers themselves are in the table below it. A chart that
       exists only as a shape says nothing to a screen reader, to a monochrome print, or to anyone
       reading the window aloud.
*/

/*
    The plot area, in viewBox units. WIDTH and HEIGHT are interpolated into the viewBox itself, so
    the geometry below and the coordinate space it draws in cannot drift apart; the rendered
    attribute is "0 0 320 78".

    The padding is not margin for its own sake: the end dot is stroke geometry drawn in DEVICE pixels
    (see the dot below), and the svg root clips at the viewBox, so a dot on the last point needs room
    on the right and under the baseline that does not shrink when the card is narrow.
*/
const WIDTH = 320;
const HEIGHT = 78;
const PAD_X = 6;
const PAD_TOP = 8;
const PAD_BOTTOM = 7;

/** The zero line. */
const BASE = HEIGHT - PAD_BOTTOM;

/** How many scans are drawn. Older ones stay in the history list; they are not the trend. */
const LIMIT = 20;

/** Ten percent, so the tallest point is not welded to the top edge. */
const HEADROOM = 1.1;

/*
    One gradient id per instance. The element renders into the light DOM, where ids are shared with
    the whole document, so a second chart with a hard-coded id would silently paint itself with the
    first one's gradient.
*/
let instances = 0;

export class TrendChart extends LitElement {
  static properties = {
    /**
     * The scans to draw, newest first, already narrowed to one site by whoever owns the URL field.
     *
     * Property only, never an attribute: it is a list of objects, and an attribute would mean
     * serialising the history into the markup and parsing it back out again.
     */
    entries: { attribute: false },
  };

  constructor() {
    super();

    this.entries = [];
    this.gradientId = `trend-area-${++instances}`;
  }

  /**
   * Renders into the element itself rather than into a shadow root - the same reasoning as
   * cs-stat-tile and cs-findings-table. The series colours are --blue-600 and --red-600 from
   * app.css, and the numbers below the chart are carried by .sr-only, which is app.css's own
   * recipe: a shadow root would mean a second copy of both, free to drift from the tiles and the
   * table this card sits above.
   */
  createRenderRoot() {
    return this;
  }

  render() {
    // Sliced before it is reversed: "the most recent twenty" is the first twenty of the host's
    // order, and the chart reads left to right from oldest to newest.
    const points = (Array.isArray(this.entries) ? this.entries : []).slice(0, LIMIT).reverse();

    if (points.length === 0) {
      // Not an empty axis. An axis with no line on it looks like a site that was scanned and found
      // to be flat, which is the opposite of what an empty history means.
      return html`<p class="trend-empty">No scans yet</p>`;
    }

    const entryCounts = points.map((point) => whole(point?.entryCount));
    const violationCounts = points.map((point) => whole(point?.violationCount));

    // Scaled to the entries series alone, as specified: entries is the quantity the card is about,
    // and violations are drawn against the same ruler so the two can be read together. Never zero,
    // so a history of empty scans still has something to divide by.
    const top = Math.max(1, ...entryCounts) * HEADROOM;

    const x = (index) => (points.length === 1
      ? WIDTH - PAD_X
      : PAD_X + (index * (WIDTH - PAD_X * 2)) / (points.length - 1));

    // Clamped at the top, because the ruler is the entries series. Violations are counted over raw
    // observations while entries are the distinct names, so a pathological run could in principle
    // exceed it; folding onto the top edge is a visible oddity, drawing outside the card is not.
    // The table below carries the true number either way.
    const y = (value) => BASE - Math.min(1, value / top) * (BASE - PAD_TOP);

    const line = (values) => values
      .map((value, index) => `${index === 0 ? 'M' : 'L'} ${round(x(index))} ${round(y(value))}`)
      .join(' ');

    const last = points.length - 1;

    // Nothing to fill under a single point, and a Z on a one-point path would draw a hairline down
    // to the baseline that is not a measurement of anything.
    const area = points.length > 1
      ? `${line(entryCounts)} L ${round(x(last))} ${BASE} L ${round(x(0))} ${BASE} Z`
      : '';

    /*
        The end dot is a ZERO-LENGTH subpath with a round cap, not a <circle>.

        preserveAspectRatio="none" scales x and y by different factors, so a circle drawn in user
        space arrives on screen as an ellipse. A cap is stroke geometry, and non-scaling-stroke
        builds stroke geometry after the transform - so the cap is a true circle of exactly
        stroke-width device pixels however the card is sized.
    */
    const dot = (value) => `M ${round(x(last))} ${round(y(value))} L ${round(x(last))} ${round(y(value))}`;

    const latestEntries = entryCounts[last];
    const latestViolations = violationCounts[last];

    const site = typeof points[last]?.site === 'string' ? points[last].site : '';
    const scans = `${points.length} ${points.length === 1 ? 'scan' : 'scans'}`;

    const period = dateRange(points, LONG_DATE);

    // Assembled from parts and joined, so a history with an unreadable date simply loses that
    // clause instead of announcing "Invalid Date" to a screen reader. Each part ends in a full stop
    // so the whole reads as sentences rather than as one run-on line.
    const label = [
      `Entries and violations across the last ${scans}${site === '' ? '' : ` of ${site}`}.`,
      period === '' ? '' : `${period}.`,
      `Entries ${spread(entryCounts)}, latest ${latestEntries}.`,
      `Violations ${spread(violationCounts)}, latest ${latestViolations}.`,
    ].filter(Boolean).join(' ');

    const shortRange = dateRange(points, SHORT_DATE);

    return html`
      <div class="trend">

        <!--
          Two spans stacked by the grid rather than one split by a <br>: a line break arrives in the
          accessibility tree as a text node holding a newline, which is a nothing for a screen reader
          to announce.
        -->
        <p class="trend-figure">
          <span class="trend-value">${latestEntries}</span>
          <span class="trend-figure-label">entries</span>
          <span class="trend-figure-label">latest scan</span>
        </p>

        <svg class="trend-chart" viewBox="0 0 ${WIDTH} ${HEIGHT}" preserveAspectRatio="none"
             role="img" aria-label=${label}>
          <defs>
            <linearGradient id=${this.gradientId} x1="0" y1="0" x2="0" y2="1">
              <stop class="trend-fade-in" offset="0"></stop>
              <stop class="trend-fade-out" offset="1"></stop>
            </linearGradient>
          </defs>

          <path class="trend-area" fill="url(#${this.gradientId})" d=${area}></path>

          <path class="trend-line trend-line--entries" d=${line(entryCounts)}></path>

          <!--
            Drawn after the entries series and never conditionally: a site that scans clean has this
            line flat along the zero baseline for every point, and a series that vanished when it was
            all zeroes would read as a chart with a measurement missing rather than as a site with no
            violations.
          -->
          <path class="trend-line trend-line--violations" d=${line(violationCounts)}></path>

          <path class="trend-dot trend-dot--entries" d=${dot(latestEntries)}></path>
          <path class="trend-dot trend-dot--violations" d=${dot(latestViolations)}></path>
        </svg>

      </div>

      <div class="trend-foot">
        <ul class="trend-legend">
          <!--
            The colon is not punctuation for its own sake. Chromium builds an element's accessible
            name by concatenating its descendants and collapsing the whitespace between them, so
            "Entries 2" reaches a screen reader as "Entries2"; the colon is what keeps the word and
            the number apart in both readings of this line.
          -->
          <li class="trend-key trend-key--entries">
            <span class="trend-swatch" aria-hidden="true"></span>
            Entries: <span class="trend-key-value">${latestEntries}</span>
          </li>
          <li class="trend-key trend-key--violations">
            <span class="trend-swatch" aria-hidden="true"></span>
            Violations: <span class="trend-key-value">${latestViolations}</span>
          </li>
        </ul>
        <p class="trend-range">${shortRange === '' ? scans : `${scans} · ${shortRange}`}</p>
      </div>

      <!--
        The numbers the shape stands for. Visually hidden rather than absent, and OUTSIDE the svg
        rather than inside it: role="img" hides an element's descendants from assistive technology,
        so a table nested in the chart would be announced as nothing at all.
      -->
      <table class="sr-only">
        <caption>Entries and violations for each of the last ${scans}, oldest first.</caption>
        <thead>
          <tr>
            <th scope="col">Scan</th>
            <th scope="col">Completed</th>
            <th scope="col">Entries</th>
            <th scope="col">Violations</th>
          </tr>
        </thead>
        <tbody>
          ${points.map((point, index) => html`
            <tr>
              <th scope="row">${index + 1}</th>
              <td>${day(point, LONG_DATE)}</td>
              <td>${entryCounts[index]}</td>
              <td>${violationCounts[index]}</td>
            </tr>
          `)}
        </tbody>
      </table>
    `;
  }
}

/*
    Explicitly en-GB rather than the machine's locale: every other label in this window is English,
    and a date that read "29 aug." on one machine and "8/29/2026" on another would be the one string
    on the page that changed with the operating system.
*/
const LONG_DATE = { day: 'numeric', month: 'short', year: 'numeric' };
const SHORT_DATE = { day: 'numeric', month: 'short' };

/** A count the chart can plot. Anything else - null, a string, NaN - counts as nothing found. */
function whole(value) {
  return Number.isFinite(value) ? Math.max(0, Math.round(value)) : 0;
}

/** Two decimals is well under a device pixel at this size, and keeps the path attribute readable. */
function round(value) {
  return Math.round(value * 100) / 100;
}

/** One scan's completion date, or "" if the host sent something Date cannot read. */
function day(point, format) {
  const at = new Date(point?.completedAt);

  return Number.isNaN(at.getTime()) ? '' : at.toLocaleDateString('en-GB', format);
}

/** The period the points cover, collapsed to one date when they all fall on it. */
function dateRange(points, format) {
  const first = day(points[0], format);
  const last = day(points[points.length - 1], format);

  if (first === '' || last === '') {
    return '';
  }

  return first === last ? first : `${first} to ${last}`;
}

/**
 * A series in words.
 *
 * "0 throughout" rather than "from 0 to 0": a site that scans clean is the ordinary case here, and
 * a range whose two ends are the same number makes the listener do the work of noticing that.
 */
function spread(values) {
  const low = Math.min(...values);
  const high = Math.max(...values);

  return low === high ? `${low} throughout` : `from ${low} to ${high}`;
}

customElements.define('cs-trend-chart', TrendChart);
