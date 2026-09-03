// The started state of the Swish page: shows the right hand-over for the device, and polls until
// Swish has decided.
//
// The poll is the path that always works. Swish's callback may never reach the server - the
// production binding drops handshakes without SNI - but this page asking "har det hänt något?"
// every few seconds does not depend on it. The server asks Swish at most every five seconds
// however fast the page polls; the interval here is only how quickly the member sees the result.
(function () {
    'use strict';

    var root = document.querySelector('[data-swish-started="true"]');
    if (!root) {
        return;
    }

    // --- which hand-over to show ---

    var isMobile = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent);
    var mode = isMobile ? 'mobile' : 'desktop';
    var toggle = root.querySelector('[data-device-toggle]');

    function apply() {
        var blocks = root.querySelectorAll('[data-device]');
        for (var i = 0; i < blocks.length; i++) {
            blocks[i].hidden = blocks[i].getAttribute('data-device') !== mode;
        }

        if (toggle) {
            toggle.hidden = false;

            // "den här enheten", not "den här telefonen". This label is only ever shown while the
            // QR code is, which is the desktop case - so it was offering to open Swish on a phone
            // to somebody sitting at a computer.
            toggle.textContent = mode === 'mobile'
                ? 'Visa QR-kod istället'
                : 'Öppna Swish på den här enheten istället';
        }
    }

    if (toggle) {
        toggle.addEventListener('click', function () {
            mode = mode === 'mobile' ? 'desktop' : 'mobile';
            apply();
        });
    }

    apply();

    // --- poll ---

    var statusUrl = root.getAttribute('data-status-url');
    var interval = parseInt(root.getAttribute('data-poll-interval'), 10) || 3000;
    if (!statusUrl) {
        return;
    }

    var failures = 0;

    function poll() {
        fetch(statusUrl, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            })
            .then(function (result) {
                failures = 0;
                if (result && result.terminal) {
                    // The server renders the outcome; reloading is simpler and more honest than
                    // rebuilding the page here.
                    window.location.reload();
                    return;
                }
                window.setTimeout(poll, interval);
            })
            .catch(function () {
                // Back off, but never give up while the page is open: the member may be mid-BankID.
                failures++;

                // After about a minute of failures, stop implying that all is well. The payment may
                // still complete - Swish decides that, not this page - so the wording says what is
                // and is not known rather than declaring a failure.
                if (failures === 6) {
                    var waiting = root.querySelector('.swish__waiting');
                    if (waiting) {
                        waiting.textContent =
                            'Vi når inte Swish just nu. Har du godkänt betalningen i appen är den på väg '
                            + 'in - ladda om sidan om en stund, eller kontakta oss på info@ndstk.se.';
                    }
                }

                window.setTimeout(poll, Math.min(interval * (failures + 1), 15000));
            });
    }

    window.setTimeout(poll, interval);
})();
