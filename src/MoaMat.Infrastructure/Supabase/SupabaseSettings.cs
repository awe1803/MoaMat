namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase connection settings, bound from the <c>Supabase</c> section of
/// <c>wwwroot/appsettings.json</c>.
/// </summary>
/// <remarks>
/// <see cref="AnonKey"/> is a <em>publishable</em> key: in a WebAssembly
/// application it is public by construction and shipping it is expected. Access
/// control is carried by the RLS policies of <c>db/rls.sql</c>, never by the
/// secrecy of this key. A <c>service_role</c> key here would be a severe leak -
/// see <see cref="SupabaseKeyInspector"/>.
/// </remarks>
public sealed class SupabaseSettings
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "Supabase";

    /// <summary>Project URL, for instance <c>https://xyz.supabase.co</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Publishable (anon) key shipped to the browser.</summary>
    public string AnonKey { get; set; } = string.Empty;

    /// <summary>
    /// Fails fast when the settings cannot produce a working client. Starting
    /// with a blank URL only defers the failure to the first query, where it
    /// surfaces as an opaque network error.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required value is missing or malformed.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new InvalidOperationException(
                $"Configuration manquante : '{SectionName}:{nameof(Url)}'. "
                + "Copiez wwwroot/appsettings.sample.json vers wwwroot/appsettings.json et renseignez-le.");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                $"Configuration invalide : '{SectionName}:{nameof(Url)}' doit être une URL absolue http(s).");
        }

        if (string.IsNullOrWhiteSpace(AnonKey))
        {
            throw new InvalidOperationException(
                $"Configuration manquante : '{SectionName}:{nameof(AnonKey)}'.");
        }

        var kind = SupabaseKeyInspector.Classify(AnonKey);
        if (kind is SupabaseKeyKind.SecretKey or SupabaseKeyKind.PrivilegedJwt)
        {
            throw new InvalidOperationException(
                $"Clé Supabase refusée : '{SectionName}:{nameof(AnonKey)}' est une clé privilégiée ({kind}). "
                + "Seule une clé publishable / anon peut être embarquée dans un client WebAssembly.");
        }
    }
}
