using System.ComponentModel.DataAnnotations;

namespace MoaMat.Web.Pages.Auth.Models;

/// <summary>Form model of the sign-up screen.</summary>
public sealed class RegisterInput
{
    /// <summary>Login address.</summary>
    [Required(ErrorMessage = "E-mail requis.")]
    [EmailAddress(ErrorMessage = "E-mail invalide.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>New password. The minimum length is also enforced by the provider.</summary>
    [Required(ErrorMessage = "Mot de passe requis.")]
    [MinLength(8, ErrorMessage = "8 caractères minimum.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Confirmation, which must match <see cref="Password"/>.</summary>
    [Compare(nameof(Password), ErrorMessage = "Les mots de passe ne correspondent pas.")]
    public string Confirm { get; set; } = string.Empty;
}
