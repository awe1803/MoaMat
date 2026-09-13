using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Authentication;
using MoaMat.Web.Notifications;

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

    [Inject]
    private PushNotificationService Push { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // Before signing out: removing the stored subscription needs the session.
        await Push.ForgetThisDeviceAsync();
        await Authentication.SignOutAsync();
        Navigation.NavigateTo(SignInRoute, replace: true);
    }
}
