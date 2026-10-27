using MoaMat.Domain.Suppliers;

namespace MoaMat.UnitTests.Suppliers;

/// <summary>
/// <see cref="SupplierServiceRequest.Create"/> mirrors the database rules of
/// <c>public.enregistrer_prestation_fournisseur()</c>: above all, a hydraulic
/// service always names oil or water, and no other service ever does.
/// </summary>
public sealed class SupplierServiceRequestTests
{
    [Fact]
    public void A_free_service_needs_neither_control_nor_method()
    {
        var result = SupplierServiceRequest.Create(null, 1, "  Entretien compresseur ", null, null, true, "  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Entretien compresseur", result.Value!.Name);
        Assert.Null(result.Value.Control);
        Assert.Null(result.Value.Method);
        Assert.Null(result.Value.Comment);
    }

    [Fact]
    public void An_optical_service_is_accepted_without_method()
    {
        var result = SupplierServiceRequest.Create(null, 1, "RR", CylinderControl.Optical, null, true, null);

        Assert.True(result.Succeeded);
        Assert.Equal(CylinderControl.Optical, result.Value!.Control);
    }

    [Theory]
    [InlineData(HydraulicMethod.Oil)]
    [InlineData(HydraulicMethod.Water)]
    public void A_hydraulic_service_keeps_its_explicit_method(HydraulicMethod method)
    {
        var result = SupplierServiceRequest.Create(null, 1, "Hydraulique", CylinderControl.Hydraulic, method, true, null);

        Assert.True(result.Succeeded);
        Assert.Equal(method, result.Value!.Method);
    }

    [Fact]
    public void A_hydraulic_service_without_method_is_refused_never_defaulted()
    {
        var result = SupplierServiceRequest.Create(null, 1, "Hydraulique", CylinderControl.Hydraulic, null, true, null);

        Assert.False(result.Succeeded);
        Assert.Contains("huile", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CylinderControl.Optical)]
    public void A_method_on_a_non_hydraulic_service_is_refused(CylinderControl? control)
    {
        var result = SupplierServiceRequest.Create(null, 1, "Autre", control, HydraulicMethod.Oil, true, null);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string? name)
    {
        var result = SupplierServiceRequest.Create(null, 1, name, null, null, true, null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Modifying_a_service_carries_its_identifier_and_activation()
    {
        var result = SupplierServiceRequest.Create(42, 1, "RR", CylinderControl.Optical, null, false, null);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Value!.ServiceId);
        Assert.False(result.Value.IsActive);
    }
}
