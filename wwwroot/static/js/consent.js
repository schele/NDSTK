/**
 * NDSTK cookie consent.
 *
 * Deliberately dependency-free and self-hosted: a consent tool that itself calls out to a third
 * party would undercut its own purpose.
 *
 * The server is the source of truth. This script never writes the consent cookie - it posts the
 * choice and lets the endpoint set it, which is what guarantees the cookie's attributes are right.
 */
(function () {
    'use strict';

    var script = document.currentScript;
    var endpoint = script.getAttribute('data-consent-endpoint') || '/api/consent';
    var cookieName = script.getAttribute('data-consent-cookie') || 'ndstk-consent';
    var policyVersion = parseInt(script.getAttribute('data-consent-version') || '1', 10);
    var consentModeEnabled = script.getAttribute('data-consent-mode') === 'on';
    var needsDecision = script.getAttribute('data-consent-needs-decision') === 'true';
    var errorMessage = script.getAttribute('data-consent-error-message') || 'Något gick fel. Försök igen.';
    var rateLimitedMessage = script.getAttribute('data-consent-rate-limited-message')
        || 'Du har försökt för många gånger. Vänta en stund och försök igen.';

    // Set only when open() finds the dialog cannot actually be displayed. Stops the 'close'
    // handler from reopening an invisible modal in a loop.
    var blockingAbandoned = false;

    var listeners = [];

    function readCookie() {
        var prefix = cookieName + '=';
        var parts = document.cookie ? document.cookie.split('; ') : [];

        for (var i = 0; i < parts.length; i++) {
            if (parts[i].indexOf(prefix) !== 0) { continue; }
            try {
                var parsed = JSON.parse(decodeURIComponent(parts[i].substring(prefix.length)));
                if (!parsed || typeof parsed.v !== 'number') { return null; }
                return {
                    version: parsed.v,
                    decidedAt: parsed.t,
                    categories: Array.isArray(parsed.c) ? parsed.c : [],
                    consentId: parsed.id
                };
            } catch (error) {
                return null;
            }
        }

        return null;
    }

    function currentCategories() {
        var state = readCookie();
        if (!state || state.version < policyVersion) { return []; }
        return state.categories;
    }

    function has(category) {
        return category === 'necessary' || currentCategories().indexOf(category) !== -1;
    }

    var dialog = document.getElementById('consent-dialog');

    /**
     * True once the dialog actually occupies space in the layout - not merely `open`. Guards
     * against a zero-height dialog (a stylesheet conflict, or one stripped by a browser
     * extension) leaving the visitor stuck behind a dimmed, unusable page.
     */
    function isDisplayed(element) {
        var box = element.getBoundingClientRect();
        return box.width > 0 && box.height > 0;
    }

    function open() {
        if (!dialog) { return; }

        var dialogSupported = typeof HTMLDialogElement === 'function'
            && typeof dialog.showModal === 'function';

        if (!dialogSupported) {
            // No native modal <dialog> support: still offer the choice, just not modally -
            // an unusable site is worse than a non-blocking one.
            if (window.console) {
                console.warn('ndstk-consent: dialog.showModal is not supported; showing the cookie choice without blocking the page.');
            }
            dialog.setAttribute('open', 'open');
            return;
        }

        dialog.showModal();

        if (!isDisplayed(dialog)) {
            // showModal() ran but the dialog is not actually visible (a CSS conflict, a browser
            // extension removed it, etc.). A dimmed, invisible modal traps the visitor worse
            // than no consent UI at all, so fail open instead.
            if (window.console) {
                console.warn('ndstk-consent: the consent dialog could not be displayed; leaving the page usable.');
            }
            blockingAbandoned = true;
            dialog.close();
        }
    }

    function close() {
        if (!dialog) { return; }
        if (typeof dialog.close === 'function') {
            dialog.close();
        } else {
            dialog.removeAttribute('open');
        }
    }

    // While no decision has been made yet, there is nothing to cancel back to, so Escape must not
    // dismiss the choice. Two layers are needed, because one is not enough:
    //
    // 1. preventDefault() on 'cancel'. This works once the visitor has interacted with the page,
    //    but browsers deliberately ignore it for a dialog opened WITHOUT user activation - which is
    //    exactly our case, since the dialog opens on load. That is anti-abuse behaviour by design
    //    (a page must not be able to trap you), so it cannot be argued with.
    // 2. Reopen on 'close' whenever no decision has been recorded. That covers the first Escape,
    //    which layer 1 cannot. After it, user activation exists and layer 1 handles the rest.
    if (dialog) {
        dialog.addEventListener('cancel', function (event) {
            if (needsDecision) { event.preventDefault(); }
        });

        dialog.addEventListener('close', function () {
            // blockingAbandoned means open() already determined the dialog cannot be displayed and
            // closed it on purpose. Reopening then would loop forever on an invisible modal.
            if (needsDecision && blockingAbandoned === false) { open(); }
        });
    }

    /** Turn inert type="text/plain" placeholders into live scripts for the granted categories. */
    function activateScripts() {
        var blocked = document.querySelectorAll('script[type="text/plain"][data-consent-category]');

        Array.prototype.forEach.call(blocked, function (placeholder) {
            if (!has(placeholder.getAttribute('data-consent-category'))) { return; }

            var live = document.createElement('script');
            var src = placeholder.getAttribute('data-src');

            if (src) {
                live.src = src;
            } else {
                live.text = placeholder.textContent;
            }

            placeholder.parentNode.replaceChild(live, placeholder);
        });
    }

    function updateConsentMode() {
        if (!consentModeEnabled || typeof window.gtag !== 'function') { return; }

        var marketing = has('marketing') ? 'granted' : 'denied';

        window.gtag('consent', 'update', {
            ad_storage: marketing,
            ad_user_data: marketing,
            ad_personalization: marketing,
            analytics_storage: has('statistics') ? 'granted' : 'denied',
            functionality_storage: has('preferences') ? 'granted' : 'denied',
            personalization_storage: has('preferences') ? 'granted' : 'denied'
        });
    }

    function announce() {
        var detail = { categories: currentCategories(), version: policyVersion };

        document.dispatchEvent(new CustomEvent('ndstk:consent-change', { detail: detail }));
        listeners.forEach(function (listener) {
            try { listener(detail); } catch (error) { /* a bad subscriber must not break consent */ }
        });
    }

    function selectedCategories() {
        var inputs = document.querySelectorAll('[data-consent-category-input]');

        return Array.prototype.filter.call(inputs, function (input) {
            return input.checked && !input.disabled;
        }).map(function (input) {
            return input.value;
        });
    }

    function statusElements() {
        return document.querySelectorAll('[data-consent-status]');
    }

    function actionButtons() {
        return document.querySelectorAll('[data-consent-action]');
    }

    /** role="status"/aria-live elements, so screen reader users hear a failure too, not just see it. */
    function showStatus(message) {
        Array.prototype.forEach.call(statusElements(), function (element) {
            element.textContent = message;
            element.hidden = false;
        });
    }

    function clearStatus() {
        Array.prototype.forEach.call(statusElements(), function (element) {
            element.textContent = '';
            element.hidden = true;
        });
    }

    /** Prevents a double-click (or a slow request plus an impatient second click) from firing twice. */
    function setActionButtonsDisabled(disabled) {
        Array.prototype.forEach.call(actionButtons(), function (button) {
            button.disabled = disabled;
        });
    }

    function send(action, categories) {
        return fetch(endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({
                categories: categories,
                action: action,
                culture: document.documentElement.lang || null
            })
        }).then(function (response) {
            if (!response.ok) {
                var error = new Error('Consent request failed: ' + response.status);
                error.status = response.status;
                throw error;
            }
            return response.json();
        }).then(function () {
            // A decision now demonstrably exists (the server accepted it and set the cookie):
            // Escape and the cancel affordance behave normally on any future reopen this page.
            // The Cancel button itself is server-rendered and stays absent until the next
            // navigation - only Escape-suppression needs to be lifted here.
            //
            // This must be cleared BEFORE close(), because the 'close' handler reopens the dialog
            // whenever it closes with no decision recorded. Closing first would bounce it straight
            // back open on the success path.
            needsDecision = false;

            close();
            clearStatus();
            activateScripts();
            updateConsentMode();
            announce();
            return true;
        }).catch(function (error) {
            // Leave the dialog in place: a failed request must not read as a recorded choice.
            if (window.console) { console.error(error); }
            showStatus(error && error.status === 429 ? rateLimitedMessage : errorMessage);
            return false;
        });
    }

    function decide(action) {
        clearStatus();
        setActionButtonsDisabled(true);

        var result;
        if (action === 'accept-all') { result = send(action, ['preferences', 'statistics', 'marketing']); }
        else if (action === 'reject-all') { result = send(action, []); }
        else if (action === 'withdrawn') {
            // Reload only on success: `send` resolves false (never rejects) on a failed
            // request, and a failed withdrawal must not look like a completed one.
            result = send(action, []).then(function (succeeded) {
                if (succeeded) { window.location.reload(); }
                return succeeded;
            });
        } else {
            result = send('custom', selectedCategories());
        }

        return result.then(function (succeeded) {
            setActionButtonsDisabled(false);
            return succeeded;
        });
    }

    document.addEventListener('click', function (event) {
        var target = event.target;
        // This handler lives at the document level for the life of the page, so guard against
        // any click target that is not an Element (e.g. a Text node reached via composed paths).
        if (!target || typeof target.closest !== 'function') { return; }

        var opener = target.closest('[data-consent-open]');
        if (opener) { event.preventDefault(); open(); return; }

        var closer = target.closest('[data-consent-close]');
        if (closer) { event.preventDefault(); close(); return; }

        var actor = target.closest('[data-consent-action]');
        if (actor) { event.preventDefault(); decide(actor.getAttribute('data-consent-action')); }
    });

    // Anything already granted from a previous visit becomes live on this page load too.
    activateScripts();
    updateConsentMode();

    // No decision yet: block the site until one is made.
    if (needsDecision) { open(); }

    window.ndstkConsent = {
        open: open,
        close: close,
        get: readCookie,
        has: has,
        onChange: function (fn) { if (typeof fn === 'function') { listeners.push(fn); } }
    };
})();
