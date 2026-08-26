import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

const API_BASE = '/umbraco/management/api/v1/backoffice/ndstk/members';

// security must be declared explicitly. Without it umbHttpClient does not attach the bearer
// token and every request 401s.
const SECURITY = [{ scheme: 'bearer', type: 'http' }];

// Money is öre everywhere on the server; the divide by a hundred happens here, once, the same way
// MembershipSettingsService is the only place kronor become öre on the way in.
const kr = (ore) => `${(ore / 100).toLocaleString('sv-SE')} kr`;

const date = (iso) => (iso ? new Date(iso).toLocaleDateString('sv-SE') : '—');

// A lapsed membership reads as a word, not as a negative number of days.
const daysLeft = (row) =>
    row.daysLeft === null || row.daysLeft === undefined
        ? '—'
        : row.daysLeft < 0
            ? 'Utgången'
            : `${row.daysLeft} d`;

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
                data: { message: err.message ?? 'Kunde inte hämta medlemmarna.' },
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
                data: { message: err.message ?? 'Kunde inte hämta medlemmen.' },
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
        if (!confirm(`Nollställ ${label}?\n\nBokningar, betalningar, tillgodoträningar, barn och `
            + 'medlemskap tas bort. Inloggningen behålls. Detta går inte att ångra.')) {
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
                    message: `Nollställt: ${data.bookings} bokningar, ${data.payments} betalningar, `
                        + `${data.credits} tillgodoträningar, ${data.participants} barn, `
                        + `${data.members} medlemskap.`,
                },
            });

            // The open panel describes an account that has just been emptied, so it goes with it.
            this._selected = null;
            this._detail = null;
            await this.#load();
        } catch (err) {
            this.#notifications?.peek('danger', {
                data: { message: err.message ?? 'Kunde inte nollställa.' },
            });
        } finally {
            this._busy = false;
        }
    }

    get #filtered() {
        const term = this._search.trim().toLocaleLowerCase('sv-SE');
        if (!term) return this._rows;

        // Child names are searchable too: a coach knows the child's name, not the parent's email.
        return this._rows.filter((row) =>
            [row.name, row.email, ...(row.childNames ?? [])]
                .filter(Boolean)
                .some((value) => value.toLocaleLowerCase('sv-SE').includes(term)));
    }

    #exportCsv() {
        const header = [
            'Namn', 'E-post', 'Telefon', 'Familjekonto', 'Verifierad', 'Medlem sedan',
            'Går ut', 'Dagar kvar', 'Deltagare', 'Barn', 'Betalt (kr)', 'Senaste betalning',
            'Bokade', 'Avbokade', 'Krediter',
        ];

        const rows = this.#filtered.map((row) => [
            row.name, row.email, row.phone ?? '', row.isFamilyAccount ? 'Ja' : 'Nej',
            date(row.verifiedUtc), date(row.memberSinceUtc), row.paidUntil ?? '',
            row.daysLeft ?? '', row.participantCount, (row.childNames ?? []).join('; '),
            row.totalPaidOre / 100, date(row.lastPaymentUtc), row.confirmedBookings,
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
        link.download = 'ndstk-medlemmar.csv';
        link.click();
        URL.revokeObjectURL(url);
    }

    render() {
        return html`
            <uui-box headline="Medlemmar">
                <div class="toolbar">
                    <uui-input
                        type="search"
                        label="Sök"
                        placeholder="Sök på namn, e-post eller barn"
                        .value=${this._search}
                        @input=${(e) => { this._search = e.target.value; }}></uui-input>

                    <span class="count">${this.#filtered.length} av ${this._rows.length}</span>

                    <uui-button
                        look="secondary"
                        label="Exportera CSV"
                        ?disabled=${this._rows.length === 0}
                        @click=${this.#exportCsv}></uui-button>

                    ${this._resetAvailable ? html`
                        <uui-button
                            look="outline"
                            color="danger"
                            label="Nollställ testdata"
                            title="Tömmer bokningar, betalningar, tillgodoträningar, barn och medlemskap för alla konton"
                            ?disabled=${this._busy}
                            @click=${() => this.#reset(null, 'alla konton')}></uui-button>
                    ` : ''}
                </div>

                ${this._busy && !this._loaded
                    ? html`<uui-loader></uui-loader>`
                    : this.#renderTable()}
            </uui-box>

            ${this._selected ? this.#renderDetail() : ''}
        `;
    }

    #renderTable() {
        if (this._rows.length === 0) {
            return html`<p>Inga medlemmar än.</p>`;
        }

        return html`
            <uui-table>
                <uui-table-head>
                    <uui-table-head-cell>Namn</uui-table-head-cell>
                    <uui-table-head-cell>E-post</uui-table-head-cell>
                    <uui-table-head-cell title="Familjekonto">Fam</uui-table-head-cell>
                    <uui-table-head-cell>Verifierad</uui-table-head-cell>
                    <uui-table-head-cell>Medlem sedan</uui-table-head-cell>
                    <uui-table-head-cell>Går ut</uui-table-head-cell>
                    <uui-table-head-cell>Kvar</uui-table-head-cell>
                    <uui-table-head-cell>Barn</uui-table-head-cell>
                    <uui-table-head-cell>Betalt</uui-table-head-cell>
                    <uui-table-head-cell>Bokade</uui-table-head-cell>
                    <uui-table-head-cell title="Avbokade av medlemmen">Avbok.</uui-table-head-cell>
                    <uui-table-head-cell title="Outnyttjade tillgodoträningar">Kred.</uui-table-head-cell>
                    ${this._resetAvailable ? html`<uui-table-head-cell></uui-table-head-cell>` : ''}
                </uui-table-head>

                ${this.#filtered.map((row) => html`
                    <uui-table-row
                        class="row ${this._selected === row.memberKey ? 'row--open' : ''}"
                        @click=${() => this.#select(row)}>
                        <uui-table-cell><strong>${row.name}</strong></uui-table-cell>
                        <uui-table-cell>${row.email}</uui-table-cell>
                        <uui-table-cell>${row.isFamilyAccount ? '✓' : '–'}</uui-table-cell>
                        <uui-table-cell>${date(row.verifiedUtc)}</uui-table-cell>
                        <uui-table-cell>${date(row.memberSinceUtc)}</uui-table-cell>
                        <uui-table-cell>${row.paidUntil ?? '—'}</uui-table-cell>
                        <uui-table-cell class=${row.daysLeft < 0 ? 'lapsed' : ''}>
                            ${daysLeft(row)}
                        </uui-table-cell>
                        <uui-table-cell title=${(row.childNames ?? []).join(', ')}>
                            ${row.participantCount}
                        </uui-table-cell>
                        <uui-table-cell>${kr(row.totalPaidOre)}</uui-table-cell>
                        <uui-table-cell>${row.confirmedBookings}</uui-table-cell>
                        <uui-table-cell>${row.cancelledBookings}</uui-table-cell>
                        <uui-table-cell>${row.unspentCredits}</uui-table-cell>
                        ${/* The row itself opens the detail panel, so this click stops here. */
                          this._resetAvailable ? html`
                            <uui-table-cell>
                                <uui-button
                                    look="outline"
                                    color="danger"
                                    compact
                                    label="Nollställ"
                                    title="Tömmer bara den här medlemmen"
                                    ?disabled=${this._busy}
                                    @click=${(e) => {
                                        e.stopPropagation();
                                        this.#reset(row.memberKey, row.email);
                                    }}></uui-button>
                            </uui-table-cell>
                        ` : ''}
                    </uui-table-row>
                `)}
            </uui-table>

            <p class="note">
                <strong>Avbok.</strong> är träningar medlemmen själv avbokat och fått en
                tillgodoträning för. Frånvaro registreras inte, så en deltagare som var bokad men
                inte kom syns inte i någon av kolumnerna.
            </p>
        `;
    }

    #renderDetail() {
        if (!this._detail) {
            return html`<uui-box headline="Läser in…"><uui-loader></uui-loader></uui-box>`;
        }

        const { summary, payments, bookings } = this._detail;

        return html`
            <uui-box headline="${summary.name}">
                <p class="detail-head">
                    ${summary.email}
                    ${summary.phone ? html` · ${summary.phone}` : ''}
                    ${summary.isFamilyAccount ? html` · <strong>Familjekonto</strong>` : ''}
                </p>

                <h4>Barn</h4>
                ${(summary.childNames ?? []).length === 0
                    ? html`<p>Inga deltagare.</p>`
                    : html`<p>${summary.childNames.join(', ')}</p>`}

                <h4>Betalningar</h4>
                ${payments.length === 0 ? html`<p>Inga betalningar.</p>` : html`
                    <uui-table>
                        <uui-table-head>
                            <uui-table-head-cell>Datum</uui-table-head-cell>
                            <uui-table-head-cell>Årsavgift</uui-table-head-cell>
                            <uui-table-head-cell>Familjetillägg</uui-table-head-cell>
                            <uui-table-head-cell>Träningsavgift</uui-table-head-cell>
                            <uui-table-head-cell>Totalt</uui-table-head-cell>
                            <uui-table-head-cell>Status</uui-table-head-cell>
                        </uui-table-head>
                        ${payments.map((p) => html`
                            <uui-table-row>
                                <uui-table-cell>${date(p.completedUtc ?? p.createdUtc)}</uui-table-cell>
                                <uui-table-cell>${p.membershipFeeOre ? kr(p.membershipFeeOre) : '–'}</uui-table-cell>
                                <uui-table-cell>${p.familyFeeOre ? kr(p.familyFeeOre) : '–'}</uui-table-cell>
                                <uui-table-cell>${p.classFeeOre ? kr(p.classFeeOre) : '–'}</uui-table-cell>
                                <uui-table-cell><strong>${kr(p.amountOre)}</strong></uui-table-cell>
                                <uui-table-cell>${p.status}</uui-table-cell>
                            </uui-table-row>
                        `)}
                    </uui-table>
                `}

                <h4>Bokningar</h4>
                ${bookings.length === 0 ? html`<p>Inga bokningar.</p>` : html`
                    <uui-table>
                        <uui-table-head>
                            <uui-table-head-cell>Barn</uui-table-head-cell>
                            <uui-table-head-cell>Träning</uui-table-head-cell>
                            <uui-table-head-cell>Tid</uui-table-head-cell>
                            <uui-table-head-cell>Status</uui-table-head-cell>
                        </uui-table-head>
                        ${bookings.map((b) => html`
                            <uui-table-row>
                                <uui-table-cell>${b.childName}</uui-table-cell>
                                <uui-table-cell>${b.className}</uui-table-cell>
                                <uui-table-cell>${new Date(b.classStartUtc).toLocaleString('sv-SE')}</uui-table-cell>
                                <uui-table-cell>${b.status}</uui-table-cell>
                            </uui-table-row>
                        `)}
                    </uui-table>
                `}
            </uui-box>
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

        .row {
            cursor: pointer;
        }

        .row--open {
            background: var(--uui-color-surface-alt);
        }

        /* An expired membership is the one thing on this table worth spotting without reading. */
        .lapsed {
            color: var(--uui-color-danger);
            font-weight: 700;
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
