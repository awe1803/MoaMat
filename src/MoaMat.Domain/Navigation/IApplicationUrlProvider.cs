namespace MoaMat.Domain.Navigation;

/// <summary>
/// Port exposing the few absolute URLs the authentication flow needs. It keeps
/// the hosting model (Blazor navigation, base href, deployment sub-path) out of
/// the adapters that talk to the identity provider.
/// </summary>
public interface IApplicationUrlProvider
{
    /// <summary>Absolute URL of the page currently displayed, fragment included.</summary>
    string CurrentUrl { get; }

    /// <summary>
    /// Absolute URL the identity provider must send the user back to after a
    /// password recovery link is clicked. It has to be registered in the
    /// provider's redirect allow-list.
    /// </summary>
    string PasswordResetCallbackUrl { get; }
}
