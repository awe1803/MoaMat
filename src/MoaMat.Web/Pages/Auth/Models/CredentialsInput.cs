using System.ComponentModel.DataAnnotations;

namespace MoaMat.Web.Pages.Auth.Models;

/// <summary>Form model of the sign-in screen.</summary>
public sealed class CredentialsInput
{
    /// <summary>Login address.</summary>
    [Required(ErrorMessage = "E-mail requis.")]
    [EmailAddress(ErrorMessage = "E-mail invalide.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Plain-text password; bound only for the duration of the request.</summary>
    [Required(ErrorMessage = "Mot de passe requis.")]
    public string Password { get; set; } = string.Empty;
}
