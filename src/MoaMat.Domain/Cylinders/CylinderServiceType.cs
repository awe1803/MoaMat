namespace MoaMat.Domain.Cylinders;

/// <summary>
/// A billable Apragaz service. <see cref="Code"/> is the discriminator stored
/// in <c>public.ref_tarif_apragaz.type_prestation</c> and
/// <c>public.campagne_ligne.type_prestation</c>; it stays in French to match
/// the schema exactly.
/// </summary>
/// <remarks>
/// <see cref="HydraulicOil"/> and <see cref="HydraulicWater"/> are kept as two
/// distinct services on purpose: the price gap between them (roughly 13 to
/// 15 € depending on the year, see the seed values in
/// <c>db/item_bouteille.sql</c>) is a real fact of the Apragaz referential,
/// never a data entry mistake to merge away. Neither is ever picked
/// automatically for a hydraulic control that is due — see the ticket's
/// acceptance decision: the manager must choose one explicitly, per bottle,
/// before a campaign can be sent.
/// </remarks>
public sealed record CylinderServiceType
{
    private CylinderServiceType(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    /// <summary>Visual requalification (<c>rr</c>) — the service for a due optical control.</summary>
    public static CylinderServiceType Rr { get; } = new("rr", "RR (contrôle optique)");

    /// <summary>Hydraulic requalification, oil bath method (<c>hydraulique_huile</c>).</summary>
    public static CylinderServiceType HydraulicOil { get; } = new("hydraulique_huile", "Hydraulique (huile)");

    /// <summary>Hydraulic requalification, water bath method (<c>hydraulique_eau</c>).</summary>
    public static CylinderServiceType HydraulicWater { get; } = new("hydraulique_eau", "Hydraulique (eau)");

    /// <summary>Every known service, in display order.</summary>
    public static IReadOnlyList<CylinderServiceType> All { get; } = [Rr, HydraulicOil, HydraulicWater];

    /// <summary>Discriminator stored in <c>type_prestation</c> columns.</summary>
    public string Code { get; }

    /// <summary>Label shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Resolves a service from its code, or <c>null</c> when the code is
    /// unknown or absent.
    /// </summary>
    /// <param name="code">Raw service code read from the database.</param>
    public static CylinderServiceType? FromCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(service => string.Equals(service.Code, code, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => Code;
}
