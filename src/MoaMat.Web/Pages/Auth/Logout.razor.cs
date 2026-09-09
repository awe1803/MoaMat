using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Authentication;

namespace MoaMat.Web.Pages.Auth;

/// <summary>
/// Sign-out screen. Sign-out is best effort by contract, so the user always
/// lands on the sign-in page even when the provider cannot be reached.
/// </summary>
public partial class Logout : ComponentBase
{
    /// <summary>Route the user is sent to once signed out.</summary>
    private const string SignInRoute = "connexion";

    [Inject]
    private IAuthenticationService Authentication { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await Authentication.SignOutAsync();
        Navigation.NavigateTo(SignInRoute, replace: true);
    }
}
