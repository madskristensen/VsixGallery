(function () {
    var btn = document.querySelector('.hamburger-btn');
    var menu = document.getElementById('mobile-menu');
    var header = document.querySelector('header');
    var overlay = document.querySelector('.mobile-menu-overlay');
    var mobileSearch = menu ? menu.querySelector('input[type=search]') : null;
    var previouslyFocused = null;
    if (!btn || !menu || !header) {
        return;
    }

    function setOpen(open) {
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        header.classList.toggle('is-open', open);
        document.body.classList.toggle('menu-open', open);
        if (open) {
            previouslyFocused = document.activeElement;
            menu.removeAttribute('hidden');
            if (mobileSearch) {
                window.requestAnimationFrame(function () { mobileSearch.focus(); });
            }
        } else {
            menu.setAttribute('hidden', '');
            if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
                previouslyFocused.focus();
            }
        }
    }

    btn.addEventListener('click', function () {
        setOpen(btn.getAttribute('aria-expanded') !== 'true');
    });

    if (overlay) {
        overlay.addEventListener('click', function () { setOpen(false); });
    }

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && btn.getAttribute('aria-expanded') === 'true') {
            setOpen(false);
        } else if (e.key === 'Tab' && btn.getAttribute('aria-expanded') === 'true') {
            var focusable = Array.prototype.slice.call(
                menu.querySelectorAll('a[href], button:not([disabled]), input:not([disabled])')
            );
            focusable.unshift(btn);
            var first = focusable[0];
            var last = focusable[focusable.length - 1];

            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        }
    });

    menu.querySelectorAll('a').forEach(function (a) {
        a.addEventListener('click', function () { setOpen(false); });
    });
})();

(function () {
    var hero = document.querySelector('.home-hero');
    var dismiss = hero ? hero.querySelector('.dismiss-hero') : null;
    var status = document.getElementById('site-status');
    var storageKey = 'vsix-gallery-intro-dismissed';

    if (!hero || !dismiss) {
        return;
    }

    try {
        if (window.localStorage.getItem(storageKey) === 'true') {
            hero.hidden = true;
            return;
        }
    } catch (error) {
        if (status) {
            status.textContent = 'The saved introduction preference could not be read.';
        }
    }

    dismiss.addEventListener('click', function () {
        hero.hidden = true;

        try {
            window.localStorage.setItem(storageKey, 'true');
        } catch (error) {
            if (status) {
                status.textContent = 'The introduction was dismissed, but the preference could not be saved.';
            }
        }
    });
})();

(function () {
    var badgeBtns = document.querySelectorAll('.copy-badge-btn');
    var status = document.getElementById('site-status');
    badgeBtns.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var id = btn.getAttribute('data-extension-id');
            var name = btn.getAttribute('data-extension-name');
            var format = btn.getAttribute('data-badge-format') === 'png' ? 'png' : 'svg';
            var origin = window.location.origin;
            var badgeUrl = origin + '/badge/' + encodeURIComponent(id) + '.' + format;
            var pageUrl = origin + '/extension/' + encodeURIComponent(id) + '/';
            var markdown = '[![Install from VSIX Gallery](' + badgeUrl + ')](' + pageUrl + ')';

            if (!navigator.clipboard) {
                btn.textContent = 'Copy unavailable';
                if (status) {
                    status.textContent = 'Clipboard access is unavailable.';
                }
                return;
            }

            navigator.clipboard.writeText(markdown).then(function () {
                var original = btn.textContent;
                btn.textContent = '✓ Copied!';
                btn.classList.add('copied');
                if (status) {
                    status.textContent = name + ' badge copied to the clipboard.';
                }
                setTimeout(function () {
                    btn.textContent = original;
                    btn.classList.remove('copied');
                }, 2000);
            }).catch(function () {
                btn.textContent = 'Copy failed';
                if (status) {
                    status.textContent = 'The badge could not be copied.';
                }
            });
        });
    });
})();
