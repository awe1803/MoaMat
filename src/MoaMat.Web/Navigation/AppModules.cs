using MoaMat.Domain.Accounts;
using MoaMat.Domain.Inventory;

namespace MoaMat.Web.Navigation;

/// <summary>
/// Module catalogue of the application, in display order.
/// </summary>
/// <remarks>
/// Groups are ordered by first appearance in <see cref="All"/>, so the reading
/// order of this file is the reading order of the sidebar. The six inventory
/// families are wired to the inventory screen through a family deep link; the
/// rest of the perimeter drawn in the mock-up is listed as a placeholder so the
/// roadmap stays visible in the product itself.
/// </remarks>
public static class AppModules
{
    /// <summary>Route prefix of the "coming later" screen.</summary>
    public const string PlaceholderRoutePrefix = "module";

    /// <summary>Every module, sidebar order.</summary>
    public static IReadOnlyList<AppModule> All { get; } =
    [
        Family(ItemFamily.Cylinder, "Cœur métier", "bouteille", "Bouteilles"),
        Family(ItemFamily.Regulator, "Cœur métier", "detendeur", "Détendeurs"),
        Planned(
            key: "sauvetage",
            group: "Cœur métier",
            icon: "sauvetage",
            title: "Matériel de sauvetage",
            subtitle: "DEA, kit O₂",
            tag: "candidat phase 1",
            text: "Aucune donnée existante — DEA, kit O₂ et trousses de secours sont à modéliser de zéro. Échéances critiques (électrodes, batteries)."),

        Family(ItemFamily.BuoyancyVest, "Matériel", "gilet", "Gilets"),
        Family(ItemFamily.SmallEquipment, "Matériel", "petit", "Petit matériel"),
        Family(ItemFamily.TeachingMaterial, "Matériel", "didactique", "Matériel didactique"),
        Planned(
            key: "compresseurs",
            group: "Matériel",
            icon: "compresseur",
            title: "Compresseurs",
            subtitle: "2 sites",
            tag: "nouveau — phase 2",
            text: "Relevé d'index horaire hebdomadaire et maintenance déclenchée sur compteur d'heures, par site."),

        Planned(
            key: "prets",
            group: "Opérations",
            icon: "prets",
            title: "Prêts",
            subtitle: "réservations",
            tag: "phase 2",
            text: "Saisie par scan de deux QR codes (membre + matériel) — trois tentatives de saisie manuelle ont échoué par le passé."),
        Planned(
            key: "membres",
            group: "Opérations",
            icon: "membres",
            title: "Bouteilles de membres",
            subtitle: "matériel de tiers",
            tag: "à confirmer",
            text: "Service rendu aux membres, matériel de tiers — n'entre jamais dans l'inventaire ni la valeur d'assurance du club."),
        Planned(
            key: "scanner",
            group: "Opérations",
            icon: "scan",
            title: "Scanner",
            subtitle: "QR code terrain",
            tag: "phase 1",
            text: "Lecture du QR code collé sur le matériel, puis ouverture directe de la fiche avec son statut de validité."),

        Planned(
            key: "achats",
            group: "Gestion",
            icon: "achats",
            title: "Achats & devis",
            subtitle: "budget, factures",
            tag: "phase 3",
            text: "Comparaison de devis, suivi budgétaire par famille et par exercice, factures scannées."),
        Family(ItemFamily.SparePart, "Gestion", "pieces", "Pièces détachées"),

        new()
        {
            Key = "inventaire",
            Group = "Système",
            Icon = "grid",
            Title = "Inventaire complet",
            Subtitle = "toutes familles",
            Href = "inventaire",
            Policy = AuthorizationPolicyNames.ReaderOrHigher,
        },
        new()
        {
            Key = "lieux",
            Group = "Système",
            Icon = "lieux",
            Title = "Lieux",
            Subtitle = "section, local, contenant",
            Href = "lieux",
            Policy = AuthorizationPolicyNames.AdministratorOrHigher,
        },
        new()
        {
            Key = "comptes",
            Group = "Système",
            Icon = "comptes",
            Title = "Comptes",
            Subtitle = "rôles et activation",
            Href = "comptes",
            Policy = AuthorizationPolicyNames.AdministratorOrHigher,
        },
        new()
        {
            Key = "journal-audit",
            Group = "Système",
            Icon = "audit",
            Title = "Journal d'audit",
            Subtitle = "événements sensibles",
            Href = "journal-audit",
            Policy = AuthorizationPolicyNames.AdministratorOrHigher,
        },
        Planned(
            key: "exports",
            group: "Système",
            icon: "exports",
            title: "Exports & états",
            subtitle: "7 modèles",
            tag: "phase 1",
            text: "Inventaire complet, feuille de contrôle terrain, fiche de vie PDF, état financier pour le CA."),
    ];

    /// <summary>Group headings, in sidebar order.</summary>
    public static IReadOnlyList<string> Groups { get; } =
        All.Select(module => module.Group).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Modules of one group, in declaration order.</summary>
    /// <param name="group">Group heading.</param>
    public static IEnumerable<AppModule> InGroup(string group) =>
        All.Where(module => string.Equals(module.Group, group, StringComparison.Ordinal));

    /// <summary>Module carrying <paramref name="key"/>, or <c>null</c> when unknown.</summary>
    /// <param name="key">Module key, as it appears in the placeholder route.</param>
    public static AppModule? FromKey(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(module => string.Equals(module.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deep link opening the inventory screen filtered on one family.</summary>
    /// <param name="familyCode">Family discriminator, see <see cref="ItemFamily.Code"/>.</param>
    public static string InventoryHref(string familyCode) =>
        $"inventaire?famille={Uri.EscapeDataString(familyCode)}";

    private static AppModule Family(ItemFamily family, string group, string icon, string title) => new()
    {
        Key = family.Code,
        Group = group,
        Icon = icon,
        Title = title,
        Subtitle = "voir l'inventaire",
        FamilyCode = family.Code,
        Href = InventoryHref(family.Code),
        Policy = AuthorizationPolicyNames.ReaderOrHigher,
    };

    private static AppModule Planned(
        string key,
        string group,
        string icon,
        string title,
        string subtitle,
        string tag,
        string text) => new()
        {
            Key = key,
            Group = group,
            Icon = icon,
            Title = title,
            Subtitle = subtitle,
            Href = $"{PlaceholderRoutePrefix}/{key}",
            IsAvailable = false,
            PlannedTag = tag,
            PlannedText = text,
        };
}
