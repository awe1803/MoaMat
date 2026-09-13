using MoaMat.Domain.Cylinders;

namespace MoaMat.UnitTests.Cylinders;

/// <summary>
/// <see cref="CylinderReferenceType.Resolve"/> is the pure C# mirror of
/// <c>public.bouteille_type_referentiel()</c> — it must resolve the same 6
/// profiles the same way, including at the edges, so the engine and the
/// database never disagree.
/// </summary>
public sealed class CylinderReferenceTypeTests
{
    [Theory]
    [InlineData("acier", CylinderReferenceType.DivingSteelCode)]
    [InlineData("alu", CylinderReferenceType.DivingAluminiumCode)]
    [InlineData("carbone", CylinderReferenceType.DivingCarbonCode)]
    public void A_diving_cylinder_resolves_by_material(string materialCode, string expected)
    {
        var material = CylinderMaterial.FromCode(materialCode);

        Assert.Equal(expected, CylinderReferenceType.Resolve(CylinderUsage.Diving, material));
    }

    [Fact]
    public void A_diving_cylinder_without_a_material_resolves_to_null_not_an_error()
    {
        // Mirrors public.bouteille_type_referentiel('plongee', null) -> NULL:
        // an incomplete classification (matiere not yet set) is not a fault,
        // so it must propagate as null, not throw.
        Assert.Null(CylinderReferenceType.Resolve(CylinderUsage.Diving, material: null));
    }

    [Theory]
    [InlineData("deco_o2", CylinderReferenceType.DecoOxygenCode)]
    [InlineData("o2_secourisme", CylinderReferenceType.RescueOxygenCode)]
    [InlineData("bloc_tampon", CylinderReferenceType.BufferTankCode)]
    public void The_other_three_usages_resolve_regardless_of_material(string usageCode, string expected)
    {
        var usage = CylinderUsage.FromCode(usageCode)!;

        Assert.Equal(expected, CylinderReferenceType.Resolve(usage, material: null));
        Assert.Equal(expected, CylinderReferenceType.Resolve(usage, CylinderMaterial.Steel));
    }
}
