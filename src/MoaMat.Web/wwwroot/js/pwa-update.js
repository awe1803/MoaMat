// Browser side of the "new version available" notice, called from PwaUpdateService.
//
// A published MoaMat is served out of the service worker cache, so a deployment
// stays invisible until a new worker takes over - and taking it over means
// reloading the page. Nothing here reloads on its own: a reload nobody asked for
// would throw away a half-filled form. The module only reports that a new
// version is ready and waits for the user to say when.

/** Message the waiting worker listens for; see service-worker-update.js. */
const skipWaitingMessage = 'moamat-skip-waiting';

/** How often a tab left open asks the server whether a new worker exists. */
const updateCheckIntervalMs = 30 * 60 * 1000;

/** Shortest delay between two checks, so tab switching cannot hammer the server. */
const updateCheckThrottleMs = 60 * 1000;

/**
 * Gives up waiting for the new worker to report taking control. Browsers do
 * fire `controllerchange` after `skipWaiting()`, but a notice stuck on screen
 * is worse than a reload that arrives on a timer.
 */
const takeOverTimeoutMs = 3000;

/** What the current watch is holding on to, or null when not watching. */
let watch = null;

let isApplying = false;
let hasReloaded = false;

/**
 * A waiting worker only means "new content" when another one already controls
 * the page: the very first worker of a fresh install also goes through that
 * state, and there is nothing to announce then.
 */
function hasNewContent(registration) {
    return registration.waiting !== null && navigator.serviceWorker.controller !== null;
}

function announce() {
    if (!watch || watch.announced) {
        return;
    }

    watch.announced = true;
    // Not awaited, and failures are swallowed: the .NET side only raises a
    // notice, and a browser event handler has nowhere to report an error to.
    watch.dotNetRef.invokeMethodAsync('OnUpdateAvailableAsync').catch(() => { });
}

/** Watches the worker being installed, and announces it once it is ready. */
function onUpdateFound() {
    const installing = watch && watch.registration ? watch.registration.installing : null;
    if (!installing) {
        return;
    }

    installing.addEventListener('statechange', () => {
        if (installing.state === 'installed' && navigator.serviceWorker.controller !== null) {
            announce();
        }
    });
}

function checkForUpdate() {
    if (!watch || !watch.registration || watch.announced) {
        return;
    }

    const now = Date.now();
    if (now - watch.lastCheck < updateCheckThrottleMs) {
        return;
    }

    watch.lastCheck = now;

    // Best effort: offline, or a server that cannot be reached, simply means
    // the check happens again later.
    watch.registration.update().catch(() => { });
}

function onVisibilityChange() {
    if (document.visibilityState === 'visible') {
        checkForUpdate();
    }
}

function attach(registration) {
    // The watch may already have been stopped while `ready` was pending.
    if (!watch) {
        return;
    }

    watch.registration = registration;
    watch.lastCheck = Date.now();

    if (hasNewContent(registration)) {
        announce();
        return;
    }

    registration.addEventListener('updatefound', onUpdateFound);
    document.addEventListener('visibilitychange', onVisibilityChange);
    watch.timer = setInterval(() => {
        watch.lastCheck = 0;
        checkForUpdate();
    }, updateCheckIntervalMs);
}

function reload() {
    if (hasReloaded) {
        return;
    }

    hasReloaded = true;
    location.reload();
}

/**
 * Starts reporting new versions to the .NET side. Deliberately not async: the
 * registration is only ready once a worker is active, and a browser that never
 * gets one must not keep the .NET call pending for ever.
 */
export function start(dotNetRef) {
    if (!('serviceWorker' in navigator) || watch) {
        return;
    }

    watch = { dotNetRef, registration: null, announced: false, lastCheck: 0, timer: 0 };
    navigator.serviceWorker.ready.then(attach).catch(() => { watch = null; });
}

/** Stops watching and releases every listener `start` took. */
export function stop() {
    if (!watch) {
        return;
    }

    clearInterval(watch.timer);
    document.removeEventListener('visibilitychange', onVisibilityChange);
    if (watch.registration) {
        watch.registration.removeEventListener('updatefound', onUpdateFound);
    }

    watch = null;
}

/**
 * Hands the page over to the waiting worker, then reloads. Does not resolve in
 * the usual case: the browser navigates away while the call is still running.
 */
export async function applyUpdate() {
    if (isApplying) {
        return;
    }

    isApplying = true;

    const registration = (watch && watch.registration)
        || await navigator.serviceWorker.getRegistration();
    const waiting = registration ? registration.waiting : null;

    if (!waiting) {
        // Nothing waiting any more - another tab already took the update over.
        // Reloading is enough to land on the version it activated.
        reload();
        return;
    }

    navigator.serviceWorker.addEventListener('controllerchange', reload, { once: true });
    setTimeout(reload, takeOverTimeoutMs);
    waiting.postMessage(skipWaitingMessage);
}
