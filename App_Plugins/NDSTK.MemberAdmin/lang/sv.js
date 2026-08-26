// Swedish terms for the member administration extension. Keys and structure mirror en.js exactly;
// anything missing here falls back to English rather than to the raw key.
export default {
    ndstk: {
        members: 'Medlemmar',
        search: 'Sök',
        searchPlaceholder: 'Sök på namn, e-post eller barn',
        showingOf: '%0% av %1%',
        exportCsv: 'Exportera CSV',
        csvFileName: 'ndstk-medlemmar.csv',
        noMembers: 'Inga medlemmar än.',
        loading: 'Läser in…',
        loadMembersFailed: 'Kunde inte hämta medlemmarna.',
        loadMemberFailed: 'Kunde inte hämta medlemmen.',

        resetAll: 'Nollställ testdata',
        resetAllTitle:
            'Tömmer bokningar, betalningar, tillgodoträningar, barn och medlemskap för alla konton',
        resetAllLabel: 'alla konton',
        resetOne: 'Nollställ',
        resetOneTitle: 'Tömmer bara den här medlemmen',
        resetConfirm:
            'Nollställ %0%?\n\nBokningar, betalningar, tillgodoträningar, barn och medlemskapet '
            + 'tas bort. Inloggningen behålls. Detta går inte att ångra.',
        resetDone:
            'Nollställt: %0% bokningar, %1% betalningar, %2% tillgodoträningar, %3% barn, '
            + '%4% medlemskap.',
        resetFailed: 'Kunde inte nollställa.',

        colName: 'Namn',
        colEmail: 'E-post',
        colFamilyShort: 'Fam',
        colFamily: 'Familjekonto',
        colVerified: 'Verifierad',
        colMemberSince: 'Medlem sedan',
        colExpires: 'Går ut',
        colLeft: 'Kvar',
        colChildren: 'Barn',
        colPaid: 'Betalt',
        colBooked: 'Bokade',
        colCancelledShort: 'Avbok.',
        colCancelled: 'Avbokade av medlemmen',
        colCreditsShort: 'Kred.',
        colCredits: 'Outnyttjade tillgodoträningar',
        lapsed: 'Utgången',
        daysShort: '%0% d',

        noteCancelled: 'är träningar medlemmen själv avbokat och fått en tillgodoträning för.',
        noteAttendance:
            'Frånvaro registreras inte, så en deltagare som var bokad men inte kom syns inte i '
            + 'någon av kolumnerna.',

        familyAccount: 'Familjekonto',
        noParticipants: 'Inga deltagare.',
        payments: 'Betalningar',
        noPayments: 'Inga betalningar.',
        bookings: 'Bokningar',
        noBookings: 'Inga bokningar.',
        colDate: 'Datum',
        colMembershipFee: 'Årsavgift',
        colFamilyFee: 'Familjetillägg',
        colClassFee: 'Träningsavgift',
        colTotal: 'Totalt',
        colStatus: 'Status',
        colClass: 'Träning',
        colTime: 'Tid',

        csvPhone: 'Telefon',
        csvDaysLeft: 'Dagar kvar',
        csvParticipants: 'Deltagare',
        csvChildNames: 'Barn',
        csvPaid: 'Betalt (kr)',
        csvLastPayment: 'Senaste betalning',
        yes: 'Ja',
        no: 'Nej',

        currencySuffix: 'kr',

        roster: 'Deltagare',
        placeBooked: '%0% plats bokad',
        placesBooked: '%0% platser bokade',
        rosterEmpty: 'Ingen har bokat den här träningen än.',
        loadRosterFailed: 'Kunde inte hämta deltagarna.',
        colChild: 'Barn',
        colAge: 'Ålder',
        colGuardian: 'Målsman',
        colPhone: 'Telefon',
        colPayment: 'Betalning',
        statusConfirmed: 'Bekräftad',
        statusPending: 'Väntar på betalning',
        credit: 'Tillgodoträning',
        noPayment: 'Ingen betalning',
        rosterNote: 'Platser som väntar på betalning räknas som bokade tills reservationen går ut.',
    },
};
