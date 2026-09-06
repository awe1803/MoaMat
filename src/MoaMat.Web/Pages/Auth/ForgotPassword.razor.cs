using System.ComponentModel.DataAnnotations;

namespace MoaMat.Web.Pages.Auth;

public partial class ForgotPassword
{
    private readonly EmailOnly _model = new();
    private bool _busy;
    private bool _sent;
    private string? _error;

    private async Task SubmitAsync()
    {
        _busy = true;
        _error = null;

        var result = await Auth.SendPasswordResetEmailAsync(_model.Email);

        _busy = false;

        if (result.Succeeded)
        {
            _sent = true;
        }
        else
        {
            _error = result.Error;
        }
    }

    private sealed class EmailOnly
    {
        [Required(ErrorMessage = "E-mail requis.")]
        [EmailAddress(ErrorMessage = "E-mail invalide.")]
        public string Email { get; set; } = "";
    }
}
