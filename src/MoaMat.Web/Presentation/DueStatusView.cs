using MoaMat.Domain.Inventory;

namespace MoaMat.Web.Presentation;

/// <summary>
/// French labels and badge classes for <see cref="DueStatus"/>.
/// </summary>
/// <remarks>
/// The vocabulary — "hors validité", "échéance proche", "valide" — is the one
/// the mock-up put in front of the club, and the badge classes are the ones the
/// design system defines. Both live here so a screen never invents its own.
/// </remarks>
public static class DueStatusView
{
    /// <summary>Badge wording shown to the user.</summary>
    /// <param name="status">Status to describe.</param>
    public static string Label(DueStatus status) => status switch
    {
        DueStatus.Overdue => "Hors validité",
        DueStatus.DueSoon => "Échéance proche",
        DueStatus.Valid => "Valide",
        _ => "Sans échéance",
    };

    /// <summary>Modifier appended to the <c>badge</c> class.</summary>
    /// <param name="status">Status to style.</param>
    public static string BadgeClass(DueStatus status) => status switch
    {
        DueStatus.Overdue => "badge expire",
        DueStatus.DueSoon => "badge proche",
        DueStatus.Valid => "badge valide",
        _ => "badge neutre",
    };
}
