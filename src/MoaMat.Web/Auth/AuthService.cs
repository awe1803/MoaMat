using Microsoft.AspNetCore.Components;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using static Supabase.Gotrue.Exceptions.FailureHint;

namespace MoaMat.Web.Auth;

/// <summary>Résultat d'une opération d'authentification, adapté à l'affichage.</summary>
public readonly record struct AuthResult(bool Succeeded, string? Error)
{
    public static AuthResult Ok { get; } = new(true, null);
    public static AuthResult Fail(string error) => new(false, error);
}

/// <summary>
/// Point d'entrée unique de l'UI pour la connexion / déconnexion et le cycle de
/// réinitialisation de mot de passe. Encapsule <see cref="Supabase.Client"/> et
/// traduit les erreurs Gotrue en messages français.
/// </summary>
public sealed class AuthService
{
    private readonly Supabase.Client _supabase;
    private readonly NavigationManager _navigation;

    public AuthService(Supabase.Client supabase, NavigationManager navigation)
    {
        _supabase = supabase;
        _navigation = navigation;
    }

    public bool IsSignedIn => _supabase.Auth.CurrentUser?.Id is not null;

    public string? CurrentEmail => _supabase.Auth.CurrentUser?.Email;

    /// <summary>URL absolue vers laquelle Supabase renvoie après clic sur le lien de récupération.</summary>
    public string PasswordResetRedirectUrl => _navigation.ToAbsoluteUri("reinitialiser-mot-de-passe").ToString();

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        try
        {
            var session = await _supabase.Auth.SignIn(email.Trim(), password);
            return session?.User?.Id is not null
                ? AuthResult.Ok
                : AuthResult.Fail("Connexion impossible. Réessayez.");
        }
        catch (GotrueException ex)
        {
            return AuthResult.Fail(Translate(ex));
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            await _supabase.Auth.SignOut();
        }
        catch (GotrueException)
        {
            // Déconnexion best-effort : la session locale est de toute façon purgée.
        }
    }

    /// <summary>Envoie l'e-mail de réinitialisation (lien de récupération).</summary>
    public async Task<AuthResult> SendPasswordResetEmailAsync(string email)
    {
        try
        {
            await _supabase.Auth.ResetPasswordForEmail(new ResetPasswordForEmailOptions(email.Trim())
            {
                RedirectTo = PasswordResetRedirectUrl,
            });
            return AuthResult.Ok;
        }
        catch (GotrueException ex)
        {
            return AuthResult.Fail(Translate(ex));
        }
    }

    /// <summary>
    /// Établit la session à partir des jetons présents dans l'URL de retour
    /// (fragment <c>#access_token=…&amp;type=recovery</c>).
    /// </summary>
    public async Task<AuthResult> EstablishSessionFromUrlAsync()
    {
        try
        {
            var session = await _supabase.Auth.GetSessionFromUrl(new Uri(_navigation.Uri), storeSession: true);
            return session?.User?.Id is not null
                ? AuthResult.Ok
                : AuthResult.Fail("Lien de réinitialisation invalide ou expiré.");
        }
        catch (Exception ex) when (ex is GotrueException or FormatException or ArgumentException)
        {
            return AuthResult.Fail("Lien de réinitialisation invalide ou expiré.");
        }
    }

    /// <summary>Applique le nouveau mot de passe à l'utilisateur de la session courante.</summary>
    public async Task<AuthResult> UpdatePasswordAsync(string newPassword)
    {
        try
        {
            var user = await _supabase.Auth.Update(new UserAttributes { Password = newPassword });
            return user?.Id is not null
                ? AuthResult.Ok
                : AuthResult.Fail("Mise à jour du mot de passe impossible.");
        }
        catch (GotrueException ex)
        {
            return AuthResult.Fail(Translate(ex));
        }
    }

    private static string Translate(GotrueException ex) => ex.Reason switch
    {
        Reason.UserBadLogin or Reason.UserBadPassword or Reason.UserBadMultiple
            => "E-mail ou mot de passe incorrect.",
        Reason.UserBadEmailAddress => "Adresse e-mail invalide.",
        Reason.UserEmailNotConfirmed => "Adresse e-mail non confirmée.",
        Reason.UserMissingInformation => "Mot de passe trop faible (6 caractères minimum).",
        Reason.UserTooManyRequests => "Trop de tentatives. Patientez quelques minutes.",
        Reason.Offline or Reason.NetworkError or Reason.CloudflareNetworkError
            => "Problème réseau. Vérifiez votre connexion.",
        Reason.BadSessionUrl or Reason.NoSessionFound or Reason.ExpiredRefreshToken or Reason.InvalidRefreshToken
            => "Lien de réinitialisation invalide ou expiré.",
        _ => "Une erreur est survenue. Réessayez.",
    };
}
