const default_endpoint = "http://localhost:6996/";

function display_error(message) {
    document.querySelector('#status').innerText = message;
}

function render_timespan(all_seconds) {
    const hours = (all_seconds / (60 * 60)) | 0;
    const remainder = all_seconds % (60 * 60);

    const minutes = (remainder / 60) | 0;
    const seconds = remainder % 60;

    if (hours > 0) {
        return hours + ":" + ("" + minutes).padStart(2, '0') + ":" + ("" + seconds).padStart(2, '0');
    } else if (minutes > 0) {
        return "0:" + ("" + minutes).padStart(2, '0') + ":" + ("" + seconds).padStart(2, '0');
    } else {
        return "0:00:" + ("" + seconds).padStart(2, '0');
    }
}

function render_status(status) {
    switch (status) {
    case "in": return "Started";
    case "out": return "Stopped";
    case "reset": return "Reset";
    }
}

function call_rpc(method, params) {
    const request = {
        jsonrpc: "2.0",
        id: 1,
        method: method,
        params: params
    };

    return browser
        .storage
        .local
        .get('endpoint')
        .then(result => {
            const endpoint = result.endpoint || default_endpoint;

            return new Promise((resolve, reject) => {
                const xhr = new XMLHttpRequest();
                xhr.open('POST', endpoint);
                xhr.setRequestHeader('Content-Type', 'application/json-rpc');
                xhr.onabort = () => reject('Aborted');
                xhr.onerror = () => reject('Received error');
                xhr.ontimeout = () => reject('Timed out');
                xhr.onload = () => resolve(JSON.parse(xhr.responseText));
                xhr.send(JSON.stringify(request));
            });
        }).then(json => {
            return new Promise((resolve, reject) => {
                if ("error" in json) {
                    const error_data = json.error.data;
                    if ("Message" in error_data) {
                        reject(error_data.Message);
                    } else {
                        reject(json.error.message);
                    }
                } else {
                    resolve(json.result);
                }
            });
        });
}

function start_clock(event) {
    const clock_id = event.target.dataset.clock;
    call_rpc('start', [clock_id])
        .catch(display_error);
}

function stop_clock(event) {
    const clock_id = event.target.dataset.clock;
    call_rpc('stop', [clock_id])
        .catch(display_error);
}

function finish_clock(event) {
    const clock_id = event.target.dataset.clock;
    call_rpc('finish', [clock_id])
        .catch(display_error);
}

function update_clocks() {
    const clock_base = document.querySelector('.clock-container');
    while (clock_base.firstChild) {
        clock_base.removeChild(clock_base.firstChild);
    }

    function render_clocks(clocks) {
        clocks.forEach(clock => {
            const dom_root = document.createElement('div');
            dom_root.className = 'clock-item';

            const dom_title = document.createElement('div');
            dom_title.className = 'clock-title';
            dom_title.innerText = clock.id;
            dom_root.appendChild(dom_title);

            const dom_elapsed = document.createElement('div');
            dom_elapsed.className = 'clock-elapsed';
            dom_elapsed.innerText = render_timespan(clock.elapsed_sec);
            dom_root.appendChild(dom_elapsed);

            const dom_status = document.createElement('div');
            dom_status.className = 'clock-status';
            dom_status.innerText = render_status(clock.status);
            dom_root.appendChild(dom_status);

            const dom_controls = document.createElement('div');
            dom_controls.className = 'clock-control';
            dom_root.appendChild(dom_controls);

            const control_start = document.createElement('button');
            control_start.innerText = 'Start';
            control_start.dataset.clock = clock.id;
            control_start.addEventListener('click', start_clock);
            dom_controls.appendChild(control_start);

            const control_stop = document.createElement('button');
            control_stop.innerText = 'Stop';
            control_stop.dataset.clock = clock.id;
            control_stop.addEventListener('click', stop_clock);
            dom_controls.appendChild(control_stop);

            const control_finish = document.createElement('button');
            control_finish.innerText = 'Reset';
            control_finish.dataset.clock = clock.id;
            control_finish.addEventListener('click', finish_clock);
            dom_controls.appendChild(control_finish);

            clock_base.appendChild(dom_root);
        });
    }

    call_rpc('list', [])
        .then(render_clocks)
        .catch(display_error);
}

document.querySelector('#new-clock-name').addEventListener('keydown', event => {
    if (event.key == 'Enter') {
        event.preventDefault();
        const new_name = document.querySelector('#new-clock-name');
        call_rpc('start', [new_name.value]).catch(display_error);

        new_name.value = '';
        return;
    }
});

update_clocks();
window.setInterval(update_clocks, 5000);
