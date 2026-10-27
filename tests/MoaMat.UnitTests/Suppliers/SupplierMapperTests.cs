using MoaMat.Domain.Cylinders;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.UnitTests.Suppliers;

/// <summary>
/// The supplier mapper joins the supplier row with its service rows and must
/// tolerate a service code the application does not know yet.
/// </summary>
public sealed class SupplierMapperTests
{
    [Fact]
    public void ToDomain_returns_known_services_in_display_order_and_drops_unknown_codes()
    {
        var record = new SupplierRecord { Id = 7, Nom = "Apragaz", Actif = true };

        var supplier = SupplierMapper.ToDomain(
            record,
            ["hydraulique_eau", "code_inconnu", "rr"]);

        Assert.Equal(7, supplier.Id);
        Assert.Equal([CylinderServiceType.Rr, CylinderServiceType.HydraulicWater], supplier.Services);
    }

    [Fact]
    public void ToDomain_of_a_tariff_converts_the_sql_date_and_skips_an_unknown_service()
    {
        var known = new SupplierTariffRecord
        {
            Id = 1,
            FournisseurId = 7,
            TypePrestation = "hydraulique_huile",
            PrixEur = 35.82m,
            DateEffet = new DateTime(2026, 1, 1),
        };
        var unknown = new SupplierTariffRecord { Id = 2, FournisseurId = 7, TypePrestation = "autre" };

        var tariff = SupplierMapper.ToDomain(known);

        Assert.NotNull(tariff);
        Assert.Equal(CylinderServiceType.HydraulicOil, tariff.Service);
        Assert.Equal(new DateOnly(2026, 1, 1), tariff.EffectiveOn);
        Assert.Null(SupplierMapper.ToDomain(unknown));
    }
}
