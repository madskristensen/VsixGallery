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
