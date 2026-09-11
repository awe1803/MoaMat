namespace MoaMat.Domain.Inventory;

/// <summary>
/// Family an inventory item belongs to. <see cref="Code"/> is the discriminator
/// stored in <c>public.item.famille</c> and used to pick the specialisation
/// table (<c>item_bouteille</c>, <c>item_detendeur</c>, ...); it stays in French
/// to match the schema exactly. <see cref="DisplayName"/> is user-facing French.
/// </summary>
public sealed record ItemFamily
{
    private ItemFamily(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    /// <summary>Diving cylinders (<c>bouteille</c>).</summary>
    public static ItemFamily Cylinder { get; } = new("bouteille", "Bouteilles");

    /// <summary>Regulators (<c>detendeur</c>).</summary>
    public static ItemFamily Regulator { get; } = new("detendeur", "Détendeurs");

    /// <summary>Buoyancy vests (<c>gilet</c>).</summary>
    public static ItemFamily BuoyancyVest { get; } = new("gilet", "Gilets");

    /// <summary>Small equipment (<c>petit_materiel</c>).</summary>
    public static ItemFamily SmallEquipment { get; } = new("petit_materiel", "Petit matériel");

    /// <summary>Teaching material (<c>materiel_didactique</c>).</summary>
    public static ItemFamily TeachingMaterial { get; } = new("materiel_didactique", "Matériel didactique");

    /// <summary>Spare parts (<c>piece_detachee</c>).</summary>
    public static ItemFamily SparePart { get; } = new("piece_detachee", "Pièces détachées");

    /// <summary>Every known family, in display order.</summary>
    public static IReadOnlyList<ItemFamily> All { get; } =
        [Cylinder, Regulator, BuoyancyVest, SmallEquipment, TeachingMaterial, SparePart];

    /// <summary>Discriminator stored in <c>public.item.famille</c>.</summary>
    public string Code { get; }

    /// <summary>Label shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Resolves a family from its code, or <c>null</c> when the code is unknown.
    /// Unknown codes are possible: the database is the source of truth and may
    /// gain a family before the client does.
    /// </summary>
    /// <param name="code">Raw family code read from the database.</param>
    public static ItemFamily? FromCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(family => string.Equals(family.Code, code, StringComparison.Ordinal));

    /// <summary>
    /// Label for a raw family code, falling back to the code itself so an
    /// unknown family still renders something meaningful instead of a blank.
    /// </summary>
    /// <param name="code">Raw family code read from the database.</param>
    public static string DisplayNameFor(string? code) =>
        FromCode(code)?.DisplayName ?? code ?? string.Empty;

    /// <inheritdoc />
    public override string ToString() => Code;
}
