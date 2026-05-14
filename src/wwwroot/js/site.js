(function () {
    var btn = document.querySelector('.hamburger-btn');
    var menu = document.getElementById('mobile-menu');
    var header = document.querySelector('header');
    var overlay = document.querySelector('.mobile-menu-overlay');
    if (!btn || !menu || !header) {
        return;
    }

    function setOpen(open) {
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        header.classList.toggle('is-open', open);
        if (open) {
            menu.removeAttribute('hidden');
        } else {
            menu.setAttribute('hidden', '');
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
            btn.focus();
        }
    });

    menu.querySelectorAll('a').forEach(function (a) {
        a.addEventListener('click', function () { setOpen(false); });
    });
})();

(function () {
    var badgeBtns = document.querySelectorAll('.copy-badge-btn');
    badgeBtns.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var id = btn.getAttribute('data-extension-id');
            var name = btn.getAttribute('data-extension-name');
            var origin = window.location.origin;
            var badgeUrl = origin + '/badge/' + encodeURIComponent(id) + '.svg';
            var pageUrl = origin + '/extension/' + encodeURIComponent(id) + '/';
            var markdown = '[![Install from VSIX Gallery](' + badgeUrl + ')](' + pageUrl + ')';

            navigator.clipboard.writeText(markdown).then(function () {
                var original = btn.textContent;
                btn.textContent = '✓ Copied!';
                btn.classList.add('copied');
                setTimeout(function () {
                    btn.textContent = original;
                    btn.classList.remove('copied');
                }, 2000);
            });
        });
    });
})();
