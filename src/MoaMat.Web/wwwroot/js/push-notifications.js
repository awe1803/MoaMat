// Browser side of the Web Push opt-in, called from PushNotificationService.
//
// Every function resolves to a plain object and never throws for an expected
// situation (unsupported browser, permission refused): the .NET side turns that
// state into a message. Unexpected failures still reject.

function isStandalone() {
    return window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
}

function isAppleMobile() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent)
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}

function isSupported() {
    return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
}

function base64UrlToBytes(value) {
    const padded = (value + '='.repeat((4 - value.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(padded), character => character.charCodeAt(0));
}

function sameKey(buffer, bytes) {
    if (!buffer) {
        return false;
    }
    const current = new Uint8Array(buffer);
    return current.length === bytes.length && current.every((value, index) => value === bytes[index]);
}

function toDto(subscription) {
    if (!subscription) {
        return null;
    }
    const json = subscription.toJSON();
    return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, userAgent: navigator.userAgent };
}

async function currentSubscription() {
    const registration = await navigator.serviceWorker.getRegistration();
    return registration ? registration.pushManager.getSubscription() : null;
}

/** Describes what this browser can do right now. */
export async function getStatus() {
    if (!isSupported()) {
        // iOS only exposes Web Push to an application installed on the home screen.
        return { supported: false, requiresInstall: isAppleMobile() && !isStandalone(), permission: 'unsupported', subscription: null };
    }

    return {
        supported: true,
        requiresInstall: false,
        permission: Notification.permission,
        subscription: toDto(await currentSubscription()),
    };
}

/**
 * Asks for the permission and subscribes this browser. Must be called from a
 * user gesture, without any await on the .NET side before the call.
 */
export async function subscribe(vapidPublicKey) {
    if (!isSupported()) {
        return { permission: 'unsupported', subscription: null };
    }

    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
        return { permission, subscription: null };
    }

    const key = base64UrlToBytes(vapidPublicKey);
    const registration = await navigator.serviceWorker.ready;
    let subscription = await registration.pushManager.getSubscription();

    // A subscription made with another server key (key rotation) cannot be reused.
    if (subscription && !sameKey(subscription.options.applicationServerKey, key)) {
        await subscription.unsubscribe();
        subscription = null;
    }

    subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });

    return { permission, subscription: toDto(subscription) };
}

/** Unsubscribes this browser. Resolves to the endpoint that was removed, or null. */
export async function unsubscribe() {
    if (!isSupported()) {
        return null;
    }

    const subscription = await currentSubscription();
    if (!subscription) {
        return null;
    }

    const endpoint = subscription.endpoint;
    await subscription.unsubscribe();
    return endpoint;
}
