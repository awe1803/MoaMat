using MoaMat.Domain.Suppliers;

namespace MoaMat.UnitTests.Suppliers;

/// <summary>
/// <see cref="ServicePricingEngine"/> is the pure C# mirror of
/// <c>public.prestation_tarif()</c> (<c>db/fournisseur_prestation.sql</c>).
/// Values below are the seeded Apragaz 2023-2024 tariffs; ids stand for the
/// three migrated services.
/// </summary>
public sealed class ServicePricingEngineTests
{
    private const long Rr = 1;
    private const long HydraulicOil = 2;
    private const long HydraulicWater = 3;

    private static readonly IReadOnlyCollection<ServicePriceRule> Rules =
    [
        new(Rr, 19.90m, new DateOnly(2023, 1, 1)),
        new(HydraulicWater, 45.92m, new DateOnly(2023, 1, 1)),
        new(HydraulicOil, 32.61m, new DateOnly(2023, 1, 1)),
        new(Rr, 20.50m, new DateOnly(2024, 1, 1)),
        new(HydraulicWater, 47.30m, new DateOnly(2024, 1, 1)),
        new(HydraulicOil, 33.59m, new DateOnly(2024, 1, 1)),
    ];

    [Fact]
    public void Resolves_the_price_in_force_at_the_given_date()
    {
        var price = ServicePricingEngine.ResolvePrice(Rr, Rules, new DateOnly(2023, 6, 1));

        Assert.Equal(19.90m, price);
    }

    [Fact]
    public void A_later_price_never_applies_retroactively()
    {
        var price = ServicePricingEngine.ResolvePrice(HydraulicWater, Rules, new DateOnly(2023, 12, 31));

        Assert.Equal(45.92m, price);
    }

    [Fact]
    public void The_new_price_applies_from_its_effective_date()
    {
        var price = ServicePricingEngine.ResolvePrice(HydraulicOil, Rules, new DateOnly(2024, 1, 1));

        Assert.Equal(33.59m, price);
    }

    [Fact]
    public void No_rule_in_force_yet_resolves_to_null_not_a_default()
    {
        var price = ServicePricingEngine.ResolvePrice(Rr, Rules, new DateOnly(2020, 1, 1));

        Assert.Null(price);
    }

    [Fact]
    public void A_service_without_any_tariff_has_no_price()
    {
        var price = ServicePricingEngine.ResolvePrice(serviceId: 99, Rules, new DateOnly(2024, 6, 1));

        Assert.Null(price);
    }

    [Fact]
    public void Hydraulic_oil_and_water_never_share_a_price()
    {
        var asOfDate = new DateOnly(2023, 6, 1);

        var oilPrice = ServicePricingEngine.ResolvePrice(HydraulicOil, Rules, asOfDate);
        var waterPrice = ServicePricingEngine.ResolvePrice(HydraulicWater, Rules, asOfDate);

        Assert.NotEqual(oilPrice, waterPrice);
    }

    [Fact]
    public void A_tie_on_EffectiveOn_resolves_deterministically_by_input_order()
    {
        // unique (prestation_id, date_effet) makes this precondition violation
        // impossible against the real catalogue — this only documents that
        // ResolvePrice neither throws nor picks randomly on malformed input.
        var tiedRules = new[]
        {
            new ServicePriceRule(Rr, 10m, new DateOnly(2025, 1, 1)),
            new ServicePriceRule(Rr, 20m, new DateOnly(2025, 1, 1)),
        };

        var price = ServicePricingEngine.ResolvePrice(Rr, tiedRules, new DateOnly(2025, 6, 1));

        Assert.Equal(10m, price);
    }

    [Fact]
    public void A_supplier_service_resolves_its_own_tariffs_only()
    {
        var service = new SupplierService
        {
            Id = HydraulicOil,
            SupplierId = 1,
            Name = "Hydraulique (huile)",
            Control = CylinderControl.Hydraulic,
            Method = HydraulicMethod.Oil,
            Tariffs = [.. Rules],
        };

        Assert.Equal(32.61m, service.PriceAt(new DateOnly(2023, 6, 1)));
        Assert.Equal(33.59m, service.PriceAt(new DateOnly(2024, 6, 1)));
    }
}
