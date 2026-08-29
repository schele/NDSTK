import { LitElement, css, html } from '/vendor/lit.js';

/*
    The scan's commentary, as it arrives.

    A scan emits a line every few hundred milliseconds for the best part of a minute, so this element
    is written around appending rather than around rendering: the list is a DOM node this component
    owns, Lit renders that node once, and every later line is an <li> appended to it. Re-rendering a
    Lit template per line would rebuild the whole list - and would throw away the text selection the
    operator was making halfway down it.
*/
export class LogPanel extends LitElement {
  static properties = {};

  static styles = css`
    /*
        The dark, monospaced panel of the design system, read through the custom properties rather
        than through app.css: class rules do not cross a shadow boundary but custom properties do,
        so the colours stay in one place and this component still styles itself.
    */
    :host {
      display: block;
      min-height: 200px;
      max-height: 44vh;
      padding: var(--s-3) var(--s-4);
      border-radius: var(--r-lg);
      background: var(--log-bg);
      color: var(--log-ink);
      font-family: var(--font-mono);
      font-size: 12px;
      line-height: 1.55;
      overflow-y: auto;
      /* A log is there to be quoted into a bug report. */
      user-select: text;
    }

    :host([hidden]) {
      display: none;
    }

    ol {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    /*
        pre-wrap, not nowrap: ScanReportWriter's summary indents its second report path with spaces
        so it lines up under the first, which only survives if the spaces do. anywhere lets a long
        URL fold instead of pushing a horizontal scrollbar under every other line.
    */
    li {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    /*
        The colour is the second cue, never the only one: the level is also spelled out in the line's
        own text, which is what a monochrome screenshot and a screen reader are left with.
    */
    li.warn {
      color: var(--log-warn);
    }

    .level {
      font-weight: 700;
    }
  `;

  constructor() {
    super();

    // The list is created here rather than in a template so that a line appended before the first
    // render still has somewhere to go: Lit renders this node, it does not build it.
    this.list = document.createElement('ol');

    this.queued = [];
    this.frame = 0;
  }

  connectedCallback() {
    super.connectedCallback();

    // Set here rather than written into index.html, so every use of the element is announced the
    // same way. aria-relevant="additions" keeps a screen reader to the new line instead of
    // re-reading the whole log each time one arrives.
    this.setAttribute('role', 'log');
    this.setAttribute('aria-live', 'polite');
    this.setAttribute('aria-relevant', 'additions');
    this.setAttribute('aria-label', 'Scan log');
  }

  render() {
    return html`${this.list}`;
  }

  /**
   * Queues one line. Levels other than "warning" render as ordinary output.
   */
  append(level, message) {
    this.queued.push({ level, message });

    if (this.frame !== 0) {
      return;
    }

    // Batched to one frame. The engine can emit a burst of lines in a single tick, and appending
    // each one separately means a layout and a scroll per line.
    this.frame = requestAnimationFrame(() => {
      this.frame = 0;
      this.flush();
    });
  }

  clear() {
    this.queued = [];
    this.list.replaceChildren();
  }

  flush() {
    if (this.queued.length === 0) {
      return;
    }

    // Measured before the append, because appending is what changes it. Only a reader already at the
    // bottom is followed down: scrolling back to read an earlier line and being yanked forward every
    // time the scan says something is the behaviour this check exists to avoid.
    const atBottom = this.scrollTop + this.clientHeight >= this.scrollHeight - 4;

    const batch = document.createDocumentFragment();

    for (const { level, message } of this.queued) {
      const line = document.createElement('li');

      if (level === 'warning') {
        line.className = 'warn';

        const word = document.createElement('span');

        word.className = 'level';
        word.textContent = 'Warning';

        line.append(word, ` ${message}`);
      } else {
        line.textContent = message;
      }

      batch.append(line);
    }

    this.queued = [];
    this.list.append(batch);

    if (atBottom) {
      this.scrollTop = this.scrollHeight;
    }
  }
}

customElements.define('cs-log-panel', LogPanel);
