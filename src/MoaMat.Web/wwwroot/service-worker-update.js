// Update hand-over, shared by the development and the published service worker.
//
// A freshly installed worker waits until every tab still using the old one is
// gone, which for an installed application can be days. The "new version"
// notice offers the user a button instead, and this listener is what that
// button reaches: it is the only thing allowed to cut the wait short.
self.addEventListener('message', event => {
    if (event.data === 'moamat-skip-waiting') {
        self.skipWaiting();
    }
});
