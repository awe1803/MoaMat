using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Builds the single <see cref="global::Supabase.Client"/> the application uses.
/// </summary>
/// <remarks>
/// The construction is deliberately explicit rather than left to the library
/// defaults, because two of those defaults are incompatible with a WebAssembly
/// host and fail at run time in ways that are hard to diagnose. Keeping the
/// workaround here, documented, stops it from being "cleaned up" later.
/// </remarks>
public static class SupabaseClientFactory
{
    /// <summary>Creates the client from validated settings.</summary>
    /// <param name="settings">Connection settings; validated before use.</param>
    /// <param name="sessionPersistence">Where the session is stored between page loads.</param>
    /// <exception cref="InvalidOperationException">The settings are missing or unsafe.</exception>
    public static global::Supabase.Client Create(
        SupabaseSettings settings,
        IGotrueSessionPersistence<Session> sessionPersistence)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessionPersistence);

        settings.Validate();

        // supabase-csharp otherwise builds its sub-clients through factories that
        // assign HttpClientHandler.Proxy. On WebAssembly, BrowserHttpHandler
        // throws PlatformNotSupportedException from that setter, which kills the
        // Functions and Storage clients at construction time. Supplying one
        // browser-compatible HttpClient everywhere avoids the setter entirely.
        var browserHttpClient = new HttpClient();

        var options = new global::Supabase.SupabaseOptions
        {
            // The realtime client opens a WebSocket plus reconnection timers that
            // the WASM runtime cannot host: no auto-connect at start-up.
            AutoConnectRealtime = false,
            AutoRefreshToken = true,
            SessionHandler = sessionPersistence,
            HttpClient = browserHttpClient,
        };

        // Storage manages its own HttpClient instances and ignores the option
        // above, so they are supplied explicitly for the same reason.
        options.StorageClientOptions.HttpRequestClient = browserHttpClient;
        options.StorageClientOptions.HttpUploadClient = browserHttpClient;
        options.StorageClientOptions.HttpDownloadClient = browserHttpClient;

        return new global::Supabase.Client(settings.Url, settings.AnonKey, options);
    }
}
