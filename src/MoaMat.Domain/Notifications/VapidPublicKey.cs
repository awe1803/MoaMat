namespace MoaMat.Domain.Notifications;

/// <summary>
/// Shape check for the VAPID public key shipped to the browser.
/// </summary>
/// <remarks>
/// A VAPID public key is an uncompressed P-256 point: 65 bytes starting with
/// <c>0x04</c>, base64url-encoded (87 characters). The matching private key is
/// 32 bytes: rejecting anything that is not a public point also stops the
/// private key from being configured on the client by mistake.
/// </remarks>
public static class VapidPublicKey
{
    private const int PointLength = 65;
    private const byte UncompressedPointPrefix = 0x04;

    /// <summary>True when <paramref name="value"/> is a well-formed VAPID public key.</summary>
    /// <param name="value">Base64url-encoded key.</param>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var base64 = value.Trim().Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

        Span<byte> bytes = stackalloc byte[PointLength + 3];
        return Convert.TryFromBase64String(base64, bytes, out var written)
            && written == PointLength
            && bytes[0] == UncompressedPointPrefix;
    }
}
