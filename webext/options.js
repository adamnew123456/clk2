function save(e) {
    e.preventDefault();
    browser.storage.local.set({
        code: document.querySelector('#endpoint').value,
    });
}

const default_endpoint = 'http://localhost:6996/';

function load() {
    function loadCode(result) {
        document.querySelector('#endpoint').value =
            result.endpoint || default_endpoint;
    }

    browser.storage.local.get('endpoint').then(loadCode);
}

document.addEventListener('DOMContentLoaded', load);
document.querySelector('form').addEventListener('submit', save);
