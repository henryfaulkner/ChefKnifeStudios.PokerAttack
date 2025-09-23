export function notifyServerOnUnload(url, data) {
    window.addEventListener('unload', function () {
        try {
            const blob = new Blob([JSON.stringify(data)], { type: 'application/json' });
            navigator.sendBeacon(url, blob);
        } catch (e) { }
    });
}

export function testNotifyServer(url, data) {
    console.log('testNotifyServer', url, data);
    const blob = new Blob([JSON.stringify(data)], { type: 'application/json' });
    var result = navigator.sendBeacon(url, blob);
    console.log('result', result);
}