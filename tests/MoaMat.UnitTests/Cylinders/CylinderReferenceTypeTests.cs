using MoaMat.Domain.Cylinders;

namespace MoaMat.UnitTests.Cylinders;

/// <summary>
/// <see cref="CylinderReferenceType.Resolve"/> is the pure C# mirror of
/// <c>public.bouteille_type_referentiel()</c> — it must resolve the same 6
/// profiles the same way so the engine and the database never disagree.
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

        Assert.Equal(expected, CylinderReferenceType.Resolve(CylinderFamily.Diving, material));
    }

    [Fact]
    public void A_diving_cylinder_without_a_material_cannot_be_resolved()
    {
        // Material is the only extra input Diving needs; a missing one must
        // fail loudly rather than silently pick a default periodicity.
        Assert.Throws<ArgumentException>(
            () => CylinderReferenceType.Resolve(CylinderFamily.Diving, material: null));
    }

    [Theory]
    [InlineData("deco_o2", CylinderReferenceType.DecoOxygenCode)]
    [InlineData("o2_secourisme", CylinderReferenceType.RescueOxygenCode)]
    [InlineData("bloc_tampon", CylinderReferenceType.BufferTankCode)]
    public void The_other_three_families_resolve_regardless_of_material(string familyCode, string expected)
    {
        var family = CylinderFamily.FromCode(familyCode)!;

        Assert.Equal(expected, CylinderReferenceType.Resolve(family, material: null));
        Assert.Equal(expected, CylinderReferenceType.Resolve(family, CylinderMaterial.Steel));
    }
}
