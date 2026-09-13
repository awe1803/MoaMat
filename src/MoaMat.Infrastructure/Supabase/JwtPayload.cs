using System.Text.Json;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>Decodes the payload of a JWT. The signature is never verified.</summary>
internal static class JwtPayload
{
    private const int SegmentCount = 3;

    /// <summary>
    /// Parses the payload segment of <paramref name="token"/>. Returns <c>null</c>
    /// when the value is not a decodable JWT.
    /// </summary>
    /// <param name="token">Raw token; may be null or empty.</param>
    public static JsonDocument? TryParse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var segments = token.Trim().Split('.');
        if (segments.Length != SegmentCount)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(DecodeBase64Url(segments[1]));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string segment)
    {
        var standard = segment.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (standard.Length % 4)) % 4;
        return Convert.FromBase64String(standard.PadRight(standard.Length + padding, '='));
    }
}
