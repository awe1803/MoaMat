using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Authentication;
using MoaMat.Web.Pages.Auth.Models;

namespace MoaMat.Web.Pages.Auth;

/// <summary>
/// Sign-up screen. Creating an account here never grants application access by
/// itself: the new account lands in the database default role <c>en_attente</c>
/// (no permission at all) and stays there until an administrator explicitly
/// activates it - see <c>db/roles.sql</c> and the <c>/comptes</c> screen.
/// </summary>
public partial class Register : ComponentBase
{
    private readonly RegisterInput _model = new();

    private bool _isBusy;
    private bool _isDone;
    private string? _error;

    [Inject]
    private IAuthenticationService Authentication { get; set; } = default!;

    private async Task SubmitAsync()
    {
        if (_isBusy)
        {
            // Guard against a double submit: the button is disabled while
            // busy, but a fast second Enter can still reach the handler.
            return;
        }

        _isBusy = true;
        _error = null;

        try
        {
            var result = await Authentication.SignUpAsync(_model.Email, _model.Password);

            if (result.Succeeded)
            {
                _isDone = true;
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
