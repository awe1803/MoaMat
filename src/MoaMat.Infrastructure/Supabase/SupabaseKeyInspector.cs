using System.Text.Json;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Classifies a configured Supabase API key without contacting Supabase.
/// </summary>
/// <remarks>
/// This is the last local guard rail before a key is baked into a static
/// WebAssembly bundle. Shipping a <c>service_role</c> key there hands every
/// visitor full read/write access, bypassing every RLS policy - so the
/// classification is deliberately conservative: anything that decodes to a
/// non-anonymous role is reported as privileged.
/// </remarks>
public static class SupabaseKeyInspector
{
    private const string PublishablePrefix = "sb_publishable_";
    private const string SecretPrefix = "sb_secret_";
    private const string AnonymousRole = "anon";
    private const int JwtSegmentCount = 3;

    /// <summary>Determines what kind of key <paramref name="key"/> is.</summary>
    /// <param name="key">Raw key as configured; may be null or empty.</param>
    public static SupabaseKeyKind Classify(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return SupabaseKeyKind.Unknown;
        }

        var value = key.Trim();

        if (value.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            return SupabaseKeyKind.SecretKey;
        }

        if (value.StartsWith(PublishablePrefix, StringComparison.Ordinal))
        {
            return SupabaseKeyKind.PublishableKey;
        }

        var role = ReadJwtRoleClaim(value);
        return role switch
        {
            null => SupabaseKeyKind.Unknown,
            AnonymousRole => SupabaseKeyKind.AnonymousJwt,
            _ => SupabaseKeyKind.PrivilegedJwt,
        };
    }

    /// <summary>
    /// Reads the <c>role</c> claim of a legacy Supabase JWT. Returns <c>null</c>
    /// when the value is not a JWT or carries no role claim - the signature is
    /// deliberately not verified, only the shape matters here.
    /// </summary>
    private static string? ReadJwtRoleClaim(string value)
    {
        var segments = value.Split('.');
        if (segments.Length != JwtSegmentCount)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            return payload.RootElement.TryGetProperty("role", out var role)
                   && role.ValueKind == JsonValueKind.String
                ? role.GetString()
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            // Not a decodable JWT payload: report "unknown" rather than guess.
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
