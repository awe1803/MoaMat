namespace MoaMat.Web.Notifications;

/// <summary>What the push notification opt-in can offer on this browser.</summary>
public enum PushNotificationState
{
    /// <summary>No VAPID key in this deployment: the feature is hidden.</summary>
    NotConfigured,

    /// <summary>The browser has no Web Push support at all.</summary>
    Unsupported,

    /// <summary>iOS / iPadOS outside an installed application: install on the home screen first.</summary>
    RequiresInstall,

    /// <summary>The user refused the permission; only the browser settings can undo it.</summary>
    Denied,

    /// <summary>Supported and allowed, but this browser is not subscribed.</summary>
    Disabled,

    /// <summary>This browser receives the notifications.</summary>
    Enabled,
}
