using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoaMat.Web.Connectivity;

namespace MoaMat.Web.Components;

/// <summary>
/// Full-screen page shown while the device cannot reach the MoaMat server,
/// asking for an internet connection to be restored.
/// </summary>
/// <remarks>
/// It covers the application rather than letting it be used: an installed
/// MoaMat starts from the service worker cache, so without this the user would
/// browse a shell whose every screen is empty and whose every action fails,
/// with nothing saying why.
/// <para>
/// Nothing is reloaded when the connection comes back: the application was
/// never torn down, only covered, so a half-filled form is still there
/// underneath. The page simply steps aside and the user carries on.
/// </para>
/// </remarks>
public partial class OfflineGate : ComponentBase, IAsyncDisposable
{
    private bool _isOffline;
    private bool _isChecking;
    private bool _hasRetryFailed;

    [Inject]
    private ConnectivityService Connectivity { get; set; } = default!;

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
            await Connectivity.StartWatchingAsync(OnConnectivityChangedAsync);

            // Asked for explicitly rather than assumed: the application may
            // well have been opened with the network already down.
            var isOnline = await Connectivity.CheckAsync();
            await OnConnectivityChangedAsync(isOnline);
        }
        catch (JSException)
        {
            // A browser that cannot answer is left alone: covering a working
            // application would be worse than missing a lost connection, which
            // every screen reports in its own way anyway.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Connectivity.StopWatchingAsync();
        GC.SuppressFinalize(this);
    }

    /// <remarks>
    /// Called from the browser, so off the renderer's synchronisation context:
    /// the redraw has to be dispatched back onto it.
    /// </remarks>
    private Task OnConnectivityChangedAsync(bool isOnline)
    {
        _isOffline = !isOnline;

        if (isOnline)
        {
            _hasRetryFailed = false;
        }

        return InvokeAsync(StateHasChanged);
    }

    private async Task RetryAsync()
    {
        _isChecking = true;
        _hasRetryFailed = false;

        try
        {
            var isOnline = await Connectivity.CheckAsync();
            _isOffline = !isOnline;
            _hasRetryFailed = !isOnline;
        }
        catch (JSException)
        {
            // The check could not even be made. Treated as "still nothing":
            // the watch keeps trying on its own.
            _hasRetryFailed = true;
        }
        finally
        {
            _isChecking = false;
        }
    }
}
