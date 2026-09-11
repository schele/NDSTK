// Marks the button busy while a form is in flight, and refuses the second submit.
//
// Every form on this site posts and waits: registration sends a mail, logging in hits the database,
// booking takes a lock, the Swish buttons talk to Swish. Without this the page simply sits there
// after the click, which reads as a button that did not work - so people press it again, and a
// second booking attempt or a second registration mail is a worse outcome than a slow one.
//
// No framework and no per-form wiring: one delegated listener covers every form the site has now
// and every one it grows later.
(() => {
    'use strict';

    // The submit event only fires once constraint validation has passed, so a form held back by a
    // required field never reaches this and never gets a spinner it would have to un-spin.
    //
    // Capture phase, so a page's own submit handler cannot silence this by stopping propagation.
    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        // The whole point. A double click on a slow form is the thing being prevented; the spinner
        // is only how that is explained to the person doing it.
        if (form.dataset.busy === 'true') {
            event.preventDefault();
            return;
        }

        form.dataset.busy = 'true';

        // submitter names the button actually pressed, which matters on the Swish panel where one
        // form sits beside another and only the pressed one should spin.
        const button = event.submitter
            ?? form.querySelector('button[type="submit"], button:not([type]), input[type="submit"]');

        if (button) {
            button.classList.add('is-busy');
            button.setAttribute('aria-busy', 'true');
        }
    }, true);

    // Going back restores the page from the browser's cache exactly as it was left - mid-submit,
    // with a button spinning at a request that finished long ago and a guard that now refuses every
    // further attempt. Both have to be cleared or the form is dead until a hard reload.
    window.addEventListener('pageshow', (event) => {
        if (event.persisted !== true) {
            return;
        }

        for (const form of document.querySelectorAll('form[data-busy="true"]')) {
            delete form.dataset.busy;
        }

        for (const button of document.querySelectorAll('.is-busy')) {
            button.classList.remove('is-busy');
            button.removeAttribute('aria-busy');
        }
    });
})();
