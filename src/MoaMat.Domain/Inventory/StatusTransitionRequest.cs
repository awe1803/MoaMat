namespace MoaMat.Domain.Inventory;

/// <summary>
/// Everything a status change must carry, per the item state machine
/// (<c>db/item_etat.sql</c>): the database rejects a bare status change with no
/// reason attached.
/// </summary>
/// <param name="StatusCode">Target status code.</param>
/// <param name="Reason">Mandatory reason for the change.</param>
/// <param name="EffectiveOn">Mandatory effective date of the change.</param>
/// <param name="AttachmentUrl">
/// Supporting document path (Supabase Storage). Required by the database when
/// <paramref name="StatusCode"/> is <c>perdu</c> or <c>vole</c>.
/// </param>
/// <param name="Authority">
/// Deciding authority. Required by the database for any transition into a
/// terminal status (<c>retire_du_service</c>, <c>perdu</c>, <c>vole</c>).
/// </param>
public sealed record StatusTransitionRequest(
    string StatusCode,
    string Reason,
    DateOnly EffectiveOn,
    string? AttachmentUrl = null,
    TransitionAuthority? Authority = null);

/// <summary>
/// Body deciding a transition into a terminal status. Matches the
/// <c>statut_autorite</c> check constraint in <c>db/model_item.sql</c>.
/// </summary>
public enum TransitionAuthority
{
    /// <summary>Organisme de contrôle (external inspection body).</summary>
    OrganismeControle,

    /// <summary>Conseil d'administration.</summary>
    Ca,

    /// <summary>Gestionnaire matériel (equipment manager).</summary>
    GestionnaireMateriel,
}
