namespace MoaMat.Web.Pwa;

/// <summary>
/// What the current browser, and the signed-in account, say about installing
/// the application.
/// </summary>
/// <param name="IsInstalled">The application already runs from the home screen.</param>
/// <param name="IsDismissed">The account asked never to see the tip again.</param>
/// <param name="CanPrompt">The browser kept an installation prompt we can show.</param>
/// <param name="Platform">Which manual instructions apply.</param>
internal sealed record PwaInstallStatus(
    bool IsInstalled,
    bool IsDismissed,
    bool CanPrompt,
    PwaInstallPlatform Platform)
{
    /// <summary>Whether the tip has anything left to offer.</summary>
    public bool ShouldOffer => !IsInstalled && !IsDismissed;
}
