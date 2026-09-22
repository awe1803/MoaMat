using Microsoft.JSInterop;

namespace MoaMat.Web.Pwa;

/// <summary>
/// Reports that a newly deployed version of MoaMat is ready, and hands the page
/// over to it on demand. The browser side lives in
/// <c>wwwroot/js/pwa-update.js</c>, the hand-over itself in
/// <c>wwwroot/service-worker-update.js</c>.
/// </summary>
/// <remarks>
/// A published MoaMat is served out of the service worker cache, so a
/// deployment only reaches the user once the new worker takes over - and taking
/// it over means reloading the page. That reload is never decided here: it
/// would throw away a half-filled form. The user is told, and picks the moment.
/// </remarks>
internal sealed class PwaUpdateService : IAsyncDisposable
{
    private const string ModulePath = "./js/pwa-update.js";

    private readonly IJSRuntime _jsRuntime;

    private IJSObjectReference? _module;
    private DotNetObjectReference<UpdateCallback>? _callback;

    /// <summary>Creates the service.</summary>
    /// <param name="jsRuntime">Browser JavaScript runtime.</param>
    public PwaUpdateService(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Starts watching for a new version. A second call is ignored: one watch
    /// is enough, and a browser reporting the same update twice would only
    /// flash the notice again.
    /// </summary>
    /// <param name="onUpdateAvailable">Called, from the browser, once a new version is ready.</param>
    public async Task StartWatchingAsync(Func<Task> onUpdateAvailable)
    {
        ArgumentNullException.ThrowIfNull(onUpdateAvailable);

        if (_callback is not null)
        {
            return;
        }

        var module = await GetModuleAsync();

        _callback = DotNetObjectReference.Create(new UpdateCallback(onUpdateAvailable));
        await module.InvokeVoidAsync("start", _callback);
    }

    /// <summary>Stops watching and releases the browser listeners.</summary>
    public async Task StopWatchingAsync()
    {
        if (_callback is null)
        {
            return;
        }

        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("stop");
            }
            catch (JSException)
            {
                // The page is already tearing down: the listeners go with it.
            }
            catch (JSDisconnectedException)
            {
                // Same, once the runtime itself is gone.
            }
        }

        _callback.Dispose();
        _callback = null;
    }

    /// <summary>
    /// Hands the page over to the waiting version and reloads it. In the usual
    /// case the returned task never completes: the browser navigates away while
    /// the call is still running.
    /// </summary>
    public async Task ApplyAsync()
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("applyUpdate");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopWatchingAsync();

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

    private async ValueTask<IJSObjectReference> GetModuleAsync() =>
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

    /// <summary>
    /// The object the browser calls back into. It only forwards: what the
    /// notice does with the news belongs to the component that asked for it.
    /// </summary>
    private sealed class UpdateCallback
    {
        private readonly Func<Task> _onUpdateAvailable;

        public UpdateCallback(Func<Task> onUpdateAvailable) => _onUpdateAvailable = onUpdateAvailable;

        /// <summary>Called from <c>pwa-update.js</c> when a new version is ready.</summary>
        [JSInvokable]
        public Task OnUpdateAvailableAsync() => _onUpdateAvailable();
    }
}
