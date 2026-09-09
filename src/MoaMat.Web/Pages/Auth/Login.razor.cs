using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Domain.Authentication;
using MoaMat.Domain.Navigation;
using MoaMat.Web.Pages.Auth.Models;

namespace MoaMat.Web.Pages.Auth;

/// <summary>Sign-in screen.</summary>
public partial class Login : ComponentBase
{
    private readonly CredentialsInput _model = new();

    private bool _isBusy;
    private string? _error;

    /// <summary>
    /// Where to go after a successful sign-in. It comes from the query string,
    /// so it is never used raw: <see cref="RelativeReturnUrl"/> reduces anything
    /// that could leave the application to the home page.
    /// </summary>
    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [Inject]
    private IAuthenticationService Authentication { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // Already signed in (session restored): skip the screen entirely.
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            NavigateToTarget();
        }
    }

    private async Task SubmitAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _error = null;

        try
        {
            var result = await Authentication.SignInAsync(_model.Email, _model.Password);

            if (result.Succeeded)
            {
                NavigateToTarget();
            }
            else
            {
                _error = result.Error;
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void NavigateToTarget() =>
        Navigation.NavigateTo(RelativeReturnUrl.FromCandidate(ReturnUrl).Value, replace: true);
}
