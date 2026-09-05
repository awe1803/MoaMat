using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Supabase.Gotrue;

namespace MoaMat.Web.Auth;

/// <summary>
/// Expose l'état d'authentification Supabase (Gotrue) au pipeline
/// d'autorisation Blazor. La session est chargée depuis le navigateur au
/// démarrage (<see cref="BrowserSessionPersistence"/>) puis tenue à jour via les
/// évènements Gotrue (connexion, déconnexion, refresh de jeton).
/// </summary>
public sealed class SupabaseAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private const string AuthenticationType = "Supabase";

    private readonly Supabase.Client _supabase;

    public SupabaseAuthenticationStateProvider(Supabase.Client supabase)
    {
        _supabase = supabase;
        _supabase.Auth.AddStateChangedListener(OnAuthStateChanged);
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(new AuthenticationState(BuildPrincipal(_supabase.Auth.CurrentUser)));

    private void OnAuthStateChanged(object sender, Constants.AuthState state)
        => NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(BuildPrincipal(_supabase.Auth.CurrentUser))));

    private static ClaimsPrincipal BuildPrincipal(User? user)
    {
        if (user?.Id is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var role = ExtractRole(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email ?? user.Id),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>Lit <c>app_metadata.role</c> (source de vérité des policies RLS).</summary>
    private static string? ExtractRole(User user)
    {
        if (user.AppMetadata is null || !user.AppMetadata.TryGetValue("role", out var value))
        {
            return null;
        }

        return value switch
        {
            string s when !string.IsNullOrWhiteSpace(s) => s.Trim().ToLowerInvariant(),
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je
                => je.GetString()?.Trim().ToLowerInvariant(),
            _ => null,
        };
    }

    public void Dispose() => _supabase.Auth.RemoveStateChangedListener(OnAuthStateChanged);
}
