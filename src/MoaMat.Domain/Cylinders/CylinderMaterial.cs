namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Material a diving cylinder is made of. <see cref="Code"/> is the
/// discriminator stored in <c>public.item_bouteille.matiere</c>; it stays in
/// French to match the schema exactly.
/// </summary>
/// <remarks>
/// Only meaningful for <see cref="CylinderFamily.Diving"/>: steel, aluminium
/// and carbon cylinders each have their own regulatory requalification
/// periodicity (see <see cref="CylinderReferenceType"/>), unlike the other
/// cylinder families whose periodicity does not depend on material.
/// </remarks>
public sealed record CylinderMaterial
{
    private CylinderMaterial(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    /// <summary>Steel (<c>acier</c>).</summary>
    public static CylinderMaterial Steel { get; } = new("acier", "Acier");

    /// <summary>Aluminium (<c>alu</c>).</summary>
    public static CylinderMaterial Aluminium { get; } = new("alu", "Aluminium");

    /// <summary>Carbon fibre (<c>carbone</c>).</summary>
    public static CylinderMaterial Carbon { get; } = new("carbone", "Carbone");

    /// <summary>Every known material, in display order.</summary>
    public static IReadOnlyList<CylinderMaterial> All { get; } = [Steel, Aluminium, Carbon];

    /// <summary>Discriminator stored in <c>public.item_bouteille.matiere</c>.</summary>
    public string Code { get; }

    /// <summary>Label shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Resolves a material from its code, or <c>null</c> when the code is
    /// unknown or absent.
    /// </summary>
    /// <param name="code">Raw material code read from the database.</param>
    public static CylinderMaterial? FromCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(material => string.Equals(material.Code, code, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => Code;
}
