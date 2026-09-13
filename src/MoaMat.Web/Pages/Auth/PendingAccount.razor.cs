using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Authentication;

namespace MoaMat.Web.Pages.Auth;

/// <summary>Blocking screen shown instead of the application while an account is pending.</summary>
public partial class PendingAccount : ComponentBase
{
    private const string SignInRoute = "connexion";

    private bool _isSigningOut;

    [Inject]
    private IAuthenticationService Authentication { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SignOutAsync()
    {
        if (_isSigningOut)
        {
            return;
        }

        _isSigningOut = true;

        try
        {
            await Authentication.SignOutAsync();
            Navigation.NavigateTo(SignInRoute, replace: true);
        }
        finally
        {
            _isSigningOut = false;
        }
    }
}
