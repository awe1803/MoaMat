using System.ComponentModel.DataAnnotations;

namespace MoaMat.Web.Pages.Auth.Models;

/// <summary>Form model of the "forgot password" screen.</summary>
public sealed class EmailInput
{
    /// <summary>Address the recovery link is sent to.</summary>
    [Required(ErrorMessage = "E-mail requis.")]
    [EmailAddress(ErrorMessage = "E-mail invalide.")]
    public string Email { get; set; } = string.Empty;
}
