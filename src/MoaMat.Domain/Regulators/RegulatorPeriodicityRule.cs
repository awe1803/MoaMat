namespace MoaMat.Domain.Regulators;

/// <summary>
/// One historized periodicity rule of <c>public.ref_periodicite_detendeur</c>
/// (<c>db/item_detendeur.sql</c>): the maintenance interval (months) for one
/// (brand, model) profile, in force from <see cref="EffectiveOn"/>.
/// </summary>
/// <param name="Brand">Brand, compared case/space-insensitively (free text from the legacy mirror, not a closed enum).</param>
/// <param name="Model">Model, compared the same way.</param>
/// <param name="PeriodicityInMonths">Maintenance interval, in months.</param>
/// <param name="EffectiveOn">Day this rule takes effect; several rules may share a (brand, model) at different dates.</param>
public sealed record RegulatorPeriodicityRule(string Brand, string Model, int PeriodicityInMonths, DateOnly EffectiveOn);
