using MoaMat.Domain.Common;
using MoaMat.Domain.Suppliers;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.UnitTests.Suppliers;

/// <summary>
/// The supplier mapper converts contact rows, catalogue services, their dated
/// tariffs and the control / bath codes — and refuses a code the application
/// does not know instead of silently guessing between oil and water.
/// </summary>
public sealed class SupplierMapperTests
{
    [Fact]
    public void ToDomain_of_a_supplier_keeps_the_contact_details_and_the_archive_flag()
    {
        var record = new SupplierRecord
        {
            Id = 7,
            Nom = "Apragaz",
            Adresse = "1 rue du Test",
            Telephone = "+32 2 000 00 00",
            Email = "contact@test.example",
            PersonneContact = "Jeanne Test",
            SiteWeb = "https://test.example",
            Actif = false,
        };

        var supplier = SupplierMapper.ToDomain(record);

        Assert.Equal(7, supplier.Id);
        Assert.Equal("Apragaz", supplier.Name);
        Assert.Equal("Jeanne Test", supplier.ContactPerson);
        Assert.Equal("https://test.example", supplier.WebsiteUrl);
        Assert.False(supplier.IsActive);
    }

    [Fact]
    public void ToDomain_of_a_service_carries_control_method_and_tariffs()
    {
        var record = new SupplierServiceRecord
        {
            Id = 3,
            FournisseurId = 7,
            Libelle = "Hydraulique (huile)",
            ControleCylindre = "hydraulique",
            MethodeHydraulique = "huile",
            Actif = true,
        };
        var tariff = new ServicePriceRule(3, 35.82m, new DateOnly(2026, 1, 1), 11);

        var service = SupplierMapper.ToDomain(record, [tariff]);

        Assert.Equal(CylinderControl.Hydraulic, service.Control);
        Assert.Equal(HydraulicMethod.Oil, service.Method);
        Assert.Equal(35.82m, service.PriceAt(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void ToDomain_of_a_free_service_has_neither_control_nor_method()
    {
        var record = new SupplierServiceRecord { Id = 4, FournisseurId = 7, Libelle = "Entretien", Actif = true };

        var service = SupplierMapper.ToDomain(record, []);

        Assert.Null(service.Control);
        Assert.Null(service.Method);
        Assert.False(service.IsCylinderService);
        Assert.Null(service.PriceAt(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void ToDomain_of_a_tariff_converts_the_sql_date_and_keeps_the_identifier()
    {
        var record = new SupplierServiceTariffRecord
        {
            Id = 1,
            PrestationId = 3,
            PrixEur = 35.82m,
            DateEffet = new DateTime(2026, 1, 1),
        };

        var rule = SupplierMapper.ToDomain(record);

        Assert.Equal(1, rule.Id);
        Assert.Equal(3, rule.ServiceId);
        Assert.Equal(new DateOnly(2026, 1, 1), rule.EffectiveOn);
    }

    [Fact]
    public void ToDomain_of_a_tariff_without_date_is_a_data_error()
    {
        var record = new SupplierServiceTariffRecord { Id = 1, PrestationId = 3, PrixEur = 1m, DateEffet = null };

        Assert.Throws<DataAccessException>(() => SupplierMapper.ToDomain(record));
    }

    [Theory]
    [InlineData("optique", CylinderControl.Optical)]
    [InlineData("hydraulique", CylinderControl.Hydraulic)]
    public void Control_codes_round_trip(string code, CylinderControl control)
    {
        Assert.Equal(control, SupplierMapper.ToControl(code));
        Assert.Equal(code, SupplierMapper.ToCode(control));
    }

    [Theory]
    [InlineData("huile", HydraulicMethod.Oil)]
    [InlineData("eau", HydraulicMethod.Water)]
    public void Method_codes_round_trip(string code, HydraulicMethod method)
    {
        Assert.Equal(method, SupplierMapper.ToMethod(code));
        Assert.Equal(code, SupplierMapper.ToCode(method));
    }

    [Fact]
    public void An_absent_control_or_method_is_null_both_ways()
    {
        Assert.Null(SupplierMapper.ToControl(null));
        Assert.Null(SupplierMapper.ToMethod(null));
        Assert.Null(SupplierMapper.ToCode((CylinderControl?)null));
        Assert.Null(SupplierMapper.ToCode((HydraulicMethod?)null));
    }

    [Fact]
    public void An_unknown_code_is_refused_never_guessed()
    {
        Assert.Throws<DataAccessException>(() => SupplierMapper.ToControl("vapeur"));
        Assert.Throws<DataAccessException>(() => SupplierMapper.ToMethod("vapeur"));
    }
}
