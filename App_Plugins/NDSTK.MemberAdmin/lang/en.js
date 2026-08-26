// English terms for the member administration extension.
//
// English is the backoffice's own fallback, so this file is the one that has to be complete: a key
// missing from sv.js falls back here, and a key missing from here renders as the raw key.
//
// One section, "ndstk", because the dashboard and the class roster share a vocabulary - a child, a
// guardian, a credit - and splitting them would mean deciding which file owns each shared word.
// Terms are looked up as ndstk_<key>; %0% and %1% are positional arguments.
export default {
    ndstk: {
        // Dashboard chrome
        members: 'Members',
        search: 'Search',
        searchPlaceholder: 'Search by name, email or child',
        showingOf: '%0% of %1%',
        exportCsv: 'Export CSV',
        csvFileName: 'ndstk-members.csv',
        noMembers: 'No members yet.',
        loading: 'Loading…',
        loadMembersFailed: 'Could not load the members.',
        loadMemberFailed: 'Could not load the member.',

        // Test data reset. Development only, so the wording is deliberately blunt.
        resetAll: 'Reset test data',
        resetAllTitle:
            'Empties bookings, payments, credits, children and memberships for every account',
        resetAllLabel: 'every account',
        resetOne: 'Reset',
        resetOneTitle: 'Empties this member only',
        resetConfirm:
            'Reset %0%?\n\nBookings, payments, credits, children and the membership are removed. '
            + 'The login is kept. This cannot be undone.',
        resetDone:
            'Reset: %0% bookings, %1% payments, %2% credits, %3% children, %4% memberships.',
        resetFailed: 'Could not reset.',

        // Member table. The short forms are column headings in a table with thirteen of them; the
        // long forms are their tooltips.
        colName: 'Name',
        colEmail: 'Email',
        colFamilyShort: 'Fam',
        colFamily: 'Family account',
        colVerified: 'Verified',
        colMemberSince: 'Member since',
        colExpires: 'Expires',
        colLeft: 'Left',
        colChildren: 'Children',
        colPaid: 'Paid',
        colBooked: 'Booked',
        colCancelledShort: 'Canc.',
        colCancelled: 'Cancelled by the member',
        colCreditsShort: 'Cred.',
        colCredits: 'Unused credits',
        lapsed: 'Expired',
        daysShort: '%0% d',

        // The note under the table. Two terms rather than one, because the first is introduced by
        // the column heading in bold and markup does not belong in a term.
        noteCancelled: 'is training the member cancelled themselves and received a credit for.',
        noteAttendance:
            'Attendance is not recorded, so a participant who was booked but did not turn up '
            + 'appears in none of the columns.',

        // Member detail panel
        familyAccount: 'Family account',
        noParticipants: 'No participants.',
        payments: 'Payments',
        noPayments: 'No payments.',
        bookings: 'Bookings',
        noBookings: 'No bookings.',
        colDate: 'Date',
        colMembershipFee: 'Annual fee',
        colFamilyFee: 'Family supplement',
        colClassFee: 'Class fee',
        colTotal: 'Total',
        colStatus: 'Status',
        colClass: 'Class',
        colTime: 'Time',

        // CSV headings, spelled out in full - a spreadsheet column has room that a table heading
        // does not, and the file outlives the screen it was exported from.
        csvPhone: 'Phone',
        csvDaysLeft: 'Days left',
        csvParticipants: 'Participants',
        csvChildNames: 'Children',
        csvPaid: 'Paid (SEK)',
        csvLastPayment: 'Last payment',
        yes: 'Yes',
        no: 'No',

        // The club charges in kronor whatever language the backoffice is in, so this is a unit and
        // not a translation. It is a term only so a future club in another currency has one place
        // to change.
        currencySuffix: 'kr',

        // Class roster
        roster: 'Participants',
        placeBooked: '%0% place booked',
        placesBooked: '%0% places booked',
        rosterEmpty: 'Nobody has booked this class yet.',
        loadRosterFailed: 'Could not load the participants.',
        colChild: 'Child',
        colAge: 'Age',
        colGuardian: 'Guardian',
        colPhone: 'Phone',
        colPayment: 'Payment',
        statusConfirmed: 'Confirmed',
        statusPending: 'Awaiting payment',
        credit: 'Credit',
        noPayment: 'No payment',
        rosterNote: 'Places awaiting payment count as booked until the hold expires.',
    },
};
