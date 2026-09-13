namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Usage classification of a cylinder. <see cref="Code"/> is the discriminator
/// stored in <c>public.item_bouteille.famille</c>; it stays in French to match
/// the schema exactly. <see cref="DisplayName"/> is user-facing French.
/// </summary>
/// <remarks>
/// Deliberately NOT named <c>CylinderFamily</c>: <c>MoaMat.Domain.Inventory.ItemFamily.Cylinder</c>
/// is a different axis (which item family this is — always "bouteille" for any
/// cylinder) from this one (what a cylinder is used for). Both happen to back a
/// column literally called <c>famille</c>, which is exactly why reusing "family"
/// here would invite confusion between the two axes.
///
/// This is one of the two axes the regulatory periodicity referential is keyed
/// on (see <see cref="CylinderReferenceType"/>): <see cref="Diving"/> further
/// splits by <see cref="CylinderMaterial"/> because steel, aluminium and
/// carbon cylinders have distinct requalification periodicities, while the
/// other three usages each have a single periodicity regardless of material.
/// </remarks>
public sealed record CylinderUsage
{
    private CylinderUsage(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    /// <summary>Diving cylinders (<c>plongee</c>) — periodicity also depends on <see cref="CylinderMaterial"/>.</summary>
    public static CylinderUsage Diving { get; } = new("plongee", "Plongée");

    /// <summary>Decompression oxygen cylinders (<c>deco_o2</c>).</summary>
    public static CylinderUsage DecoOxygen { get; } = new("deco_o2", "Déco O₂");

    /// <summary>Rescue oxygen cylinders (<c>o2_secourisme</c>).</summary>
    public static CylinderUsage RescueOxygen { get; } = new("o2_secourisme", "O₂ secourisme");

    /// <summary>Buffer tank cylinders (<c>bloc_tampon</c>).</summary>
    public static CylinderUsage BufferTank { get; } = new("bloc_tampon", "Bloc tampon");

    /// <summary>Every known usage, in display order.</summary>
    public static IReadOnlyList<CylinderUsage> All { get; } =
        [Diving, DecoOxygen, RescueOxygen, BufferTank];

    /// <summary>Discriminator stored in <c>public.item_bouteille.famille</c>.</summary>
    public string Code { get; }

    /// <summary>Label shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Resolves a usage from its code, or <c>null</c> when the code is unknown.
    /// Unknown codes are possible: the database is the source of truth and may
    /// gain a usage before the client does.
    /// </summary>
    /// <param name="code">Raw usage code read from the database.</param>
    public static CylinderUsage? FromCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(usage => string.Equals(usage.Code, code, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => Code;
}
