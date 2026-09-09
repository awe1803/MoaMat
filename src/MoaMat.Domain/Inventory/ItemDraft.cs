namespace MoaMat.Domain.Inventory;

/// <summary>
/// Write model for <c>public.item</c>. Database-owned columns are deliberately
/// absent: the generated identifier, the ambiguity flags recomputed by trigger,
/// the audit timestamps and the migration provenance columns. A client can
/// therefore not overwrite them, even by mistake.
/// </summary>
public sealed record ItemDraft
{
    /// <summary>
    /// Identifier of the item being updated. Left at <c>0</c> for a creation:
    /// the database generates the value (<c>GENERATED ALWAYS</c>).
    /// </summary>
    public long Id { get; init; }

    /// <summary>Club code painted on the gear; may be absent.</summary>
    public string? ClubCode { get; init; }

    /// <summary>Family discriminator, see <see cref="ItemFamily.Code"/>.</summary>
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

    /// <summary>Status code; defaults to the in-service status.</summary>
    public string StatusCode { get; init; } = "en_service";

    /// <summary>Container the item is stored in.</summary>
    public long? ContainerId { get; init; }

    /// <summary>Free-text destination or assignment.</summary>
    public string? Destination { get; init; }

    /// <summary>Free-text remark.</summary>
    public string? Remark { get; init; }

    /// <summary>Next inspection or requalification due date.</summary>
    public DateOnly? DueOn { get; init; }

    /// <summary>False to keep the item out of the active inventory.</summary>
    public bool IsActive { get; init; } = true;
}
