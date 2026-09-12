namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Resolves a cylinder's (<see cref="CylinderFamily"/>, <see cref="CylinderMaterial"/>)
/// pair to the reference type code the periodicity referential is keyed on.
/// </summary>
/// <remarks>
/// Mirrors <c>public.bouteille_type_referentiel(famille, matiere)</c>
/// (<c>db/item_bouteille.sql</c>) exactly, so the same 6 profiles resolve the
/// same way in the database and in this pure, testable engine. The 6 codes
/// match the 6 rows of the legacy <c>public.ref_regle_requalification</c>
/// mirror: Plongée ACIER/ALU/Carbonne, Deco O², O² Secourisme, Tampons.
/// </remarks>
public static class CylinderReferenceType
{
    /// <summary>Code for a diving cylinder made of the given material.</summary>
    public const string DivingSteelCode = "plongee_acier";

    /// <summary>Code for a diving cylinder made of the given material.</summary>
    public const string DivingAluminiumCode = "plongee_alu";

    /// <summary>Code for a diving cylinder made of the given material.</summary>
    public const string DivingCarbonCode = "plongee_carbone";

    /// <summary>Code for a decompression oxygen cylinder.</summary>
    public const string DecoOxygenCode = "deco_o2";

    /// <summary>Code for a rescue oxygen cylinder.</summary>
    public const string RescueOxygenCode = "o2_secourisme";

    /// <summary>Code for a buffer tank cylinder.</summary>
    public const string BufferTankCode = "bloc_tampon";

    /// <summary>
    /// Resolves the reference type code for a cylinder profile.
    /// </summary>
    /// <param name="family">Usage family of the cylinder.</param>
    /// <param name="material">
    /// Material of the cylinder. Required — and only meaningful — when
    /// <paramref name="family"/> is <see cref="CylinderFamily.Diving"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="family"/> is <see cref="CylinderFamily.Diving"/> and <paramref name="material"/> is <c>null</c>:
    /// the periodicity referential cannot be resolved without a material for that family.
    /// </exception>
    public static string Resolve(CylinderFamily family, CylinderMaterial? material)
    {
        ArgumentNullException.ThrowIfNull(family);

        if (ReferenceEquals(family, CylinderFamily.Diving))
        {
            if (material is null)
            {
                throw new ArgumentException(
                    "A diving cylinder requires a material to resolve its periodicity reference type.",
                    nameof(material));
            }

            return material.Code switch
            {
                "acier" => DivingSteelCode,
                "alu" => DivingAluminiumCode,
                "carbone" => DivingCarbonCode,
                _ => throw new ArgumentException($"Unknown cylinder material '{material.Code}'.", nameof(material)),
            };
        }

        return family.Code;
    }
}
