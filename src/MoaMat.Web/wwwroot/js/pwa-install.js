// Browser side of the "install MoaMat" tip, called from PwaInstallService.
//
// Only what the browser alone can answer lives here: whether the application
// already runs installed, which manual steps apply, and whether an installation
// prompt was kept. The "never show it again" choice is NOT stored here - it
// belongs to the account, in the database, so it survives cleared site data and
// follows the member from one device to the next.
//
// Nothing here throws for an expected situation: a browser that never offers an
// installation prompt, a user who declines it. Every function resolves to a
// plain value the .NET side turns into a state.

/** The prompt Chromium handed us before Blazor booted; see index.html. */
function deferredPrompt() {
    return window.moamatInstallPrompt ? window.moamatInstallPrompt.event : null;
}

function forgetPrompt() {
    if (window.moamatInstallPrompt) {
        window.moamatInstallPrompt.event = null;
    }
}

/** True once the application runs from the home screen rather than a tab. */
function isStandalone() {
    return window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
}

function isAppleMobile() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent)
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}

/**
 * Which set of manual instructions applies. Only the families whose steps
 * really differ are told apart; anything else falls back to a generic wording.
 */
function platform() {
    if (isAppleMobile()) {
        return 'ios';
    }
    if (/Android/.test(navigator.userAgent)) {
        return 'android';
    }
    if (navigator.maxTouchPoints > 1) {
        return 'unknown';
    }
    return 'desktop';
}

/** Describes what this browser can do about installation right now. */
export function getStatus() {
    return {
        installed: isStandalone(),
        canPrompt: deferredPrompt() !== null,
        platform: platform(),
    };
}

/**
 * Shows the browser's own installation prompt. Must be called from a user
 * gesture, without any await on the .NET side before the call. Resolves to
 * true when the application was installed.
 */
export async function prompt() {
    const deferred = deferredPrompt();
    if (!deferred) {
        return false;
    }

    // The prompt is single-use: Chromium fires a fresh event if the user declines.
    forgetPrompt();

    try {
        await deferred.prompt();
        const choice = await deferred.userChoice;
        return choice.outcome === 'accepted';
    } catch {
        return false;
    }
}
