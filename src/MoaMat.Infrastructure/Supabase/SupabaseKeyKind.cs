namespace MoaMat.Infrastructure.Supabase;

/// <summary>What kind of Supabase API key a configured value turned out to be.</summary>
public enum SupabaseKeyKind
{
    /// <summary>Not recognisable as a Supabase key; treated as harmless but suspect.</summary>
    Unknown = 0,

    /// <summary>Modern publishable key (<c>sb_publishable_...</c>). Safe to ship.</summary>
    PublishableKey = 1,

    /// <summary>Legacy JWT whose <c>role</c> claim is <c>anon</c>. Safe to ship.</summary>
    AnonymousJwt = 2,

    /// <summary>Modern secret key (<c>sb_secret_...</c>). Must never reach a browser.</summary>
    SecretKey = 3,

    /// <summary>Legacy JWT with a privileged <c>role</c> claim, typically <c>service_role</c>.</summary>
    PrivilegedJwt = 4,
}
