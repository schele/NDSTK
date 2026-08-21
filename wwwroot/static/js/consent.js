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
    var bar = document.querySelector('[data-consent-bar]');

    function open() {
        if (!dialog) { return; }
        if (typeof dialog.showModal === 'function') {
            dialog.showModal();
        } else {
            dialog.setAttribute('open', 'open');
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

    /** Turn inert `type="text/plain"` placeholders into live scripts for the granted categories. */
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
            if (!response.ok) { throw new Error('Consent request failed: ' + response.status); }
            return response.json();
        }).then(function () {
            close();
            if (bar) { bar.hidden = true; }
            activateScripts();
            updateConsentMode();
            announce();
        }).catch(function (error) {
            // Leave the bar in place: a failed request must not read as a recorded choice.
            if (window.console) { console.error(error); }
        });
    }

    function decide(action) {
        if (action === 'accept-all') { return send(action, ['preferences', 'statistics', 'marketing']); }
        if (action === 'reject-all') { return send(action, []); }
        if (action === 'withdrawn') {
            return send(action, []).then(function () { window.location.reload(); });
        }
        return send('custom', selectedCategories());
    }

    document.addEventListener('click', function (event) {
        var opener = event.target.closest('[data-consent-open]');
        if (opener) { event.preventDefault(); open(); return; }

        var closer = event.target.closest('[data-consent-close]');
        if (closer) { event.preventDefault(); close(); return; }

        var actor = event.target.closest('[data-consent-action]');
        if (actor) { event.preventDefault(); decide(actor.getAttribute('data-consent-action')); }
    });

    // Anything already granted from a previous visit becomes live on this page load too.
    activateScripts();
    updateConsentMode();

    window.ndstkConsent = {
        open: open,
        close: close,
        get: readCookie,
        has: has,
        onChange: function (fn) { if (typeof fn === 'function') { listeners.push(fn); } }
    };
})();
