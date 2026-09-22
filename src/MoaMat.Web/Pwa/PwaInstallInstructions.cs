namespace MoaMat.Web.Pwa;

/// <summary>
/// The steps a member has to follow to add MoaMat to their home screen, one
/// wording per browser family.
/// </summary>
/// <remarks>
/// A browser that kept an installation prompt does not need any of this: the
/// tip shows its own button instead. These steps are the manual path, for
/// iOS - which never offers a prompt - and for every browser that declines to.
/// </remarks>
internal static class PwaInstallInstructions
{
    private static readonly string[] Ios =
    [
        "Touchez le bouton Partager de Safari (le carré avec une flèche).",
        "Faites défiler, puis choisissez « Sur l'écran d'accueil ».",
        "Confirmez avec « Ajouter » : MoaMat rejoint vos applications.",
    ];

    private static readonly string[] Android =
    [
        "Ouvrez le menu du navigateur (les trois points).",
        "Choisissez « Installer l'application » ou « Ajouter à l'écran d'accueil ».",
        "Confirmez : MoaMat rejoint vos applications.",
    ];

    private static readonly string[] Desktop =
    [
        "Cliquez sur l'icône d'installation, à droite de la barre d'adresse.",
        "Choisissez « Installer ».",
        "MoaMat s'ouvre ensuite dans sa propre fenêtre.",
    ];

    private static readonly string[] Unknown =
    [
        "Ouvrez le menu de votre navigateur.",
        "Cherchez « Installer l'application » ou « Ajouter à l'écran d'accueil ».",
        "Confirmez : MoaMat rejoint vos applications.",
    ];

    /// <summary>Steps to display for a browser family.</summary>
    /// <param name="platform">Family reported by the browser.</param>
    public static IReadOnlyList<string> For(PwaInstallPlatform platform) => platform switch
    {
        PwaInstallPlatform.Ios => Ios,
        PwaInstallPlatform.Android => Android,
        PwaInstallPlatform.Desktop => Desktop,
        _ => Unknown,
    };
}
