import { LitElement, html, nothing } from '/vendor/lit.js';

/*
    One number, what it counts, and a line of context under it.

    Deliberately dumb: it formats nothing, derives nothing and knows about no scan. Every number and
    every word in it is handed over by whoever owns the result, so the Scan page and the History page
    can show the same tile without either of them having to agree with the other about arithmetic.
*/
export class StatTile extends LitElement {
  static properties = {
    value: { type: Number },
    label: {},
    /** Optional. Nothing is rendered for an empty hint, rather than an empty line. */
    hint: {},
    /** One of blue, red, amber, teal - the tint sets in app.css. */
    tone: {},
  };

  constructor() {
    super();

    // Defaults rather than undefined: an element in the markup renders before anything fills it,
    // and "undefined" is not a count.
    this.value = 0;
    this.label = '';
    this.hint = '';
    this.tone = 'blue';
  }

  /**
   * Renders into the element itself rather than into a shadow root.
   *
   * The .tile rules and the four tint sets live in app.css, and class rules do not cross a shadow
   * boundary. Encapsulating this element would mean a second copy of those colours - the same
   * palette written twice and free to drift apart - for an element that slots nothing and has no
   * markup of its own to protect. cs-log-panel keeps its shadow root because what it styles is one
   * host box; a tile, a table and a pill are the design system itself.
   */
  createRenderRoot() {
    return this;
  }

  render() {
    return html`
      <div class="tile tile--${this.tone}">
        <span class="tile-value">${this.value}</span>
        <span class="tile-label">${this.label}</span>
        ${this.hint ? html`<span class="tile-hint">${this.hint}</span>` : nothing}
      </div>
    `;
  }
}

customElements.define('cs-stat-tile', StatTile);
