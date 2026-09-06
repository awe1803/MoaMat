using System.ComponentModel.DataAnnotations;

namespace MoaMat.Web.Pages.Auth;

public partial class ResetPassword
{
    private enum Stage { Checking, LinkInvalid, Form, Done }

    private readonly NewPassword _model = new();
    private Stage _stage = Stage.Checking;
    private bool _busy;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        // Le lien de l'e-mail renvoie ici avec les jetons dans le fragment
        // (#access_token=…&type=recovery). On établit la session avant de
        // permettre la saisie du nouveau mot de passe.
        if (Auth.IsSignedIn)
        {
            _stage = Stage.Form;
            return;
        }

        var result = await Auth.EstablishSessionFromUrlAsync();
        _stage = result.Succeeded ? Stage.Form : Stage.LinkInvalid;
        _error = result.Succeeded ? null : result.Error;
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        _error = null;

        var result = await Auth.UpdatePasswordAsync(_model.Password);

        _busy = false;

        if (result.Succeeded)
        {
            _stage = Stage.Done;
        }
        else
        {
            _error = result.Error;
        }
    }

    private void GoToApp() => Navigation.NavigateTo("", replace: true);

    private sealed class NewPassword
    {
        [Required(ErrorMessage = "Mot de passe requis.")]
        [MinLength(8, ErrorMessage = "8 caractères minimum.")]
        public string Password { get; set; } = "";

        [Compare(nameof(Password), ErrorMessage = "Les mots de passe ne correspondent pas.")]
        public string Confirm { get; set; } = "";
    }
}
