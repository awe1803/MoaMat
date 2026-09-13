using MoaMat.Domain.Common;

namespace MoaMat.Domain.Authentication;

/// <summary>
/// Port covering the sign-in / sign-out and password recovery cycle. The
/// adapter owns the identity provider and is responsible for turning provider
/// errors into messages that are safe to display.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>True when a session is currently established.</summary>
    bool IsSignedIn { get; }

    /// <summary>E-mail of the signed-in user, or <c>null</c> when signed out.</summary>
    string? CurrentEmail { get; }

    /// <summary>Signs a user in with an e-mail and a password.</summary>
    /// <param name="email">Login address; leading and trailing spaces are ignored.</param>
    /// <param name="password">Plain-text password, never logged nor persisted.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new account. This only starts the sign-up - it never signs
    /// the caller in and never grants application access by itself: the
    /// account is created with the database default role <c>en_attente</c>
    /// (see <c>db/roles.sql</c>), which holds no permission at all until an
    /// administrator explicitly activates it and assigns a real role (see
    /// <c>public.set_compte_actif</c> / <c>public.utilisateur_role</c>).
    /// </summary>
    /// <param name="email">Login address; leading and trailing spaces are ignored.</param>
    /// <param name="password">Plain-text password, never logged nor persisted.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs the current user out. Best effort: the local session is dropped
    /// even if the provider cannot be reached, so this never throws.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the password recovery e-mail. The result is deliberately identical
    /// whether or not the address exists, so the screen cannot be used to
    /// enumerate accounts.
    /// </summary>
    /// <param name="email">Address to send the recovery link to.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Establishes a session from the tokens carried by the current recovery
    /// URL fragment.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> EstablishSessionFromCallbackAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a new password to the user of the current session.</summary>
    /// <param name="newPassword">New plain-text password, never logged nor persisted.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);
}
