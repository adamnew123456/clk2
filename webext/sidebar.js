const default_endpoint = "http://localhost:6996/";

function object_keys(o) {
    let props = [];
    for (let key in o) {
        if (o.hasOwnProperty(key)) {
            props.push(key);
        }
    }
    return props;
}

let error_ticks = 0;

function display_status(message, temporary) {
    if (!temporary) {
        error_ticks = 10;
        document.querySelector('#status').innerText = message;
    } else if (error_ticks == 0) {
        document.querySelector('#status').innerText = message;
    } else {
        error_ticks--;
    }
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
        .catch(display_status);
}

function stop_clock(event) {
    const clock_id = event.target.dataset.clock;
    call_rpc('stop', [clock_id])
        .catch(display_status);
}

function finish_clock(event) {
    const clock_id = event.target.dataset.clock;
    call_rpc('finish', [clock_id])
        .catch(display_status);
}

let history_visible = false;

function show_history(event) {
    const clock_id = event.target.dataset.clock;

    const history_container = document.querySelector('.history-container');
    function render_history(entries) {
        entries.forEach(entry => {
            const history_item = document.createElement('div');
            history_item.className = 'history-item';
            history_container.append(history_item);

            const history_event = document.createElement('div');
            history_event.className = 'history-event';
            history_event.innerText = entry.event;
            history_item.appendChild(history_event);

            const history_cumulative = document.createElement('div');
            history_cumulative.className = 'history-cumulative';
            history_cumulative.innerText = render_timespan(entry.cumulative_sec);
            history_item.appendChild(history_cumulative);

            const history_timestamp = document.createElement('div');
            history_timestamp.className = 'history-timestamp';
            history_timestamp.innerText = entry.timestamp;
            history_item.appendChild(history_timestamp);
        });

        document.querySelector('#clock-list-page').className = 'hidden';
        document.querySelector('#history-list-page').className = '';
        history_visible = true;
    }

    call_rpc('history', [clock_id])
        .then(render_history)
        .catch(display_status);
}

let ui_cache = {};

function update_clocks() {
    const clock_base = document.querySelector('.clock-container');

    if (history_visible) {
        while (clock_base.firstChild) {
            clock_base.removeChild(clock_base.firstChild);
        }
        ui_cache = {};
        return;
    }

    function new_clockui(clock) {
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
        control_start.innerText = '|>';
        control_start.dataset.clock = clock.id;
        control_start.addEventListener('click', start_clock);
        dom_controls.appendChild(control_start);

        const control_stop = document.createElement('button');
        control_stop.innerText = '||';
        control_stop.dataset.clock = clock.id;
        control_stop.addEventListener('click', stop_clock);
        dom_controls.appendChild(control_stop);

        const control_finish = document.createElement('button');
        control_finish.innerText = '<<';
        control_finish.dataset.clock = clock.id;
        control_finish.addEventListener('click', finish_clock);
        dom_controls.appendChild(control_finish);

        const control_history = document.createElement('button');
        control_history.innerText = 'History';
        control_history.dataset.clock = clock.id;
        control_history.addEventListener('click', show_history);
        dom_controls.appendChild(control_history);

        ui_cache[clock.id] = {
            id: clock.id,
            root: dom_root,
            elapsed: dom_elapsed,
            status: dom_status
        };

        return dom_root;
    }

    function render_clocks(clocks) {
        let id_clocks = {};
        clocks.forEach(clock => {
            id_clocks[clock.id] = clock;
        });

        let existing_ui_ids = object_keys(ui_cache);
        let changed_clocks = {};
        clocks.forEach(clock => {
            if (clock.id in ui_cache) {
                changed_clocks[clock.id] = true;
                const ui = ui_cache[clock.id];
                ui.elapsed.innerText = render_timespan(clock.elapsed_sec);
                ui.status.innerText = render_status(clock.status);
            } else {
                const dom_root = new_clockui(clock);
                clock_base.appendChild(dom_root);
            }
        });

        existing_ui_ids.forEach(ui_id => {
            if (!(ui_id in changed_clocks)) {
                const dom_root = ui_cache[ui_id].root;
                dom_root.parentNode.removeChild(dom_root);
                delete ui_cache[ui_id];
            }
        });

        display_status('OK', true);
    }

    call_rpc('list', [])
        .then(render_clocks)
        .catch(display_status);
}

document.querySelector('#new-clock-name').addEventListener('keydown', event => {
    if (event.key == 'Enter') {
        event.preventDefault();
        const new_name = document.querySelector('#new-clock-name');
        call_rpc('start', [new_name.value]).catch(display_status);

        new_name.value = '';
        return;
    }
});

document.querySelector('#history-back').addEventListener('click', event => {
    const history_container = document.querySelector('.history-container');
    while (history_container.firstChild) {
        history_container.removeChild(history_container.firstChild);
    }

    document.querySelector('#history-list-page').className = 'hidden';
    document.querySelector('#clock-list-page').className = '';
    history_visible = false;
});

update_clocks();
window.setInterval(update_clocks, 1000);
