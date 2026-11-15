/*
 * A deliberately small service worker.
 * It only caches the static assets of the interface. Pages, API calls and the
 * SignalR connection always go to the network, so the data is never stale.
 */
const cacheName = 'financeflow-static-v1';
const assets = ['/', '/app.css', '/manifest.webmanifest', '/favicon.svg'];

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(cacheName)
            .then(function (cache) { return cache.addAll(assets); })
            .then(function () { return self.skipWaiting(); })
            .catch(function () { /* the application still works without the cache */ })
    );
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(keys.filter(function (key) { return key !== cacheName; })
                    .map(function (key) { return caches.delete(key); }));
            })
            .then(function () { return self.clients.claim(); })
    );
});

self.addEventListener('fetch', function (event) {
    const request = event.request;

    if (request.method !== 'GET') {
        return;
    }

    const url = new URL(request.url);

    if (url.origin !== self.location.origin || url.pathname.startsWith('/hubs')) {
        return;
    }

    event.respondWith(
        fetch(request)
            .then(function (response) {
                if (response.ok && (url.pathname.startsWith('/css') || url.pathname.startsWith('/js')
                    || url.pathname.startsWith('/lib') || url.pathname.endsWith('.css')
                    || url.pathname.endsWith('.js') || url.pathname.endsWith('.svg')
                    || url.pathname.endsWith('.png') || url.pathname.endsWith('.webmanifest'))) {
                    const copy = response.clone();
                    caches.open(cacheName).then(function (cache) { cache.put(request, copy); });
                }

                return response;
            })
            .catch(function () {
                return caches.match(request).then(function (cached) {
                    return cached || caches.match('/');
                });
            })
    );
});
