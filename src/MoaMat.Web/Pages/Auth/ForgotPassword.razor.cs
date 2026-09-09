using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Authentication;
using MoaMat.Web.Pages.Auth.Models;

namespace MoaMat.Web.Pages.Auth;

/// <summary>
/// "Forgot password" screen. The confirmation shown afterwards is deliberately
/// identical whether or not the address exists, so the screen cannot be used to
/// enumerate accounts.
/// </summary>
public partial class ForgotPassword : ComponentBase
{
    private readonly EmailInput _model = new();

    private bool _isBusy;
    private bool _isSent;
    private string? _error;

    [Inject]
    private IAuthenticationService Authentication { get; set; } = default!;

    /// <summary>Sends the recovery e-mail.</summary>
    private async Task SubmitAsync()
    {
        if (_isBusy)
        {
            // Guard against a double submit: the button is disabled while busy,
            // but a fast second Enter can still reach the handler.
            return;
        }

        _isBusy = true;
        _error = null;

        try
        {
            var result = await Authentication.SendPasswordResetEmailAsync(_model.Email);

            if (result.Succeeded)
            {
                _isSent = true;
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
}
