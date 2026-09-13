// Web Push handlers, shared by the development and the published service worker
// (both pull this file in with importScripts).
//
// The payload is produced by the Edge Function notify-pending-account:
//   { title, body, url, tag }
// where "url" is relative to the application scope (the app is served under a
// sub-path on GitHub Pages).

self.addEventListener('push', event => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch {
        data = { body: event.data ? event.data.text() : '' };
    }

    event.waitUntil(self.registration.showNotification(data.title || 'MoaMat', {
        body: data.body || '',
        // Royal Moana logo; the badge is a white silhouette on transparency
        // because Android only keeps the alpha channel in the status bar.
        icon: 'notification-icon.png',
        badge: 'notification-badge.png',
        tag: data.tag,
        data: { url: data.url || '' },
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();

    const scope = self.registration.scope;
    let target = new URL(event.notification.data?.url ?? '', scope).href;

    // Never let a payload send the user outside the application.
    if (!target.startsWith(scope)) {
        target = scope;
    }

    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const existing = windows.find(client => client.url.startsWith(scope));

        if (existing) {
            await existing.focus();
            try {
                await existing.navigate(target);
                return;
            } catch {
                // navigate() is refused on a page this worker does not control:
                // fall through and open a fresh window instead.
            }
        }

        await self.clients.openWindow(target);
    })());
});
