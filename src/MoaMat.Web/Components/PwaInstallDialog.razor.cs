using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoaMat.Domain.Common;
using MoaMat.Web.Pwa;

namespace MoaMat.Web.Components;

/// <summary>
/// One-off tip explaining how to add MoaMat to the home screen, shown when the
/// application opens in a browser tab rather than as an installed application.
/// </summary>
/// <remarks>
/// It stays out of the way on purpose: nothing is shown to somebody who already
/// runs the installed application, who ticked "ne plus afficher ce message" -
/// stored on their account, so the answer holds for good and on every device -
/// or whose browser cannot even be asked. Closing the dialog without ticking
/// the box keeps the tip for the next visit, which is what a member who simply
/// has no time right now expects.
/// </remarks>
public partial class PwaInstallDialog : ComponentBase
{
    private IReadOnlyList<string> _steps = [];
    private bool _isOpen;
    private bool _canPrompt;
    private bool _neverAgain;
    private bool _isBusy;
    private string? _error;

    [Inject]
    private PwaInstallService Install { get; set; } = default!;

    /// <summary>
    /// Reads the state after the first render: the shell must be drawn before
    /// anything covers it, and JavaScript interop is not available earlier in a
    /// prerendered-shaped lifecycle anyway.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        PwaInstallStatus status;
        try
        {
            status = await Install.GetStatusAsync();
        }
        catch (JSException)
        {
            // A browser that cannot even report its state gets no tip: there
            // would be nothing useful to tell the user anyway.
            return;
        }
        catch (DataAccessException)
        {
            // The stored answer is out of reach: staying silent is the safe
            // side of the doubt - it may well be a "never again" we cannot
            // read, and the tip loses nothing by waiting for the next visit.
            return;
        }

        if (!status.ShouldOffer)
        {
            return;
        }

        _canPrompt = status.CanPrompt;
        _steps = PwaInstallInstructions.For(status.Platform);
        _isOpen = true;

        StateHasChanged();
    }

    private async Task InstallAsync()
    {
        _isBusy = true;

        try
        {
            // Nothing is awaited before this call: browsers only show their own
            // installation prompt while the click gesture is still current.
            await Install.PromptAsync();
        }
        catch (JSException)
        {
            // The browser refused to show its prompt: fall back to closing the
            // dialog, the manual path is still available from its own menu.
        }
        finally
        {
            _isBusy = false;
        }

        Close();
    }

    /// <remarks>
    /// The choice is stored as soon as the box is ticked - so it survives a tab
    /// closed on the dialog rather than on one of its buttons - and cleared
    /// again if the user changes their mind before closing it. A refused write
    /// puts the box back where it was: the screen never shows a choice the
    /// database did not keep.
    /// </remarks>
    private async Task OnNeverAgainChanged(ChangeEventArgs args)
    {
        var neverAgain = args.Value is true;

        // Set before awaiting, so the render that follows records the ticked
        // box; the revert below is then a real change the diff can undo in the
        // DOM, which it would not be had nothing been rendered in between.
        _neverAgain = neverAgain;
        _isBusy = true;
        _error = null;

        var result = await Install.SetDismissedAsync(neverAgain);

        _isBusy = false;

        if (!result.Succeeded)
        {
            _neverAgain = !neverAgain;
            _error = result.Error;
        }
    }

    private void Close() => _isOpen = false;
}
