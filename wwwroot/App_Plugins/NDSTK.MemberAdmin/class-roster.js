import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

const API_BASE = '/umbraco/management/api/v1/backoffice/ndstk/members';

// security must be declared explicitly, or umbHttpClient sends no bearer token and the request 401s.
const SECURITY = [{ scheme: 'bearer', type: 'http' }];

const STATUS_LABELS = {
    Confirmed: 'Bekräftad',
    Pending: 'Väntar på betalning',
};

class NdstkClassRoster extends UmbElementMixin(LitElement) {
    static properties = {
        _rows: { state: true },
        _loaded: { state: true },
        _classKey: { state: true },
    };

    #notifications;

    constructor() {
        super();
        this._rows = [];
        this._loaded = false;
        this._classKey = null;

        this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => { this.#notifications = ctx; });

        this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (context) => {
            if (!context) return;

            // The workspace "unique" IS the document key, which is what ndstkBooking.ClassKey holds.
            // Read as an observable where the context exposes one, and fall back to a plain value:
            // getting this wrong renders an empty tab with nothing in the console, which is the
            // hardest kind of failure to notice.
            const unique = context.unique;

            if (unique && typeof unique.subscribe === 'function') {
                this.observe(unique, (value) => { if (value) this.#load(value); });
                return;
            }

            const value = typeof context.getUnique === 'function' ? context.getUnique() : unique;
            if (value) this.#load(value);
        });
    }

    async #load(classKey) {
        if (this._classKey === classKey) return;
        this._classKey = classKey;

        try {
            const { data, error } = await tryExecute(
                this,
                umbHttpClient.get({ url: `${API_BASE}/roster/${classKey}`, security: SECURITY }),
            );
            if (error) throw error;
            this._rows = data ?? [];
        } catch (err) {
            this.#notifications?.peek('danger', {
                data: { message: err.message ?? 'Kunde inte hämta deltagarna.' },
            });
        } finally {
            this._loaded = true;
        }
    }

    render() {
        if (!this._loaded) {
            return html`<uui-box headline="Deltagare"><uui-loader></uui-loader></uui-box>`;
        }

        return html`
            <uui-box headline="Deltagare">
                <p class="count">
                    ${this._rows.length}
                    ${this._rows.length === 1 ? 'plats bokad' : 'platser bokade'}
                </p>

                ${this._rows.length === 0
                    ? html`<p>Ingen har bokat den här träningen än.</p>`
                    : html`
                        <uui-table>
                            <uui-table-head>
                                <uui-table-head-cell>Barn</uui-table-head-cell>
                                <uui-table-head-cell>Ålder</uui-table-head-cell>
                                <uui-table-head-cell>Målsman</uui-table-head-cell>
                                <uui-table-head-cell>E-post</uui-table-head-cell>
                                <uui-table-head-cell>Telefon</uui-table-head-cell>
                                <uui-table-head-cell>Status</uui-table-head-cell>
                            </uui-table-head>
                            ${this._rows.map((row) => html`
                                <uui-table-row>
                                    <uui-table-cell><strong>${row.childName}</strong></uui-table-cell>
                                    <uui-table-cell>${row.age ?? '—'}</uui-table-cell>
                                    <uui-table-cell>${row.guardianName}</uui-table-cell>
                                    <uui-table-cell>
                                        <a href="mailto:${row.guardianEmail}">${row.guardianEmail}</a>
                                    </uui-table-cell>
                                    <uui-table-cell>
                                        ${row.guardianPhone
                                            ? html`<a href="tel:${row.guardianPhone}">${row.guardianPhone}</a>`
                                            : '—'}
                                    </uui-table-cell>
                                    <uui-table-cell
                                        class=${row.status === 'Pending' ? 'pending' : ''}>
                                        ${STATUS_LABELS[row.status] ?? row.status}
                                    </uui-table-cell>
                                </uui-table-row>
                            `)}
                        </uui-table>
                    `}

                <p class="note">
                    Platser som väntar på betalning räknas som bokade tills reservationen går ut.
                </p>
            </uui-box>
        `;
    }

    static styles = css`
        :host {
            display: block;
            padding: var(--uui-size-layout-1);
        }

        .count {
            font-weight: 700;
            margin: 0 0 var(--uui-size-space-4);
        }

        /* An unpaid hold is the one row a coach should treat differently, so it is marked. */
        .pending {
            color: var(--uui-color-warning-emphasis, var(--uui-color-text-alt));
        }

        .note {
            margin-top: var(--uui-size-space-4);
            color: var(--uui-color-text-alt);
            font-size: 0.9em;
        }
    `;
}

customElements.define('ndstk-class-roster', NdstkClassRoster);
export default NdstkClassRoster;
