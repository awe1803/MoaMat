using Microsoft.JSInterop;
using MoaMat.Domain.Common;
using MoaMat.Domain.Preferences;

namespace MoaMat.Web.Pwa;

/// <summary>
/// Drives the "install MoaMat on this device" tip: the browser side lives in
/// <c>wwwroot/js/pwa-install.js</c>, the "never show it again" choice is stored
/// through <see cref="IUserPreferenceRepository"/>.
/// </summary>
/// <remarks>
/// The preference is deliberately attached to the account rather than to the
/// browser: somebody who answered "ne plus afficher ce message" means it for
/// good, and a browser store would forget it the day site data is cleared, or
/// simply never know about it on their next device.
/// </remarks>
internal sealed class PwaInstallService : IAsyncDisposable
{
    private const string ModulePath = "./js/pwa-install.js";

    private readonly IJSRuntime _jsRuntime;
    private readonly IUserPreferenceRepository _preferences;

    private IJSObjectReference? _module;

    /// <summary>Creates the service.</summary>
    /// <param name="jsRuntime">Browser JavaScript runtime.</param>
    /// <param name="preferences">Store of the account display preferences.</param>
    public PwaInstallService(IJSRuntime jsRuntime, IUserPreferenceRepository preferences)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentNullException.ThrowIfNull(preferences);
        _jsRuntime = jsRuntime;
        _preferences = preferences;
    }

    /// <summary>
    /// Reads what should be offered right now: what this browser can do, and
    /// what the signed-in account already answered.
    /// </summary>
    /// <exception cref="DataAccessException">The stored choice could not be read.</exception>
    public async Task<PwaInstallStatus> GetStatusAsync()
    {
        var module = await GetModuleAsync();
        var status = await module.InvokeAsync<BrowserStatus>("getStatus");

        // Nothing is asked of the database for a browser that has nothing to
        // offer anyway - the installed application opens on every start-up.
        var isDismissed = !status.Installed && await _preferences.IsInstallTipHiddenAsync();

        return new PwaInstallStatus(
            status.Installed,
            isDismissed,
            status.CanPrompt,
            ToPlatform(status.Platform));
    }

    /// <summary>
    /// Shows the browser's own installation prompt. Call it straight from the
    /// click handler: browsers only show it during a user gesture.
    /// </summary>
    /// <returns><see langword="true"/> when the application was installed.</returns>
    public async Task<bool> PromptAsync()
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<bool>("prompt");
    }

    /// <summary>Stores, or clears, the "never show the tip again" choice.</summary>
    /// <param name="dismissed">Whether the account must stop being offered the tip.</param>
    public Task<OperationResult> SetDismissedAsync(bool dismissed) =>
        _preferences.SetInstallTipHiddenAsync(dismissed);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away: nothing left to release.
            }
        }
    }

    private static PwaInstallPlatform ToPlatform(string? platform) => platform switch
    {
        "ios" => PwaInstallPlatform.Ios,
        "android" => PwaInstallPlatform.Android,
        "desktop" => PwaInstallPlatform.Desktop,
        _ => PwaInstallPlatform.Unknown,
    };

    /// <remarks>
    /// The module is imported when the tip reads its state, so by the time the
    /// user clicks "Installer" it is cached and the prompt is issued without a
    /// round-trip that would consume the user gesture.
    /// </remarks>
    private async ValueTask<IJSObjectReference> GetModuleAsync() =>
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

    private sealed record BrowserStatus(bool Installed, bool CanPrompt, string? Platform);
}
