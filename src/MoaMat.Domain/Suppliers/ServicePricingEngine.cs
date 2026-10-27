namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Pure resolution of the price in force for a supplier service at a given
/// date, mirroring <c>public.prestation_tarif()</c>
/// (<c>db/fournisseur_prestation.sql</c>).
/// </summary>
/// <remarks>
/// No I/O — the caller supplies the tariffs (normally
/// <see cref="SupplierService.Tariffs"/>) so the engine stays testable without a
/// database.
/// <para>
/// <b>Accepted duplication, not an oversight</b>: the resolution logic is
/// deliberately re-implemented rather than shared with
/// <c>public.prestation_tarif()</c>. Only the estimate shown while picking a
/// service in "Préparer une campagne" goes through this class — the persisted
/// estimate (<c>campagne_ligne.cout_estime_eur</c>) and every actual cost are
/// always computed or entered server-side, never trusted from the client. The
/// risk is purely a display one; keep the "latest row on/before the date" rule
/// in sync with the SQL function if it ever changes.
/// </para>
/// </remarks>
public static class ServicePricingEngine
{
    /// <summary>
    /// Resolves the price in force for <paramref name="serviceId"/> at
    /// <paramref name="asOfDate"/>, or <c>null</c> when no rule is in force yet
    /// at that date (a service without tariff has no price — never a default).
    /// </summary>
    /// <param name="serviceId">Service to price.</param>
    /// <param name="rules">The tariffs, possibly of several services and dates.</param>
    /// <param name="asOfDate">Date the price should be resolved for.</param>
    public static decimal? ResolvePrice(
        long serviceId,
        IReadOnlyCollection<ServicePriceRule> rules,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var rule = rules
            .Where(r => r.ServiceId == serviceId && r.EffectiveOn <= asOfDate)
            .OrderByDescending(r => r.EffectiveOn)
            .FirstOrDefault();

        return rule?.PriceEur;
    }
}
