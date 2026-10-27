using MoaMat.Domain.Cylinders;

namespace MoaMat.Domain.Suppliers;

/// <summary>
/// One price of the history of a supplier's service: the price applies from
/// <see cref="EffectiveOn"/> until a later row supersedes it.
/// </summary>
/// <param name="Id">Database identifier.</param>
/// <param name="SupplierId">Owning supplier.</param>
/// <param name="Service">Service the price applies to.</param>
/// <param name="PriceEur">Price in euros.</param>
/// <param name="EffectiveOn">First day the price applies.</param>
public sealed record SupplierTariff(
    long Id,
    long SupplierId,
    CylinderServiceType Service,
    decimal PriceEur,
    DateOnly EffectiveOn);
