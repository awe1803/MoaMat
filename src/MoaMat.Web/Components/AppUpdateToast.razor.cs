using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoaMat.Web.Pwa;

namespace MoaMat.Web.Components;

/// <summary>
/// Discreet notice telling the user that a newly deployed version of MoaMat is
/// ready, with the reload that switches to it.
/// </summary>
/// <remarks>
/// It never reloads on its own: an installed MoaMat can stay open for days, and
/// a page that refreshes itself under the user would lose whatever is being
/// typed. "Plus tard" simply hides the notice - the new version is already
/// cached, so the next start-up picks it up whatever happens here.
/// </remarks>
public partial class AppUpdateToast : ComponentBase, IAsyncDisposable
{
    private bool _isVisible;
    private bool _isBusy;

    [Inject]
    private PwaUpdateService Update { get; set; } = default!;

    /// <summary>
    /// Starts watching after the first render: JavaScript interop is not
    /// available earlier, and the shell must be drawn before anything is laid
    /// over it.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await Update.StartWatchingAsync(OnUpdateAvailableAsync);
        }
        catch (JSException)
        {
            // A browser that cannot watch its service worker gets no notice:
            // it simply loads the new version on its next start-up.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Update.StopWatchingAsync();
        GC.SuppressFinalize(this);
    }

    /// <remarks>
    /// Called from the browser, so off the renderer's synchronisation context:
    /// the redraw has to be dispatched back onto it.
    /// </remarks>
    private Task OnUpdateAvailableAsync()
    {
        _isVisible = true;
        return InvokeAsync(StateHasChanged);
    }

    private async Task ApplyAsync()
    {
        _isBusy = true;

        try
        {
            // Does not return in the usual case: the page reloads underneath.
            await Update.ApplyAsync();
        }
        catch (JSException)
        {
            // The hand-over could not even be asked for. Hiding the notice
            // beats leaving a button that does nothing; the next start-up
            // switches to the new version anyway.
            _isBusy = false;
            _isVisible = false;
        }
    }

    private void Dismiss() => _isVisible = false;
}
