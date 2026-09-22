// Browser side of the "no internet access" page, called from ConnectivityService.
//
// `navigator.onLine` is only trustworthy when it says false: a device sitting
// behind a captive Wi-Fi portal, or on a connection that carries nothing
// useful, reports true all the same. So the browser's own answer is only ever
// taken as a reason to look, and every verdict is settled by actually reaching
// the MoaMat server.
//
// That probe deliberately aims at the Supabase project rather than at a file of
// our own: a published MoaMat is served out of the service worker cache, so a
// same-origin request succeeds with the network unplugged and would prove
// nothing. It is sent `no-cors`, so the opaque answer only says that the
// request completed - which is exactly the question being asked, and asks
// nothing of the server's CORS configuration.

/** Gives up on a probe that never answers; a hanging request is a dead link. */
const probeTimeoutMs = 5000;

/**
 * How often the page checks by itself while it is down. Only while down: a
 * connection that works needs no polling, the browser reports its loss.
 * Coming back, on the other hand, is not always announced - a captive portal
 * signed into in another tab fires no event here.
 */
const recheckIntervalMs = 15 * 1000;

/** What the current watch is holding on to, or null when not watching. */
let watch = null;

function timeoutSignal() {
    // Absent on older WebKit; a probe without a deadline still resolves, it
    // just leans on the browser's own network timeout.
    return typeof AbortSignal.timeout === 'function' ? AbortSignal.timeout(probeTimeoutMs) : undefined;
}

/** Answers whether the MoaMat server can be reached right now. */
async function probe(probeUrl) {
    if (navigator.onLine === false) {
        return false;
    }

    try {
        await fetch(probeUrl, {
            method: 'HEAD',
            mode: 'no-cors',
            cache: 'no-store',
            signal: timeoutSignal(),
        });
        return true;
    } catch {
        // Every failure reads the same way to the user: the server is out of
        // reach. Which of DNS, TLS or routing gave up is of no help to them.
        return false;
    }
}

/** Reports a verdict to the .NET side, but only when it changed. */
function report(isOnline) {
    if (!watch || watch.isOnline === isOnline) {
        return;
    }

    watch.isOnline = isOnline;
    // Not awaited, and failures are swallowed: the .NET side only shows or
    // hides a page, and a browser event handler has nowhere to report an error.
    watch.dotNetRef.invokeMethodAsync('OnConnectivityChangedAsync', isOnline).catch(() => { });
}

async function recheck() {
    if (!watch) {
        return;
    }

    report(await probe(watch.probeUrl));
}

/** The browser saying "offline" is trusted as is: it is never wrong that way. */
function onOffline() {
    report(false);
}

/** The browser saying "online" only earns a probe; the probe decides. */
function onOnline() {
    recheck();
}

function onVisibilityChange() {
    if (document.visibilityState === 'visible' && watch && !watch.isOnline) {
        recheck();
    }
}

/**
 * Starts reporting connectivity changes to the .NET side. The initial state is
 * not announced here: the caller asks for it with `check`, so the page is never
 * put up on an assumption.
 */
export function start(dotNetRef, probeUrl) {
    if (watch) {
        return;
    }

    watch = { dotNetRef, probeUrl, isOnline: true, timer: 0 };

    window.addEventListener('online', onOnline);
    window.addEventListener('offline', onOffline);
    document.addEventListener('visibilitychange', onVisibilityChange);
    watch.timer = setInterval(() => {
        if (watch && !watch.isOnline) {
            recheck();
        }
    }, recheckIntervalMs);
}

/** Stops watching and releases every listener `start` took. */
export function stop() {
    if (!watch) {
        return;
    }

    clearInterval(watch.timer);
    window.removeEventListener('online', onOnline);
    window.removeEventListener('offline', onOffline);
    document.removeEventListener('visibilitychange', onVisibilityChange);

    watch = null;
}

/**
 * Checks on demand - at start-up, and behind the "Réessayer" button. The answer
 * is returned to the caller and, when a watch is running, also folded into its
 * state so a later change is still reported as a change.
 */
export async function check(probeUrl) {
    const isOnline = await probe(watch ? watch.probeUrl : probeUrl);
    report(isOnline);
    return isOnline;
}
