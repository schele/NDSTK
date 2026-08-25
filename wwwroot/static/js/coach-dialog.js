// Opens the coach profile dialogs on the class listing.
//
// The whole profile is already in the page, so this only shows and hides it - there is nothing to
// fetch, nothing to fail, and no loading state. Without JavaScript the name renders as a button
// that does nothing, which is the one thing this cannot avoid: a <dialog> has no declarative
// opener. The profile is a nice-to-have beside a class listing that works regardless.
(function () {
    'use strict';

    // One delegated listener rather than one per button. A page can carry a dozen classes, and the
    // dialogs are rendered inline beside each of them.
    document.addEventListener('click', function (event) {
        var opener = event.target.closest('[data-coach-open]');
        if (opener) {
            var dialog = document.getElementById(opener.getAttribute('data-coach-open'));
            // showModal rather than show: it traps focus and takes Escape for free, which is most
            // of what makes a dialog usable with a keyboard.
            if (dialog && typeof dialog.showModal === 'function') {
                dialog.showModal();
            }
            return;
        }

        var closer = event.target.closest('[data-coach-close]');
        if (closer) {
            var open = closer.closest('dialog');
            if (open) {
                open.close();
            }
            return;
        }

        // Clicking the backdrop. The event target is the dialog itself only when the click landed
        // outside its content, because the content is wrapped in elements of its own.
        if (event.target.matches('dialog.coach-dialog')) {
            event.target.close();
        }
    });
})();
