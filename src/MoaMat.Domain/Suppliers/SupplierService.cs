namespace MoaMat.Domain.Suppliers;

/// <summary>
/// A service offered by a supplier, mirroring a row of
/// <c>public.fournisseur_prestation</c> with its dated tariffs
/// (<c>public.fournisseur_prestation_tarif</c>).
/// </summary>
/// <remarks>
/// Either a bottle control service (<see cref="Control"/> set — usable in a
/// campaign) or a free service (<see cref="Control"/> <c>null</c>). The tariff
/// is optional: a service with no <see cref="Tariffs"/> simply has no price.
/// </remarks>
public sealed record SupplierService
{
    /// <summary>Technical identifier of <c>public.fournisseur_prestation</c>.</summary>
    public required long Id { get; init; }

    /// <summary>Supplier offering this service.</summary>
    public required long SupplierId { get; init; }

    /// <summary>Label shown to the user; unique per supplier.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Bottle counter this service feeds; <c>null</c> for a free service,
    /// which cannot be used in a campaign.
    /// </summary>
    public CylinderControl? Control { get; init; }

    /// <summary>
    /// Oil or water bath — set if and only if <see cref="Control"/> is
    /// <see cref="CylinderControl.Hydraulic"/> (database check constraint).
    /// </summary>
    public HydraulicMethod? Method { get; init; }

    /// <summary>False once deactivated: kept for history, no longer offered in new campaigns.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Free-text remark.</summary>
    public string? Comment { get; init; }

    /// <summary>Dated tariffs, oldest first; empty for a service without tariff.</summary>
    public IReadOnlyList<ServicePriceRule> Tariffs { get; init; } = [];

    /// <summary>True for a service that can be picked on a campaign line.</summary>
    public bool IsCylinderService => Control is not null;

    /// <summary>Price in force at <paramref name="asOfDate"/>, or <c>null</c> when there is none.</summary>
    /// <param name="asOfDate">Date the price should be resolved for.</param>
    public decimal? PriceAt(DateOnly asOfDate) => ServicePricingEngine.ResolvePrice(Id, Tariffs.ToArray(), asOfDate);

    /// <summary>Short label of the control and, for hydraulic, the bath — e.g. "Hydraulique · huile".</summary>
    public string ControlLabel => ControlLabelFor(Control, Method);

    /// <summary>Short label of a control and, for hydraulic, the bath.</summary>
    /// <param name="control">Bottle counter fed, or <c>null</c> for a free service.</param>
    /// <param name="method">Bath, meaningful for hydraulic only.</param>
    public static string ControlLabelFor(CylinderControl? control, HydraulicMethod? method) => control switch
    {
        null => "Prestation libre",
        CylinderControl.Optical => "Optique",
        CylinderControl.Hydraulic => method switch
        {
            HydraulicMethod.Oil => "Hydraulique · huile",
            HydraulicMethod.Water => "Hydraulique · eau",
            _ => "Hydraulique",
        },
        _ => string.Empty,
    };
}
