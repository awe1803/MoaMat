using Microsoft.JSInterop;

namespace MoaMat.Web.Connectivity;

/// <summary>
/// Reports whether the MoaMat server can be reached from this device. The
/// browser side lives in <c>wwwroot/js/connectivity.js</c>.
/// </summary>
/// <remarks>
/// An installed MoaMat starts from the service worker cache, so the shell draws
/// itself perfectly well with the network unplugged - and then every screen it
/// draws is empty, because the inventory lives in Supabase. Losing the network
/// is therefore not a per-query failure to report page by page: it is a state
/// of the application, which <see cref="Components.OfflineGate"/> puts on
/// screen.
/// </remarks>
internal sealed class ConnectivityService : IAsyncDisposable
{
    private const string ModulePath = "./js/connectivity.js";

    private readonly IJSRuntime _jsRuntime;
    private readonly string _probeUrl;

    private IJSObjectReference? _module;
    private DotNetObjectReference<ConnectivityCallback>? _callback;

    /// <summary>Creates the service.</summary>
    /// <param name="jsRuntime">Browser JavaScript runtime.</param>
    /// <param name="probeUrl">
    /// Address the probe tries to reach. It is the Supabase project rather than
    /// a file of our own on purpose: a same-origin request is answered from the
    /// service worker cache and would report a working connection while the
    /// device is offline.
    /// </param>
    public ConnectivityService(IJSRuntime jsRuntime, string probeUrl)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(probeUrl);
        _jsRuntime = jsRuntime;
        _probeUrl = probeUrl;
    }

    /// <summary>
    /// Starts watching the connection. A second call is ignored: one watch is
    /// enough, and the current state is read with <see cref="CheckAsync"/>
    /// rather than announced here.
    /// </summary>
    /// <param name="onConnectivityChanged">
    /// Called, from the browser, whenever the connection is lost or comes back.
    /// </param>
    public async Task StartWatchingAsync(Func<bool, Task> onConnectivityChanged)
    {
        ArgumentNullException.ThrowIfNull(onConnectivityChanged);

        if (_callback is not null)
        {
            return;
        }

        var module = await GetModuleAsync();

        _callback = DotNetObjectReference.Create(new ConnectivityCallback(onConnectivityChanged));
        await module.InvokeVoidAsync("start", _callback, _probeUrl);
    }

    /// <summary>
    /// Checks the connection now, and answers whether the server was reached.
    /// </summary>
    public async Task<bool> CheckAsync()
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<bool>("check", _probeUrl);
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
    /// The object the browser calls back into. It only forwards: what to show
    /// belongs to the component that asked for the watch.
    /// </summary>
    private sealed class ConnectivityCallback
    {
        private readonly Func<bool, Task> _onConnectivityChanged;

        public ConnectivityCallback(Func<bool, Task> onConnectivityChanged) =>
            _onConnectivityChanged = onConnectivityChanged;

        /// <summary>Called from <c>connectivity.js</c> when the connection changes.</summary>
        /// <param name="isOnline">True once the server can be reached again.</param>
        [JSInvokable]
        public Task OnConnectivityChangedAsync(bool isOnline) => _onConnectivityChanged(isOnline);
    }
}
