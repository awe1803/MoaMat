using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Navigation;

namespace MoaMat.Web.Navigation;

/// <summary>
/// Resolves the absolute URLs the authentication flow needs from the Blazor
/// <see cref="NavigationManager"/>, so the deployment sub-path (GitHub Pages
/// project site) is taken into account without any adapter knowing about it.
/// </summary>
internal sealed class BlazorApplicationUrlProvider : IApplicationUrlProvider
{
    /// <summary>Route the password recovery link must come back to.</summary>
    private const string PasswordResetRoute = "reinitialiser-mot-de-passe";

    private readonly NavigationManager _navigation;

    /// <summary>Creates the provider.</summary>
    /// <param name="navigation">Blazor navigation manager.</param>
    public BlazorApplicationUrlProvider(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
    }

    /// <inheritdoc />
    public string CurrentUrl => _navigation.Uri;

    /// <inheritdoc />
    public string PasswordResetCallbackUrl => _navigation.ToAbsoluteUri(PasswordResetRoute).ToString();
}
