(function () {
    var el = document.getElementById('readme');
    if (!el) {
        return;
    }

    var url = el.dataset.url;
    if (!url) {
        return;
    }

    function fetchReadme(readmeUrl) {
        return fetch('https://markdownservice.azurewebsites.net/markdown.ashx?url=' + readmeUrl)
            .then(function (response) { return response.text(); });
    }

    function getAlternateUrl(readmeUrl) {
        if (readmeUrl.indexOf('/refs/heads/main/') !== -1) {
            return readmeUrl.replace('/refs/heads/main/', '/refs/heads/master/');
        }
        if (readmeUrl.indexOf('/refs/heads/master/') !== -1) {
            return readmeUrl.replace('/refs/heads/master/', '/refs/heads/main/');
        }
        if (readmeUrl.indexOf('/main/') !== -1) {
            return readmeUrl.replace('/main/', '/master/');
        }
        if (readmeUrl.indexOf('/master/') !== -1) {
            return readmeUrl.replace('/master/', '/main/');
        }
        return null;
    }

    fetchReadme(url).then(function (text) {
        if (text && text.trim().length > 0 && text.indexOf('404') === -1) {
            el.innerHTML = text;
        } else {
            var altUrl = getAlternateUrl(url);
            if (altUrl) {
                fetchReadme(altUrl).then(function (t) { el.innerHTML = t; });
            } else {
                el.innerHTML = text;
            }
        }
    });
})();
