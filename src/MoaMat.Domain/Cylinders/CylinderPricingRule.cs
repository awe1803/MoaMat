namespace MoaMat.Domain.Cylinders;

/// <summary>
/// One dated entry of the Apragaz pricing referential, mirroring a row of
/// <c>public.ref_tarif_apragaz</c> (<c>db/item_bouteille.sql</c>).
/// </summary>
/// <param name="ServiceType">Which service this price applies to.</param>
/// <param name="PriceEur">Price in force from <see cref="EffectiveOn"/>.</param>
/// <param name="EffectiveOn">
/// Date from which this value is in force. The referential is historized
/// (append-only, admin-editable): several rows can share the same
/// <see cref="ServiceType"/> at different <see cref="EffectiveOn"/> dates —
/// <see cref="CylinderPricingEngine"/> always picks the one in force at the
/// date the price is resolved for.
/// <para>
/// A given <see cref="ServiceType"/> is never expected to have two rules
/// sharing the same <see cref="EffectiveOn"/>: <c>public.ref_tarif_apragaz</c>
/// enforces this with <c>unique (type_prestation, date_effet)</c>. If a
/// caller ever builds a <see cref="CylinderPricingRule"/> collection outside
/// that constraint (e.g. a hand-written test) and violates it,
/// <see cref="CylinderPricingEngine.ResolvePrice"/> breaks the tie using a
/// stable sort over the input order — deterministic given the same input,
/// but not a resolution rule to depend on.
/// </para>
/// </param>
public sealed record CylinderPricingRule(
    CylinderServiceType ServiceType,
    decimal PriceEur,
    DateOnly EffectiveOn);
