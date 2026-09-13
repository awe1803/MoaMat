using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Domain.Accounts;
using MoaMat.Infrastructure.Supabase;
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
        Task.FromResult(new AuthenticationState(BuildPrincipal()));

    /// <inheritdoc />
    public void Dispose() => _client.Auth.RemoveStateChangedListener(OnAuthenticationStateChanged);

    private void OnAuthenticationStateChanged(object sender, Constants.AuthState state) =>
        NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(BuildPrincipal())));

    private ClaimsPrincipal BuildPrincipal()
    {
        var user = _client.Auth.CurrentUser;
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

        // The role lives in the access token (public.custom_access_token_hook
        // copies public.utilisateur_role into it), not in the GoTrue user object.
        var role = SupabaseAccessTokenInspector.ReadRole(_client.Auth.CurrentSession?.AccessToken);
        if (role != AppRole.None)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Code));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }
}
