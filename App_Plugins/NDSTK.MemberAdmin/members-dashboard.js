import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

const API_BASE = '/umbraco/management/api/v1/backoffice/ndstk/members';

// security must be declared explicitly. Without it umbHttpClient does not attach the bearer
// token and every request 401s.
const SECURITY = [{ scheme: 'bearer', type: 'http' }];

// Dates in this table are data, not prose: they are read down a column and compared, not into a
// sentence. So they are pinned to ISO rather than localized along with the words around them.
//
// "Short" is not one format. The same instant is 8/25/26 in en, 25/08/2026 in en-GB and 2026-08-25
// in sv-SE, and the first two disagree about which number is the month - next to a membership
// expiry the API already sends as an ISO string, that is a misreading waiting to happen rather than
// a matter of taste.
//
// sv-SE is chosen for its date pattern, not its language: nothing formatted here is a word. Local
// time rather than UTC, because an instant late on the 24th in Stockholm is the 25th to the club
// reading it - which is exactly why an account created at 23:15 showed up a day later.
const ISO_DATE = new Intl.DateTimeFormat('sv-SE', { dateStyle: 'short' });

// Seconds left off deliberately. localize.dateTime() would be the obvious call for a start time,
// but it formats with timeStyle "medium" and puts them back.
const ISO_DATE_TIME = new Intl.DateTimeFormat('sv-SE', { dateStyle: 'short', timeStyle: 'short' });

class NdstkMembersDashboard extends UmbElementMixin(LitElement) {
    static properties = {
        _rows: { state: true },
        _search: { state: true },
        _selected: { state: true },
        _detail: { state: true },
        _busy: { state: true },
        _loaded: { state: true },
        _resetAvailable: { state: true },
    };

    #notifications;

    constructor() {
        super();
        this._rows = [];
        this._search = '';
        this._selected = null;
        this._detail = null;
        this._busy = false;
        this._loaded = false;
        this._resetAvailable = false;

        this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => { this.#notifications = ctx; });
    }

    connectedCallback() {
        super.connectedCallback();
        this.#load();
        this.#probeReset();
    }

    // Shorthand: this reads better than this.localize.term('ndstk_x') forty times over, and it is
    // the only thing standing between a template and an unreadable one.
    #t(key, ...args) {
        return this.localize.term(`ndstk_${key}`, ...args);
    }

    // Money is öre everywhere on the server; the divide by a hundred happens here, once, the same
    // way MembershipSettingsService is the only place kronor become öre on the way in. The grouping
    // follows the backoffice language; the unit does not, because the club charges in kronor
    // whichever language an administrator reads.
    #kr(ore) {
        return `${this.localize.number(ore / 100)} ${this.#t('currencySuffix')}`;
    }

    // An em dash rather than an empty cell: a blank could mean "none" or "failed to load".
    #date(iso) {
        return iso ? ISO_DATE.format(new Date(iso)) : '—';
    }

    // A lapsed membership reads as a word, not as a negative number of days.
    #daysLeft(row) {
        if (row.daysLeft === null || row.daysLeft === undefined) return '—';
        return row.daysLeft < 0 ? this.#t('lapsed') : this.#t('daysShort', row.daysLeft);
    }

    // The reset endpoints answer 404 unless the site is running in development with the setting
    // on, so asking is how the dashboard knows whether to draw the buttons at all. Called without
    // tryExecute and with everything swallowed: a 404 here is the expected answer, not a fault,
    // and it must not raise a notification.
    async #probeReset() {
        try {
            const { error } = await umbHttpClient.get({
                url: `${API_BASE}/reset`,
                security: SECURITY,
            });
            this._resetAvailable = !error;
        } catch {
            this._resetAvailable = false;
        }
    }

    async #load() {
        this._busy = true;
        try {
            const { data, error } = await tryExecute(
                this,
                umbHttpClient.get({ url: API_BASE, security: SECURITY }),
            );
            if (error) throw error;
            this._rows = data ?? [];
        } catch (err) {
            this.#notifications?.peek('danger', {
                data: { message: err.message ?? this.#t('loadMembersFailed') },
            });
        } finally {
            this._loaded = true;
            this._busy = false;
        }
    }

    async #select(row) {
        // Clicking the open row closes it, so the table is never stuck showing a panel.
        if (this._selected === row.memberKey) {
            this._selected = null;
            this._detail = null;
            return;
        }

        this._selected = row.memberKey;
        this._detail = null;

        try {
            const { data, error } = await tryExecute(
                this,
                umbHttpClient.get({ url: `${API_BASE}/${row.memberKey}`, security: SECURITY }),
            );
            if (error) throw error;
            this._detail = data ?? null;
        } catch (err) {
            this.#notifications?.peek('danger', {
                data: { message: err.message ?? this.#t('loadMemberFailed') },
            });
        }
    }

    // Empties one account, or every account when memberKey is null.
    //
    // The confirmation is the browser's own rather than the backoffice modal. This is a
    // development-only control that throws data away, and a native confirm cannot fail to appear -
    // a modal that failed to open would leave the button silently doing nothing, which is the one
    // outcome a destructive action must not have.
    async #reset(memberKey, label) {
        if (!confirm(this.#t('resetConfirm', label))) {
            return;
        }

        this._busy = true;
        try {
            const { data, error } = await tryExecute(
                this,
                umbHttpClient.post({
                    url: memberKey ? `${API_BASE}/reset/${memberKey}` : `${API_BASE}/reset`,
                    security: SECURITY,
                }),
            );
            if (error) throw error;

            this.#notifications?.peek('positive', {
                data: {
                    message: this.#t(
                        'resetDone',
                        data.bookings, data.payments, data.credits,
                        data.participants, data.members),
                },
            });

            // The open panel describes an account that has just been emptied, so it goes with it.
            this._selected = null;
            this._detail = null;
            await this.#load();
        } catch (err) {
            this.#notifications?.peek('danger', {
                data: { message: err.message ?? this.#t('resetFailed') },
            });
        } finally {
            this._busy = false;
        }
    }

    get #filtered() {
        const term = this._search.trim().toLocaleLowerCase(this.localize.lang());
        if (!term) return this._rows;

        // Child names are searchable too: a coach knows the child's name, not the parent's email.
        return this._rows.filter((row) =>
            [row.name, row.email, ...(row.childNames ?? [])]
                .filter(Boolean)
                .some((value) => value.toLocaleLowerCase(this.localize.lang()).includes(term)));
    }

    #exportCsv() {
        const header = [
            this.#t('colName'), this.#t('colEmail'), this.#t('csvPhone'), this.#t('colFamily'),
            this.#t('colVerified'), this.#t('colMemberSince'), this.#t('colExpires'),
            this.#t('csvDaysLeft'), this.#t('csvParticipants'), this.#t('csvChildNames'),
            this.#t('csvPaid'), this.#t('csvLastPayment'), this.#t('colBooked'),
            this.#t('colCancelled'), this.#t('colCredits'),
        ];

        const rows = this.#filtered.map((row) => [
            row.name, row.email, row.phone ?? '',
            row.isFamilyAccount ? this.#t('yes') : this.#t('no'),
            this.#date(row.verifiedUtc), this.#date(row.memberSinceUtc), row.paidUntil ?? '',
            row.daysLeft ?? '', row.participantCount, (row.childNames ?? []).join('; '),
            row.totalPaidOre / 100, this.#date(row.lastPaymentUtc), row.confirmedBookings,
            row.cancelledBookings, row.unspentCredits,
        ]);

        // Every field quoted and embedded quotes doubled: a child's name can contain a comma.
        const escape = (value) => `"${String(value ?? '').replaceAll('"', '""')}"`;
        const csv = [header, ...rows].map((row) => row.map(escape).join(',')).join('\r\n');

        // The BOM is what makes Excel open this as UTF-8 rather than mangling å, ä and ö.
        const blob = new Blob([`﻿${csv}`], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = url;
        link.download = this.#t('csvFileName');
        link.click();
        URL.revokeObjectURL(url);
    }

    render() {
        return html`
            <uui-box headline=${this.#t('members')}>
                <div class="toolbar">
                    <uui-input
                        type="search"
                        label=${this.#t('search')}
                        placeholder=${this.#t('searchPlaceholder')}
                        .value=${this._search}
                        @input=${(e) => { this._search = e.target.value; }}></uui-input>

                    <span class="count">
                        ${this.#t('showingOf', this.#filtered.length, this._rows.length)}
                    </span>

                    <uui-button
                        look="secondary"
                        label=${this.#t('exportCsv')}
                        ?disabled=${this._rows.length === 0}
                        @click=${this.#exportCsv}></uui-button>

                    ${this._resetAvailable ? html`
                        <uui-button
                            look="outline"
                            color="danger"
                            label=${this.#t('resetAll')}
                            title=${this.#t('resetAllTitle')}
                            ?disabled=${this._busy}
                            @click=${() => this.#reset(null, this.#t('resetAllLabel'))}></uui-button>
                    ` : ''}
                </div>

                ${this._busy && !this._loaded
                    ? html`<uui-loader></uui-loader>`
                    : this.#renderTable()}
            </uui-box>
        `;
    }

    /// A real table element rather than uui-table, and that is not a preference.
    ///
    /// The account's detail expands underneath the row it belongs to, as one cell spanning every
    /// column. That needs colspan, which is an attribute of a real td - uui-table lays out with CSS
    /// display:table on custom elements, and CSS has no colspan property, so a panel inserted there
    /// is squeezed into the first column whatever it is styled with. The three tables inside the
    /// panel are still uui-tables: nothing in them has to span anything.
    #renderTable() {
        if (this._rows.length === 0) {
            return html`<p>${this.#t('noMembers')}</p>`;
        }

        // The chevron, the twelve data columns, and the reset button where it is available.
        const columns = 13 + (this._resetAvailable ? 1 : 0);

        return html`
            <table class="members">
                <thead>
                    <tr>
                        <th class="chevron-col"><span class="sr-only">${this.#t('toggleDetails')}</span></th>
                        <th>${this.#t('colName')}</th>
                        <th>${this.#t('colEmail')}</th>
                        <th title=${this.#t('colFamily')}>${this.#t('colFamilyShort')}</th>
                        <th>${this.#t('colVerified')}</th>
                        <th>${this.#t('colMemberSince')}</th>
                        <th>${this.#t('colExpires')}</th>
                        <th>${this.#t('colLeft')}</th>
                        <th>${this.#t('colChildren')}</th>
                        <th>${this.#t('colPaid')}</th>
                        <th>${this.#t('colBooked')}</th>
                        <th title=${this.#t('colCancelled')}>${this.#t('colCancelledShort')}</th>
                        <th title=${this.#t('colCredits')}>${this.#t('colCreditsShort')}</th>
                        ${this._resetAvailable ? html`<th></th>` : ''}
                    </tr>
                </thead>
                <tbody>
                    ${this.#filtered.map((row) => this.#renderRow(row, columns))}
                </tbody>
            </table>

            ${/* The column heading is repeated in bold to introduce the sentence, which is why the
                  note is two terms and not one - markup does not belong inside a translation. */''}
            <p class="note">
                <strong>${this.#t('colCancelledShort')}</strong> ${this.#t('noteCancelled')}
                ${this.#t('noteAttendance')}
            </p>
        `;
    }

    /// One account: its row, and underneath it the panel when the row is open.
    #renderRow(row, columns) {
        const open = this._selected === row.memberKey;

        return html`
            <tr class="row ${open ? 'row--open' : ''}"
                tabindex="0"
                aria-expanded=${open ? 'true' : 'false'}
                title=${this.#t('toggleDetails')}
                @click=${() => this.#select(row)}
                @keydown=${(e) => this.#rowKey(e, row)}>
                <td class="chevron-col">
                    <span class="chevron ${open ? 'chevron--open' : ''}" aria-hidden="true">▸</span>
                </td>
                <td><strong>${row.name}</strong></td>
                <td>${row.email}</td>
                <td>${row.isFamilyAccount ? '✓' : '–'}</td>
                <td>${this.#date(row.verifiedUtc)}</td>
                <td>${this.#date(row.memberSinceUtc)}</td>
                <td>${row.paidUntil ?? '—'}</td>
                <td class=${row.daysLeft < 0 ? 'lapsed' : ''}>${this.#daysLeft(row)}</td>
                <td title=${(row.childNames ?? []).join(', ')}>${row.participantCount}</td>
                <td>${this.#kr(row.totalPaidOre)}</td>
                <td>${row.confirmedBookings}</td>
                <td>${row.cancelledBookings}</td>
                <td>${row.unspentCredits}</td>
                ${/* The row itself opens the panel, so this click stops here. */
                  this._resetAvailable ? html`
                    <td>
                        <uui-button
                            look="outline"
                            color="danger"
                            compact
                            label=${this.#t('resetOne')}
                            title=${this.#t('resetOneTitle')}
                            ?disabled=${this._busy}
                            @click=${(e) => {
                                e.stopPropagation();
                                this.#reset(row.memberKey, row.email);
                            }}></uui-button>
                    </td>
                ` : ''}
            </tr>

            ${open ? html`
                <tr class="panel-row">
                    <td class="panel-cell" colspan=${columns}>${this.#renderPanel()}</td>
                </tr>
            ` : ''}
        `;
    }

    /// A row that behaves like a button has to answer the keyboard like one. Space is also the
    /// page-scroll key, which is why it is stopped rather than only acted on.
    #rowKey(event, row) {
        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }

        event.preventDefault();
        this.#select(row);
    }

    /// What sits under an open row.
    ///
    /// No name and no email at the top: both are in the row directly above, and repeating them is
    /// the kind of noise that made the old separate box hard to place. Payments come first because
    /// they are what the club opens an account to look at.
    #renderPanel() {
        if (!this._detail) {
            return html`<div class="panel panel--loading"><uui-loader></uui-loader></div>`;
        }

        const { summary, payments, bookings } = this._detail;

        return html`
            <div class="panel">
                ${summary.phone || summary.isFamilyAccount ? html`
                    <p class="detail-head">
                        ${summary.phone ?? ''}
                        ${summary.isFamilyAccount ? html`${summary.phone ? ' · ' : ''}
                            <strong>${this.#t('familyAccount')}</strong>` : ''}
                    </p>
                ` : ''}

                <h4>${this.#t('payments')}</h4>
                ${payments.length === 0 ? html`<p>${this.#t('noPayments')}</p>` : html`
                    <uui-table>
                        <uui-table-head>
                            <uui-table-head-cell>${this.#t('colDate')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colMembershipFee')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colFamilyFee')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colClassFee')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colTotal')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colStatus')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colSwishReference')}</uui-table-head-cell>
                        </uui-table-head>
                        ${payments.map((p) => html`
                            <uui-table-row>
                                <uui-table-cell>${this.#date(p.completedUtc ?? p.createdUtc)}</uui-table-cell>
                                <uui-table-cell>${p.membershipFeeOre ? this.#kr(p.membershipFeeOre) : '–'}</uui-table-cell>
                                <uui-table-cell>${p.familyFeeOre ? this.#kr(p.familyFeeOre) : '–'}</uui-table-cell>
                                <uui-table-cell>${p.classFeeOre ? this.#kr(p.classFeeOre) : '–'}</uui-table-cell>
                                <uui-table-cell><strong>${this.#kr(p.amountOre)}</strong></uui-table-cell>
                                <uui-table-cell>${p.status}${p.errorCode ? ` (${p.errorCode})` : ''}</uui-table-cell>
                                <uui-table-cell><code>${p.bankReference ?? '–'}</code></uui-table-cell>
                            </uui-table-row>
                        `)}
                    </uui-table>
                `}

                <h4>${this.#t('colChildren')}</h4>
                ${(summary.childNames ?? []).length === 0
                    ? html`<p>${this.#t('noParticipants')}</p>`
                    : html`<p>${summary.childNames.join(', ')}</p>`}

                <h4>${this.#t('bookings')}</h4>
                ${bookings.length === 0 ? html`<p>${this.#t('noBookings')}</p>` : html`
                    <uui-table>
                        <uui-table-head>
                            <uui-table-head-cell>${this.#t('colChild')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colClass')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colTime')}</uui-table-head-cell>
                            <uui-table-head-cell>${this.#t('colStatus')}</uui-table-head-cell>
                        </uui-table-head>
                        ${bookings.map((b) => html`
                            <uui-table-row>
                                <uui-table-cell>${b.childName}</uui-table-cell>
                                <uui-table-cell>${b.className}</uui-table-cell>
                                <uui-table-cell>${ISO_DATE_TIME.format(new Date(b.classStartUtc))}</uui-table-cell>
                                <uui-table-cell>${b.status}</uui-table-cell>
                            </uui-table-row>
                        `)}
                    </uui-table>
                `}
            </div>
        `;
    }

    static styles = css`
        :host {
            display: block;
            padding: var(--uui-size-layout-1);
        }

        .toolbar {
            display: flex;
            flex-wrap: wrap;
            gap: var(--uui-size-space-4);
            align-items: center;
            margin-bottom: var(--uui-size-space-4);
        }

        .toolbar uui-input {
            flex: 1 1 18rem;
        }

        .count {
            color: var(--uui-color-text-alt);
            font-size: 0.9em;
        }

        /* Hand-rolled because the list has to be a real table - see #renderTable. The tokens are
           uui's own, so this sits beside other backoffice tables rather than beside nothing. */
        table.members {
            width: 100%;
            border-collapse: collapse;
        }

        table.members th,
        table.members td {
            text-align: left;
            padding: var(--uui-size-space-3) var(--uui-size-space-4);
            border-bottom: 1px solid var(--uui-color-border);
            vertical-align: top;
        }

        table.members th {
            font-weight: 700;
            white-space: nowrap;
            border-bottom-width: 2px;
        }

        .row {
            cursor: pointer;
        }

        .row:hover > td {
            background: var(--uui-color-surface-alt);
        }

        .row:focus-visible {
            outline: 2px solid var(--uui-color-focus, #3544b1);
            outline-offset: -2px;
        }

        /* The open row and its panel are one object, so the line between them goes. */
        .row--open > td {
            background: var(--uui-color-surface-alt);
            border-bottom-color: transparent;
        }

        .chevron-col {
            width: 1rem;
            padding-right: 0;
        }

        .chevron {
            display: inline-block;
            color: var(--uui-color-text-alt);
            transition: transform 120ms ease;
        }

        .chevron--open {
            transform: rotate(90deg);
        }

        @media (prefers-reduced-motion: reduce) {
            .chevron {
                transition: none;
            }
        }

        .panel-cell {
            padding: 0 var(--uui-size-space-4) var(--uui-size-space-4);
            background: var(--uui-color-surface-alt);
        }

        /* Inset, with the accent down its left edge, so it reads as belonging to the row above
           rather than as another row of the table. */
        .panel {
            background: var(--uui-color-surface);
            border-left: 3px solid var(--uui-color-focus, #3544b1);
            padding: var(--uui-size-space-2) var(--uui-size-space-5) var(--uui-size-space-4);
        }

        .panel--loading {
            padding: var(--uui-size-space-4) var(--uui-size-space-5);
        }

        /* An expired membership is the one thing on this table worth spotting without reading. */
        .lapsed {
            color: var(--uui-color-danger);
            font-weight: 700;
        }

        .sr-only {
            position: absolute;
            width: 1px;
            height: 1px;
            padding: 0;
            margin: -1px;
            overflow: hidden;
            clip-path: inset(50%);
            white-space: nowrap;
        }

        .note {
            margin-top: var(--uui-size-space-4);
            color: var(--uui-color-text-alt);
            font-size: 0.9em;
            max-width: 60rem;
        }

        .detail-head {
            color: var(--uui-color-text-alt);
        }

        h4 {
            margin: var(--uui-size-space-5) 0 var(--uui-size-space-2);
        }
    `;
}

customElements.define('ndstk-members-dashboard', NdstkMembersDashboard);
export default NdstkMembersDashboard;
