namespace MoaMat.Domain.Inventory;

/// <summary>
/// Read model of one inventory item, as exposed by the view <c>public.v_item</c>:
/// the item plus its resolved status, its full location path and its club code
/// ambiguity flags.
/// </summary>
/// <remarks>
/// Ambiguous club codes are <b>reported, never renumbered</b>: the club codes
/// inherited from the legacy Access database are the members' own reference and
/// rewriting them would break every physical label.
/// </remarks>
public sealed record InventoryItem
{
    /// <summary>Technical identifier of <c>public.item</c>.</summary>
    public required long Id { get; init; }

    /// <summary>Club code painted on the gear; may be absent.</summary>
    public string? ClubCode { get; init; }

    /// <summary>True when the club code is duplicated or does not follow the naming scheme.</summary>
    public bool HasAmbiguousClubCode { get; init; }

    /// <summary>True when the club code is shared with at least one other item.</summary>
    public bool HasDuplicateClubCode { get; init; }

    /// <summary>True when the club code does not follow the club naming scheme.</summary>
    public bool HasNonStructuringClubCode { get; init; }

    /// <summary>Raw family code; resolve it with <see cref="ItemFamily.FromCode"/>.</summary>
    public required string FamilyCode { get; init; }

    /// <summary>Manufacturer serial number.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Brand.</summary>
    public string? Brand { get; init; }

    /// <summary>Model.</summary>
    public string? Model { get; init; }

    /// <summary>Acquisition date.</summary>
    public DateOnly? AcquiredOn { get; init; }

    /// <summary>Purchase price in euros.</summary>
    public decimal? PriceEur { get; init; }

    /// <summary>Current status code.</summary>
    public required string StatusCode { get; init; }

    /// <summary>Label of the current status, resolved by the view.</summary>
    public string? StatusLabel { get; init; }

    /// <summary>True when the current status is terminal.</summary>
    public bool IsStatusTerminal { get; init; }

    /// <summary>Container the item is stored in.</summary>
    public long? ContainerId { get; init; }

    /// <summary>Full storage path, "Section &gt; Room &gt; Container".</summary>
    public string? LocationPath { get; init; }

    /// <summary>Free-text destination or assignment.</summary>
    public string? Destination { get; init; }

    /// <summary>Free-text remark.</summary>
    public string? Remark { get; init; }

    /// <summary>Next inspection or requalification due date.</summary>
    public DateOnly? DueOn { get; init; }

    /// <summary>False once the item has been logically removed from the inventory.</summary>
    public bool IsActive { get; init; }

    /// <summary>Creation instant, maintained by the database.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update instant, maintained by the database.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Human-readable reason why the club code was flagged, for the badge
    /// tooltip. Returns <c>null</c> when the code is not ambiguous.
    /// </summary>
    public string? DescribeClubCodeAmbiguity()
    {
        if (!HasAmbiguousClubCode)
        {
            return null;
        }

        var reasons = new List<string>(2);

        if (HasDuplicateClubCode)
        {
            reasons.Add("code dupliqué");
        }

        if (HasNonStructuringClubCode)
        {
            reasons.Add("code non structurant");
        }

        return reasons.Count > 0 ? string.Join(", ", reasons) : "code ambigu";
    }
}
