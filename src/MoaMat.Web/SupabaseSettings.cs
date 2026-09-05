namespace MoaMat.Web;

/// <summary>
/// Configuration Supabase liée à la section "Supabase" de wwwroot/appsettings.json.
/// La clé est une clé <c>publishable</c> (anon) : elle est publique par nature dans une
/// application WebAssembly. Le contrôle d'accès réel repose sur les policies RLS
/// définies dans <c>db/schema.sql</c>.
/// </summary>
public sealed class SupabaseSettings
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;

    public string AnonKey { get; set; } = string.Empty;
}
