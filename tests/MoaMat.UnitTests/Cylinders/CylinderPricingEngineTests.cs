using MoaMat.Domain.Cylinders;

namespace MoaMat.UnitTests.Cylinders;

/// <summary>
/// <see cref="CylinderPricingEngine"/> is the pure C# mirror of
/// <c>public.bouteille_tarif_apragaz()</c> (<c>db/item_bouteille.sql</c>).
/// Values below are the seeded Apragaz 2023-2026 tariffs.
/// </summary>
public sealed class CylinderPricingEngineTests
{
    private static readonly IReadOnlyCollection<CylinderPricingRule> Rules =
    [
        new(CylinderServiceType.Rr, 19.90m, new DateOnly(2023, 1, 1)),
        new(CylinderServiceType.HydraulicWater, 45.92m, new DateOnly(2023, 1, 1)),
        new(CylinderServiceType.HydraulicOil, 32.61m, new DateOnly(2023, 1, 1)),
        new(CylinderServiceType.Rr, 20.50m, new DateOnly(2024, 1, 1)),
        new(CylinderServiceType.HydraulicWater, 47.30m, new DateOnly(2024, 1, 1)),
        new(CylinderServiceType.HydraulicOil, 33.59m, new DateOnly(2024, 1, 1)),
    ];

    [Fact]
    public void Resolves_the_price_in_force_at_the_given_date()
    {
        var price = CylinderPricingEngine.ResolvePrice(CylinderServiceType.Rr, Rules, new DateOnly(2023, 6, 1));

        Assert.Equal(19.90m, price);
    }

    [Fact]
    public void A_later_price_never_applies_retroactively()
    {
        var price = CylinderPricingEngine.ResolvePrice(
            CylinderServiceType.HydraulicWater, Rules, new DateOnly(2023, 12, 31));

        Assert.Equal(45.92m, price);
    }

    [Fact]
    public void The_new_price_applies_from_its_effective_date()
    {
        var price = CylinderPricingEngine.ResolvePrice(
            CylinderServiceType.HydraulicOil, Rules, new DateOnly(2024, 1, 1));

        Assert.Equal(33.59m, price);
    }

    [Fact]
    public void No_rule_in_force_yet_resolves_to_null_not_a_default()
    {
        var price = CylinderPricingEngine.ResolvePrice(CylinderServiceType.Rr, Rules, new DateOnly(2020, 1, 1));

        Assert.Null(price);
    }

    [Fact]
    public void Hydraulic_oil_and_water_never_share_a_price()
    {
        var asOfDate = new DateOnly(2023, 6, 1);

        var oilPrice = CylinderPricingEngine.ResolvePrice(CylinderServiceType.HydraulicOil, Rules, asOfDate);
        var waterPrice = CylinderPricingEngine.ResolvePrice(CylinderServiceType.HydraulicWater, Rules, asOfDate);

        Assert.NotEqual(oilPrice, waterPrice);
    }

    [Fact]
    public void A_tie_on_EffectiveOn_resolves_deterministically_by_input_order()
    {
        // public.ref_tarif_apragaz enforces unique (type_prestation, date_effet),
        // so this precondition violation never occurs against the real
        // referential — this only documents that ResolvePrice does not throw
        // or pick randomly if a caller ever hands it malformed input (see the
        // remark on CylinderPricingRule.EffectiveOn).
        var tiedRules = new[]
        {
            new CylinderPricingRule(CylinderServiceType.Rr, 10m, new DateOnly(2025, 1, 1)),
            new CylinderPricingRule(CylinderServiceType.Rr, 20m, new DateOnly(2025, 1, 1)),
        };

        var price = CylinderPricingEngine.ResolvePrice(CylinderServiceType.Rr, tiedRules, new DateOnly(2025, 6, 1));

        Assert.Equal(10m, price);
    }
}
