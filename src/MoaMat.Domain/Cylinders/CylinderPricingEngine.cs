namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Pure resolution of the Apragaz price in force for a service at a given
/// date, mirroring <c>public.bouteille_tarif_apragaz()</c>
/// (<c>db/item_bouteille.sql</c>).
/// </summary>
/// <remarks>
/// No I/O — the caller supplies the pricing referential (normally read from
/// <c>public.ref_tarif_apragaz</c>) so the engine stays testable without a
/// database, the same way <see cref="CylinderDueDateEngine"/> does for due
/// dates.
/// <para>
/// <b>Accepted duplication, not an oversight</b>: the resolution logic here is
/// deliberately re-implemented rather than shared with
/// <c>public.bouteille_tarif_apragaz()</c> (same precedent as
/// <see cref="CylinderDueDateEngine"/> mirroring <c>public.bouteille_echeance()</c>).
/// Only the estimate shown to the manager while picking a service in
/// "Préparer une campagne" goes through this class — the persisted estimate
/// (<c>campagne_ligne.cout_estime_eur</c>) and every actual cost are always
/// computed or entered server-side, never trusted from the client. The risk
/// this duplication carries is purely a <b>display</b> one: if
/// <c>bouteille_tarif_apragaz()</c>'s "latest row on/before the date" rule
/// ever changes without updating <see cref="ResolvePrice"/> to match, the
/// live estimate shown here could disagree with the one the database computes
/// and persists a moment later — never a financial-integrity issue, since
/// nothing this method returns is ever written back as-is. Keep the two in
/// sync deliberately if the SQL side changes.
/// </para>
/// </remarks>
public static class CylinderPricingEngine
{
    /// <summary>
    /// Resolves the price in force for <paramref name="serviceType"/> at
    /// <paramref name="asOfDate"/>, or <c>null</c> when no rule is in force
    /// yet at that date.
    /// </summary>
    /// <param name="serviceType">Service to price.</param>
    /// <param name="rules">
    /// The pricing referential, historized: several rules may share a service
    /// type at different <see cref="CylinderPricingRule.EffectiveOn"/> dates.
    /// The rule applied is the one in force at <paramref name="asOfDate"/>.
    /// </param>
    /// <param name="asOfDate">Date the price should be resolved for.</param>
    public static decimal? ResolvePrice(
        CylinderServiceType serviceType,
        IReadOnlyCollection<CylinderPricingRule> rules,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(rules);

        var rule = rules
            .Where(r => r.ServiceType == serviceType && r.EffectiveOn <= asOfDate)
            .OrderByDescending(r => r.EffectiveOn)
            .FirstOrDefault();

        return rule?.PriceEur;
    }
}
