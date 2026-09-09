using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Navigation;

namespace MoaMat.Web;

/// <summary>
/// Sends an unauthenticated visitor to the sign-in screen, remembering where
/// they were heading.
/// </summary>
/// <remarks>
/// The remembered path is validated as a <see cref="RelativeReturnUrl"/> before
/// it is handed back as a query parameter, so a crafted deep link cannot turn
/// the sign-in redirect into an open redirect.
/// </remarks>
public partial class RedirectToLogin : ComponentBase
{
    private const string SignInRoute = "connexion";

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        var current = RelativeReturnUrl.FromCandidate(Navigation.ToBaseRelativePath(Navigation.Uri));

        var target = current == RelativeReturnUrl.Home
                     || current.Value.StartsWith(SignInRoute, StringComparison.OrdinalIgnoreCase)
            ? SignInRoute
            : $"{SignInRoute}?returnUrl={Uri.EscapeDataString(current.Value)}";

        Navigation.NavigateTo(target, forceLoad: false, replace: true);
    }
}
