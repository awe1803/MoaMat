namespace MoaMat.Domain.Notifications;

/// <summary>
/// Web Push subscription of one browser: where to send (<see cref="Endpoint"/>)
/// and the keys the payload is encrypted with.
/// </summary>
/// <param name="Endpoint">HTTPS URL of the browser push service.</param>
/// <param name="P256dh">Browser public key (base64url).</param>
/// <param name="Auth">Browser authentication secret (base64url).</param>
public sealed record PushSubscription(string Endpoint, string P256dh, string Auth)
{
    /// <summary>
    /// True when the subscription can be stored: an absolute HTTPS endpoint and
    /// both keys present. The database applies the same checks.
    /// </summary>
    public bool IsComplete =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(P256dh)
        && !string.IsNullOrWhiteSpace(Auth);
}
