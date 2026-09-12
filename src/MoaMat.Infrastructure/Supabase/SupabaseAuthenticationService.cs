using Microsoft.Extensions.Logging;
using MoaMat.Domain.Authentication;
using MoaMat.Domain.Common;
using MoaMat.Domain.Navigation;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase (GoTrue) adapter for <see cref="IAuthenticationService"/>. It is the
/// only place that knows GoTrue exists, and the only place that turns a GoTrue
/// failure reason into a French message.
/// </summary>
internal sealed class SupabaseAuthenticationService : IAuthenticationService
{
    private const string GenericFailureMessage = "Une erreur est survenue. Réessayez.";
    private const string InvalidLinkMessage = "Lien de réinitialisation invalide ou expiré.";

    private readonly global::Supabase.Client _client;
    private readonly IApplicationUrlProvider _urls;
    private readonly ILogger<SupabaseAuthenticationService> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="urls">Provider of the absolute URLs the recovery flow needs.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseAuthenticationService(
        global::Supabase.Client client,
        IApplicationUrlProvider urls,
        ILogger<SupabaseAuthenticationService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _urls = urls;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSignedIn => _client.Auth.CurrentUser?.Id is not null;

    /// <inheritdoc />
    public string? CurrentEmail => _client.Auth.CurrentUser?.Email;

    /// <inheritdoc />
    public async Task<OperationResult> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return OperationResult.Failure("E-mail ou mot de passe incorrect.");
        }

        try
        {
            var session = await _client.Auth.SignIn(email.Trim(), password).ConfigureAwait(false);

            return session?.User?.Id is not null
                ? OperationResult.Success
                : OperationResult.Failure("Connexion impossible. Réessayez.");
        }
        catch (GotrueException exception)
        {
            return OperationResult.Failure(Translate(exception, nameof(SignInAsync)));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return OperationResult.Failure("E-mail ou mot de passe invalide.");
        }

        try
        {
            var session = await _client.Auth.SignUp(email.Trim(), password).ConfigureAwait(false);

            // With e-mail confirmation disabled on the project, GoTrue signs
            // the caller in as part of SignUp. The screen's contract is that
            // signing up never grants access by itself (the account still
            // needs an administrator to activate it), so any such session is
            // dropped immediately - best effort, the way SignOutAsync already
            // treats a failing remote sign-out as non-fatal. With
            // confirmation enabled, SignUp returns no session and this is a
            // no-op.
            if (session?.AccessToken is not null)
            {
                await SignOutAsync(cancellationToken).ConfigureAwait(false);
            }

            return OperationResult.Success;
        }
        catch (GotrueException exception)
        {
            return OperationResult.Failure(Translate(exception, nameof(SignUpAsync)));
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _client.Auth.SignOut().ConfigureAwait(false);
        }
        catch (GotrueException exception)
        {
            // Best effort: the local session is dropped either way, so a failing
            // sign-out must never block the user on the sign-out page.
            _logger.LogWarning(exception, "Remote sign-out failed; local session dropped anyway.");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> SendPasswordResetEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var options = new ResetPasswordForEmailOptions(email.Trim())
            {
                RedirectTo = _urls.PasswordResetCallbackUrl,
            };

            await _client.Auth.ResetPasswordForEmail(options).ConfigureAwait(false);
            return OperationResult.Success;
        }
        catch (GotrueException exception)
        {
            return OperationResult.Failure(Translate(exception, nameof(SendPasswordResetEmailAsync)));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> EstablishSessionFromCallbackAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var session = await _client.Auth
                .GetSessionFromUrl(new Uri(_urls.CurrentUrl), storeSession: true)
                .ConfigureAwait(false);

            return session?.User?.Id is not null
                ? OperationResult.Success
                : OperationResult.Failure(InvalidLinkMessage);
        }
        catch (Exception exception) when (exception is GotrueException or FormatException or ArgumentException)
        {
            _logger.LogWarning(exception, "Could not establish a session from the recovery URL.");
            return OperationResult.Failure(InvalidLinkMessage);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> UpdatePasswordAsync(
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var user = await _client.Auth
                .Update(new UserAttributes { Password = newPassword })
                .ConfigureAwait(false);

            return user?.Id is not null
                ? OperationResult.Success
                : OperationResult.Failure("Mise à jour du mot de passe impossible.");
        }
        catch (GotrueException exception)
        {
            return OperationResult.Failure(Translate(exception, nameof(UpdatePasswordAsync)));
        }
    }

    /// <summary>
    /// Maps a GoTrue failure reason to a message safe to display. Anything not
    /// recognised falls back to a generic message: an authentication screen must
    /// never echo provider internals back to an anonymous visitor.
    /// </summary>
    private string Translate(GotrueException exception, string operationName)
    {
        _logger.LogWarning(
            exception,
            "GoTrue refused {Operation} ({Reason}, HTTP {StatusCode}).",
            operationName,
            exception.Reason,
            exception.StatusCode);

        return exception.Reason switch
        {
            FailureHint.Reason.UserBadLogin
                or FailureHint.Reason.UserBadPassword
                or FailureHint.Reason.UserBadMultiple => "E-mail ou mot de passe incorrect.",
            FailureHint.Reason.UserBadEmailAddress => "Adresse e-mail invalide.",
            FailureHint.Reason.UserEmailNotConfirmed => "Adresse e-mail non confirmée.",
            FailureHint.Reason.UserMissingInformation => "Mot de passe trop faible (6 caractères minimum).",
            FailureHint.Reason.UserAlreadyRegistered =>
                "Un compte existe déjà pour cette adresse. Connectez-vous ou réinitialisez votre mot de passe.",
            FailureHint.Reason.UserTooManyRequests => "Trop de tentatives. Patientez quelques minutes.",
            FailureHint.Reason.Offline
                or FailureHint.Reason.NetworkError
                or FailureHint.Reason.CloudflareNetworkError => "Problème réseau. Vérifiez votre connexion.",
            FailureHint.Reason.BadSessionUrl
                or FailureHint.Reason.NoSessionFound
                or FailureHint.Reason.ExpiredRefreshToken
                or FailureHint.Reason.InvalidRefreshToken => InvalidLinkMessage,
            _ => GenericFailureMessage,
        };
    }
}
