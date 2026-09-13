using MoaMat.Domain.Notifications;

namespace MoaMat.Web.Notifications;

/// <summary>
/// Web Push settings, bound from the optional <c>Push</c> section of
/// <c>wwwroot/appsettings.json</c>.
/// </summary>
/// <remarks>
/// Only the VAPID <em>public</em> key belongs here; it is public by design. The
/// private key lives in the secrets of the Edge Function
/// <c>notify-pending-account</c>. Without a key the feature is simply hidden.
/// </remarks>
public sealed class PushNotificationSettings
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "Push";

    /// <summary>VAPID public key (base64url), shared with the Edge Function.</summary>
    public string VapidPublicKey { get; set; } = string.Empty;

    /// <summary>True when push notifications are configured for this deployment.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(VapidPublicKey);

    /// <summary>
    /// Fails fast on a malformed key - in particular a private key pasted in the
    /// public slot, which would otherwise ship to every browser.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is present but is not a VAPID public key.</exception>
    public void Validate()
    {
        if (IsEnabled && !Domain.Notifications.VapidPublicKey.IsValid(VapidPublicKey))
        {
            throw new InvalidOperationException(
                $"Configuration invalide : '{SectionName}:{nameof(VapidPublicKey)}' n'est pas une clé publique VAPID "
                + "(65 octets, base64url). Ne jamais y placer la clé privée.");
        }
    }
}
