(function () {
    var el = document.getElementById('readme');
    if (!el || !el.dataset.url) {
        return;
    }

    var SERVICE = 'https://markdownservice.azurewebsites.net/markdown.ashx?url=';

    var policy = window.trustedTypes
        ? trustedTypes.createPolicy('markdown-html', { createHTML: function (s) { return s; } })
        : null;

    function setHtml(target, html) {
        target.innerHTML = policy ? policy.createHTML(html) : html;
    }

    function fetchReadme(readmeUrl) {
        return fetch(SERVICE + readmeUrl).then(function (response) {
            return response.text().then(function (text) {
                return { ok: response.ok, text: text };
            });
        });
    }

    function getAlternateUrl(readmeUrl) {
        var swaps = [
            ['/refs/heads/main/', '/refs/heads/master/'],
            ['/refs/heads/master/', '/refs/heads/main/'],
            ['/main/', '/master/'],
            ['/master/', '/main/']
        ];
        for (var i = 0; i < swaps.length; i++) {
            if (readmeUrl.indexOf(swaps[i][0]) !== -1) {
                return readmeUrl.replace(swaps[i][0], swaps[i][1]);
            }
        }
        return null;
    }

    function isValid(result) {
        return result.ok && result.text && result.text.trim().length > 0;
    }

    fetchReadme(el.dataset.url).then(function (result) {
        if (isValid(result)) {
            setHtml(el, result.text);
            return;
        }
        var altUrl = getAlternateUrl(el.dataset.url);
        if (!altUrl) {
            el.remove();
            return;
        }
        fetchReadme(altUrl).then(function (alt) {
            if (isValid(alt)) {
                setHtml(el, alt.text);
            } else {
                el.remove();
            }
        });
    });
})();
