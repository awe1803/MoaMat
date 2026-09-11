using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Domain.Accounts;
using Supabase.Gotrue;

namespace MoaMat.Web.Authentication;

/// <summary>
/// Exposes the Supabase (GoTrue) session to the Blazor authorization pipeline.
/// The session is restored from the browser at start-up and then kept in sync
/// through GoTrue state events: sign-in, sign-out, token refresh.
/// </summary>
/// <remarks>
/// The role claim built here drives navigation and screen affordances. It is
/// read from a token the client holds, so it is never trusted as a security
/// decision: the RLS policies of <c>db/rls.sql</c> re-check every access
/// server-side.
/// </remarks>
internal sealed class SupabaseAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private const string AuthenticationType = "Supabase";
    private const string RoleMetadataKey = "role";

    private readonly Supabase.Client _client;

    /// <summary>Subscribes to GoTrue state changes.</summary>
    /// <param name="client">Configured Supabase client.</param>
    public SupabaseAuthenticationStateProvider(Supabase.Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _client.Auth.AddStateChangedListener(OnAuthenticationStateChanged);
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(BuildPrincipal(_client.Auth.CurrentUser)));

    /// <inheritdoc />
    public void Dispose() => _client.Auth.RemoveStateChangedListener(OnAuthenticationStateChanged);

    private void OnAuthenticationStateChanged(object sender, Constants.AuthState state) =>
        NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(BuildPrincipal(_client.Auth.CurrentUser))));

    private static ClaimsPrincipal BuildPrincipal(User? user)
    {
        if (user?.Id is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email ?? user.Id),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        var role = ReadRole(user);
        if (role != AppRole.None)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Code));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>
    /// Reads <c>app_metadata.role</c>, which the SQL hook
    /// <c>public.custom_access_token_hook</c> copies from
    /// <c>public.utilisateur_role</c> - the source of truth the RLS policies use.
    /// </summary>
    private static AppRole ReadRole(User user)
    {
        if (user.AppMetadata is null || !user.AppMetadata.TryGetValue(RoleMetadataKey, out var value))
        {
            return AppRole.None;
        }

        var code = value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };

        return AppRole.FromCode(code?.Trim().ToLower(CultureInfo.InvariantCulture));
    }
}
