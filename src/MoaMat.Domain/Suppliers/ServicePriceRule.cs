namespace MoaMat.Domain.Suppliers;

/// <summary>
/// One dated tariff of a supplier service, mirroring a row of
/// <c>public.fournisseur_prestation_tarif</c>.
/// </summary>
/// <param name="ServiceId">Service this price applies to.</param>
/// <param name="PriceEur">Price in force from <see cref="EffectiveOn"/>.</param>
/// <param name="EffectiveOn">
/// Date from which this value is in force. Tariffs are historized
/// (append-only): several rows can share a <see cref="ServiceId"/> at
/// different dates — <see cref="ServicePricingEngine"/> always picks the one in
/// force at the date the price is resolved for.
/// <para>
/// A service never has two rules on the same date
/// (<c>unique (prestation_id, date_effet)</c>). If a caller builds a collection
/// outside that constraint, <see cref="ServicePricingEngine.ResolvePrice"/>
/// breaks the tie by stable input order — deterministic, but not a rule to
/// depend on.
/// </para>
/// </param>
/// <param name="Id">Database identifier of the tariff row (0 when not persisted), used to delete a mistaken entry.</param>
public sealed record ServicePriceRule(long ServiceId, decimal PriceEur, DateOnly EffectiveOn, long Id = 0);
