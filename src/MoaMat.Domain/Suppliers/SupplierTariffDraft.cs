using MoaMat.Domain.Common;

namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Validated new price for a supplier's service. A price is never edited: a
/// change is a new draft with a later effective date, so the history stays
/// intact. A service may also have no price at all.
/// </summary>
public readonly record struct SupplierTariffDraft
{
    /// <summary>Highest price accepted, matching the <c>numeric(12,2)</c> column.</summary>
    public const decimal MaxPriceEur = 9_999_999_999m;

    private SupplierTariffDraft(long serviceId, decimal priceEur, DateOnly effectiveOn)
    {
        ServiceId = serviceId;
        PriceEur = priceEur;
        EffectiveOn = effectiveOn;
    }

    /// <summary>Service the price applies to.</summary>
    public long ServiceId { get; }

    /// <summary>Price in euros, rounded to the cent.</summary>
    public decimal PriceEur { get; }

    /// <summary>First day the price applies.</summary>
    public DateOnly EffectiveOn { get; }

    /// <summary>Validates and builds a tariff draft.</summary>
    /// <param name="serviceId">Service the price applies to (required).</param>
    /// <param name="priceEur">Price in euros; zero or more.</param>
    /// <param name="effectiveOn">First day the price applies (required).</param>
    /// <param name="draft">The validated draft when the method returns a success.</param>
    /// <returns>A success, or a failure carrying a message to display as-is.</returns>
    public static OperationResult TryCreate(
        long? serviceId,
        decimal? priceEur,
        DateOnly? effectiveOn,
        out SupplierTariffDraft draft)
    {
        draft = default;

        if (serviceId is not { } id)
        {
            return OperationResult.Failure("Choisir la prestation concernée.");
        }

        if (priceEur is not { } price)
        {
            return OperationResult.Failure("Le prix est obligatoire.");
        }

        if (price < 0 || price > MaxPriceEur)
        {
            return OperationResult.Failure("Le prix doit être compris entre 0 et 9 999 999 999 €.");
        }

        if (effectiveOn is not { } date)
        {
            return OperationResult.Failure("La date d'effet est obligatoire.");
        }

        draft = new SupplierTariffDraft(id, decimal.Round(price, 2, MidpointRounding.AwayFromZero), date);
        return OperationResult.Success;
    }
}
