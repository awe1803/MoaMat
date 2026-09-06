using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;

namespace MoaMat.Web.Pages.Auth;

public partial class Login
{
    private readonly Credentials _model = new();
    private bool _busy;
    private string? _error;

    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // Déjà connecté (session restaurée) : on saute l'écran.
        var state = await AuthState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            Navigation.NavigateTo(SafeReturnUrl(), replace: true);
        }
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        _error = null;

        var result = await Auth.SignInAsync(_model.Email, _model.Password);

        _busy = false;

        if (result.Succeeded)
        {
            Navigation.NavigateTo(SafeReturnUrl(), replace: true);
        }
        else
        {
            _error = result.Error;
        }
    }

    private string SafeReturnUrl()
        => string.IsNullOrWhiteSpace(ReturnUrl) || ReturnUrl.Contains("://") || ReturnUrl.StartsWith("//")
            ? ""
            : ReturnUrl;

    private sealed class Credentials
    {
        [Required(ErrorMessage = "E-mail requis.")]
        [EmailAddress(ErrorMessage = "E-mail invalide.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Mot de passe requis.")]
        public string Password { get; set; } = "";
    }
}
