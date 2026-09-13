namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Resolves a cylinder's (<see cref="CylinderUsage"/>, <see cref="CylinderMaterial"/>)
/// pair to the reference type code the periodicity referential is keyed on.
/// </summary>
/// <remarks>
/// Mirrors <c>public.bouteille_type_referentiel(famille, matiere)</c>
/// (<c>db/item_bouteille.sql</c>) exactly, so the same 6 profiles resolve the
/// same way in the database and in this pure, testable engine — including at
/// the edges: a missing material for a diving cylinder is an INCOMPLETE
/// classification (the SQL side propagates NULL for it, never an error, since
/// <c>item_bouteille.matiere</c> is nullable while awaiting classification),
/// while an out-of-domain material code is an invalid/unrecognized value and
/// both sides fail loudly on it. The 6 valid codes match the 6 rows of the
/// legacy <c>public.ref_regle_requalification</c> mirror: Plongée
/// ACIER/ALU/Carbonne, Deco O², O² Secourisme, Tampons.
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
    /// Resolves the reference type code for a cylinder profile, or <c>null</c>
    /// when the classification is not complete enough to resolve yet.
    /// </summary>
    /// <param name="usage">Usage classification of the cylinder.</param>
    /// <param name="material">
    /// Material of the cylinder. Only meaningful when <paramref name="usage"/>
    /// is <see cref="CylinderUsage.Diving"/>; <c>null</c> there means the
    /// classification is incomplete (e.g. a not-yet-classified legacy item),
    /// not an error.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="material"/> is a code outside the known set — a genuine
    /// data problem, never a legitimately incomplete classification.
    /// </exception>
    public static string? Resolve(CylinderUsage usage, CylinderMaterial? material)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (ReferenceEquals(usage, CylinderUsage.Diving))
        {
            if (material is null)
            {
                // Incomplete classification, not an error: mirrors
                // public.bouteille_type_referentiel('plongee', null) -> NULL.
                return null;
            }

            return material.Code switch
            {
                "acier" => DivingSteelCode,
                "alu" => DivingAluminiumCode,
                "carbone" => DivingCarbonCode,
                _ => throw new ArgumentException($"Unknown cylinder material '{material.Code}'.", nameof(material)),
            };
        }

        return usage.Code;
    }
}
