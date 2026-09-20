namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Characteristics specific to a cylinder (<c>public.item_bouteille</c>), the
/// part of the fiche that the generic <see cref="Inventory.InventoryItem"/> does
/// not carry.
/// </summary>
public sealed record CylinderDetails
{
    /// <summary>Technical identifier of the parent item.</summary>
    public required long ItemId { get; init; }

    /// <summary>Number painted on the cylinder — NOT the internal id (see <c>db/MODELE.md</c> §12).</summary>
    public string? PaintedNumber { get; init; }

    /// <summary>Nominal volume, litres.</summary>
    public decimal? VolumeLitres { get; init; }

    /// <summary>Service pressure, bar.</summary>
    public int? ServicePressureBar { get; init; }

    /// <summary>Empty weight, kg.</summary>
    public decimal? TareKg { get; init; }

    /// <summary>Raw usage code, see <see cref="CylinderUsage.FromCode"/>.</summary>
    public string? UsageCode { get; init; }

    /// <summary>Raw material code, see <see cref="CylinderMaterial.FromCode"/>.</summary>
    public string? MaterialCode { get; init; }

    /// <summary>Valve thread type (filetage), as recorded.</summary>
    public string? Thread { get; init; }

    /// <summary>Whether the cylinder has a double outlet; <c>null</c> when unknown.</summary>
    public bool? HasDoubleOutlet { get; init; }

    /// <summary>Identifier this cylinder had in the legacy Access database, when it came from it.</summary>
    public long? AccessId { get; init; }

    /// <summary>Last optical control, if any.</summary>
    public DateOnly? LastOpticalControlOn { get; init; }

    /// <summary>Last hydraulic control, if any.</summary>
    public DateOnly? LastHydraulicControlOn { get; init; }

    /// <summary>Label of the usage ("Plongée", "Déco O₂"…), or the raw code when unknown.</summary>
    public string? UsageLabel => CylinderUsage.FromCode(UsageCode)?.DisplayName ?? UsageCode;

    /// <summary>Label of the material, or the raw code when unknown.</summary>
    public string? MaterialLabel => CylinderMaterial.FromCode(MaterialCode)?.DisplayName ?? MaterialCode;
}
