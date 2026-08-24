// Counts the payment reservation down on the Swish page.
//
// Cosmetic only. The server decides whether a hold is still valid - the reminder job sweeps expired
// ones, and settling re-checks the payment is still pending - so a clock skewed on the visitor's
// machine cannot buy them extra time or lose them a place early. Its job is to stop the page
// claiming "14 minuter till" twenty minutes after it was rendered.
(function () {
    'use strict';

    var el = document.querySelector('[data-hold-expires]');
    if (!el) {
        return;
    }

    var expiresAt = Date.parse(el.getAttribute('data-hold-expires'));
    if (isNaN(expiresAt)) {
        // Leave the server-rendered sentence alone rather than replacing it with something wrong.
        return;
    }

    var actions = document.querySelector('[data-hold-actions]');
    var timer = null;

    function plural(value, one, many) {
        return value + ' ' + (value === 1 ? one : many);
    }

    function remainingText(msLeft) {
        // Under a minute the seconds are what matters; above it, minutes alone read more calmly
        // than a ticking mm:ss.
        if (msLeft < 60000) {
            var seconds = Math.floor(msLeft / 1000);
            return 'Platsen är reserverad i ' + plural(seconds, 'sekund', 'sekunder') + ' till.';
        }

        // Rounded up, and taken straight from the milliseconds so this is the identical calculation
        // the server does. Flooring to whole seconds first would leave a sub-second window where the
        // two disagree by a minute, which is exactly the kind of gap that shows up as a flicker on
        // page load.
        //
        // Rounding up rather than truncating because a hold has a few milliseconds less than its
        // full duration left a heartbeat after it is created: truncating would show a minute fewer
        // from the outset, as though a minute had already been lost.
        var minutes = Math.ceil(msLeft / 60000);

        return 'Platsen är reserverad i ' + plural(minutes, 'minut', 'minuter') + ' till.';
    }

    function expire() {
        if (timer) {
            window.clearInterval(timer);
        }

        el.textContent = 'Reservationen har gått ut. Platsen är inte längre bokad åt dig.';
        el.classList.add('swish__hold--expired');

        // The buttons are left in the DOM but disabled: removing them would shift the page under
        // whoever is looking at it. A submission would fail server-side anyway.
        if (actions) {
            var buttons = actions.querySelectorAll('button');
            for (var i = 0; i < buttons.length; i++) {
                buttons[i].disabled = true;
            }
        }
    }

    function tick() {
        var msLeft = expiresAt - Date.now();

        if (msLeft <= 0) {
            expire();
            return;
        }

        el.textContent = remainingText(msLeft);
    }

    tick();
    timer = window.setInterval(tick, 1000);
})();
