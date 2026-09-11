using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Authentication;
using MoaMat.Web.Pages.Auth.Models;

namespace MoaMat.Web.Pages.Auth;

/// <summary>
/// Password reset screen, reached from the recovery e-mail.
/// </summary>
/// <remarks>
/// The link carries the tokens in the URL fragment
/// (<c>#access_token=...&amp;type=recovery</c>). The session has to be
/// established from them before a new password can be set, which is why the
/// screen starts in <see cref="ResetPasswordStage.Checking"/>.
/// </remarks>
public partial class ResetPassword : ComponentBase
{
    private readonly NewPasswordInput _model = new();

    private ResetPasswordStage _stage = ResetPasswordStage.Checking;
    private bool _isBusy;
    private string? _error;

    [Inject]
    private IAuthenticationService Authentication { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Authentication.IsSignedIn)
        {
            _stage = ResetPasswordStage.Form;
            return;
        }

        var result = await Authentication.EstablishSessionFromCallbackAsync();
        _stage = result.Succeeded ? ResetPasswordStage.Form : ResetPasswordStage.LinkInvalid;
        _error = result.Succeeded ? null : result.Error;
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
            var result = await Authentication.UpdatePasswordAsync(_model.Password);

            if (result.Succeeded)
            {
                _stage = ResetPasswordStage.Done;
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

    private void GoToApplication() => Navigation.NavigateTo(string.Empty, replace: true);
}
