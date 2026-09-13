using System.Text.Json;
using MoaMat.Domain.Accounts;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>Reads the application claims of a Supabase session access token.</summary>
public static class SupabaseAccessTokenInspector
{
    private const string AppMetadataClaim = "app_metadata";
    private const string RoleKey = "role";

    /// <summary>
    /// Reads <c>app_metadata.role</c> from the access token. That claim is written
    /// by <c>public.custom_access_token_hook</c> into the JWT only: the user object
    /// GoTrue returns alongside the session does not carry it.
    /// </summary>
    /// <param name="accessToken">Session access token; may be null.</param>
    /// <returns>The role, or <see cref="AppRole.None"/> when absent or unreadable.</returns>
    public static AppRole ReadRole(string? accessToken)
    {
        using var payload = JwtPayload.TryParse(accessToken);

        if (payload is null
            || payload.RootElement.ValueKind != JsonValueKind.Object
            || !payload.RootElement.TryGetProperty(AppMetadataClaim, out var metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty(RoleKey, out var role)
            || role.ValueKind != JsonValueKind.String)
        {
            return AppRole.None;
        }

        return AppRole.FromCode(role.GetString());
    }
}
