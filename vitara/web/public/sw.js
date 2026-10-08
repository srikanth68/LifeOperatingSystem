// Vitara's service worker: it makes the app open instantly and survive a bad connection, and it
// is deliberately forgetful about everything personal.
//
// What it keeps: the app itself (its page, scripts, styles, icons).
// What it NEVER keeps: anything under /svc/. That is every request to the health servers and to
// sign-in, i.e. readings, labs and tokens. They always go to the network, are never written to a
// cache, and so are never readable from the phone's storage by anything but the app's own session.
//
// If the network is down the shell still opens, and the app says its data cannot be reached
// rather than showing numbers that may be days old and looking current.

const SHELL = 'vitara-shell-v1';
const ASSETS = 'vitara-assets-v1';
const SHELL_FILES = ['/', '/vitara.svg', '/manifest.webmanifest', '/icons/icon-192.png', '/icons/icon-512.png'];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(SHELL)
      .then(cache => Promise.all(SHELL_FILES.map(f => cache.add(f).catch(() => undefined))))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k !== SHELL && k !== ASSETS).map(k => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', event => {
  const req = event.request;
  if (req.method !== 'GET') return;

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  // Personal data and sign-in: straight to the network, untouched, never stored.
  if (url.pathname.startsWith('/svc/')) return;

  // Opening the app: the live page if there is one, the saved shell if there is not.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then(res => { const copy = res.clone(); caches.open(SHELL).then(c => c.put('/', copy)); return res; })
        .catch(() => caches.match('/')),
    );
    return;
  }

  // Built files have a hash in their name, so a cached one is never stale.
  if (url.pathname.startsWith('/assets/')) {
    event.respondWith(
      caches.match(req).then(hit => hit || fetch(req).then(res => {
        if (res.ok) { const copy = res.clone(); caches.open(ASSETS).then(c => c.put(req, copy)); }
        return res;
      })),
    );
    return;
  }

  // Icons and the like: the network first, the saved copy as a fallback.
  event.respondWith(fetch(req).catch(() => caches.match(req)));
});
