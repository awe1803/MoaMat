using System.ComponentModel.DataAnnotations;

namespace MoaMat.Web.Pages.Auth.Models;

/// <summary>Form model of the password reset screen.</summary>
public sealed class NewPasswordInput
{
    /// <summary>New password. The minimum length is also enforced by the provider.</summary>
    [Required(ErrorMessage = "Mot de passe requis.")]
    [MinLength(8, ErrorMessage = "8 caractères minimum.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Confirmation, which must match <see cref="Password"/>.</summary>
    [Compare(nameof(Password), ErrorMessage = "Les mots de passe ne correspondent pas.")]
    public string Confirm { get; set; } = string.Empty;
}
