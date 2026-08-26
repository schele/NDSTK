import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

const API_BASE = '/umbraco/management/api/v1/backoffice/ndstk/members';

// security must be declared explicitly, or umbHttpClient sends no bearer token and the request 401s.
const SECURITY = [{ scheme: 'bearer', type: 'http' }];

// The statuses the API returns. Kept as a map from the wire value to a term key rather than to a
// finished string, so a status this build does not know about still renders as itself.
const STATUS_TERMS = {
    Confirmed: 'ndstk_statusConfirmed',
    Pending: 'ndstk_statusPending',
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

    // Öre to kronor, here at the edge, the same way the dashboard and the portal do it. The grouping
    // follows the backoffice language; the unit does not, because the club charges in kronor
    // whichever language an administrator reads.
    #kr(ore) {
        return `${this.localize.number(ore / 100)} ${this.localize.term('ndstk_currencySuffix')}`;
    }

    // What the club actually received for this place. A credit and a payment are not exclusive: a
    // lapsed member spending one pays the annual fee and nothing for the class, so both show.
    #payment(row) {
        const parts = [];
        if (row.usedCredit) parts.push(this.localize.term('ndstk_credit'));
        if (row.paidOre) parts.push(this.#kr(row.paidOre));

        // Says so rather than leaving a blank cell that could mean either "free" or "not loaded".
        return parts.length ? parts.join(' + ') : this.localize.term('ndstk_noPayment');
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
                data: { message: err.message ?? this.localize.term('ndstk_loadRosterFailed') },
            });
        } finally {
            this._loaded = true;
        }
    }

    render() {
        const headline = this.localize.term('ndstk_roster');

        if (!this._loaded) {
            return html`<uui-box headline=${headline}><uui-loader></uui-loader></uui-box>`;
        }

        return html`
            <uui-box headline=${headline}>
                <p class="count">
                    ${this._rows.length === 1
                        ? this.localize.term('ndstk_placeBooked', this._rows.length)
                        : this.localize.term('ndstk_placesBooked', this._rows.length)}
                </p>

                ${this._rows.length === 0
                    ? html`<p>${this.localize.term('ndstk_rosterEmpty')}</p>`
                    : html`
                        <uui-table>
                            <uui-table-head>
                                <uui-table-head-cell>${this.localize.term('ndstk_colChild')}</uui-table-head-cell>
                                <uui-table-head-cell>${this.localize.term('ndstk_colAge')}</uui-table-head-cell>
                                <uui-table-head-cell>${this.localize.term('ndstk_colGuardian')}</uui-table-head-cell>
                                <uui-table-head-cell>${this.localize.term('ndstk_colEmail')}</uui-table-head-cell>
                                <uui-table-head-cell>${this.localize.term('ndstk_colPhone')}</uui-table-head-cell>
                                <uui-table-head-cell>${this.localize.term('ndstk_colPayment')}</uui-table-head-cell>
                                <uui-table-head-cell>${this.localize.term('ndstk_colStatus')}</uui-table-head-cell>
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
                                        class=${!row.usedCredit && !row.paidOre ? 'unpaid' : ''}>
                                        ${this.#payment(row)}
                                    </uui-table-cell>
                                    <uui-table-cell
                                        class=${row.status === 'Pending' ? 'pending' : ''}>
                                        ${STATUS_TERMS[row.status]
                                            ? this.localize.term(STATUS_TERMS[row.status])
                                            : row.status}
                                    </uui-table-cell>
                                </uui-table-row>
                            `)}
                        </uui-table>
                    `}

                <p class="note">${this.localize.term('ndstk_rosterNote')}</p>
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

        /* A confirmed place with nothing recorded against it is worth spotting: either it was
           comped, or something went wrong between booking and payment. */
        .unpaid {
            color: var(--uui-color-danger, var(--uui-color-text-alt));
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
