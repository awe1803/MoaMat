namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Usage family of a diving cylinder. <see cref="Code"/> is the discriminator
/// stored in <c>public.item_bouteille.famille</c>; it stays in French to match
/// the schema exactly. <see cref="DisplayName"/> is user-facing French.
/// </summary>
/// <remarks>
/// This is one of the two axes the regulatory periodicity referential is keyed
/// on (see <see cref="CylinderReferenceType"/>): <see cref="Diving"/> further
/// splits by <see cref="CylinderMaterial"/> because steel, aluminium and
/// carbon cylinders have distinct requalification periodicities, while the
/// other three families each have a single periodicity regardless of material.
/// </remarks>
public sealed record CylinderFamily
{
    private CylinderFamily(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    /// <summary>Diving cylinders (<c>plongee</c>) — periodicity also depends on <see cref="CylinderMaterial"/>.</summary>
    public static CylinderFamily Diving { get; } = new("plongee", "Plongée");

    /// <summary>Decompression oxygen cylinders (<c>deco_o2</c>).</summary>
    public static CylinderFamily DecoOxygen { get; } = new("deco_o2", "Déco O₂");

    /// <summary>Rescue oxygen cylinders (<c>o2_secourisme</c>).</summary>
    public static CylinderFamily RescueOxygen { get; } = new("o2_secourisme", "O₂ secourisme");

    /// <summary>Buffer tank cylinders (<c>bloc_tampon</c>).</summary>
    public static CylinderFamily BufferTank { get; } = new("bloc_tampon", "Bloc tampon");

    /// <summary>Every known family, in display order.</summary>
    public static IReadOnlyList<CylinderFamily> All { get; } =
        [Diving, DecoOxygen, RescueOxygen, BufferTank];

    /// <summary>Discriminator stored in <c>public.item_bouteille.famille</c>.</summary>
    public string Code { get; }

    /// <summary>Label shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Resolves a family from its code, or <c>null</c> when the code is unknown.
    /// Unknown codes are possible: the database is the source of truth and may
    /// gain a family before the client does.
    /// </summary>
    /// <param name="code">Raw family code read from the database.</param>
    public static CylinderFamily? FromCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(family => string.Equals(family.Code, code, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => Code;
}
