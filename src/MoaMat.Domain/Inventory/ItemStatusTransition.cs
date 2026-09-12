namespace MoaMat.Domain.Inventory;

/// <summary>
/// One entry of the decision history of an item's status changes, as exposed
/// by <c>public.v_item_transition</c> (<c>db/item_etat.sql</c>). Append-only:
/// the database rejects any update or delete.
/// </summary>
public sealed record ItemStatusTransition
{
    /// <summary>Technical identifier of <c>public.item_transition</c>.</summary>
    public required long Id { get; init; }

    /// <summary>Item this transition applies to.</summary>
    public required long ItemId { get; init; }

    /// <summary>Status code before the transition; <c>null</c> for the item's first recorded status.</summary>
    public string? PreviousStatusCode { get; init; }

    /// <summary>Label of the status before the transition.</summary>
    public string? PreviousStatusLabel { get; init; }

    /// <summary>Status code after the transition.</summary>
    public required string NewStatusCode { get; init; }

    /// <summary>Label of the status after the transition.</summary>
    public string? NewStatusLabel { get; init; }

    /// <summary>Reason recorded for the transition.</summary>
    public required string Reason { get; init; }

    /// <summary>Effective date recorded for the transition.</summary>
    public DateOnly EffectiveOn { get; init; }

    /// <summary>Deciding authority, when the transition led into a terminal status.</summary>
    public string? Authority { get; init; }

    /// <summary>Supporting document path, when the transition was a loss or theft.</summary>
    public string? AttachmentUrl { get; init; }

    /// <summary>Identifier of the account that made the change.</summary>
    public Guid? DecidedById { get; init; }

    /// <summary>E-mail of the account that made the change.</summary>
    public string? DecidedByEmail { get; init; }

    /// <summary>Instant the transition was recorded.</summary>
    public DateTimeOffset RecordedAt { get; init; }
}
