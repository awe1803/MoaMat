using System.Text.Json;
using Microsoft.JSInterop;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace MoaMat.Web.Authentication;

/// <summary>
/// Stores the GoTrue session in the browser <c>localStorage</c>, so the user
/// stays signed in across a page reload or a PWA restart.
/// </summary>
/// <remarks>
/// Only tokens issued for the publishable (anon) key are stored, and they are
/// already scoped by the RLS policies. Every access is guarded: in a private
/// window, with site data blocked, or past the storage quota, the accessor
/// itself throws - the session then simply lives for the lifetime of the tab
/// instead of taking the application down.
/// </remarks>
internal sealed class BrowserSessionPersistence : IGotrueSessionPersistence<Session>
{
    private const string StorageKey = "moamat.supabase.session";

    private readonly IJSInProcessRuntime _jsRuntime;

    /// <summary>Creates the store over the browser JavaScript runtime.</summary>
    /// <param name="jsRuntime">
    /// The WebAssembly runtime. It is always an in-process implementation there,
    /// which is what makes the synchronous access required by GoTrue possible.
    /// </param>
    public BrowserSessionPersistence(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _jsRuntime = (IJSInProcessRuntime)jsRuntime;
    }

    /// <inheritdoc />
    public void SaveSession(Session session)
    {
        try
        {
            _jsRuntime.InvokeVoid("localStorage.setItem", StorageKey, JsonSerializer.Serialize(session));
        }
        catch (JSException)
        {
            // Storage unavailable (private mode, quota exceeded): keep the
            // session in memory for this tab rather than failing the sign-in.
        }
    }

    /// <inheritdoc />
    public void DestroySession()
    {
        try
        {
            _jsRuntime.InvokeVoid("localStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
            // Nothing to clean up if storage is unavailable; the in-memory
            // session is dropped by the caller either way.
        }
    }

    /// <inheritdoc />
    public Session? LoadSession()
    {
        try
        {
            var raw = _jsRuntime.Invoke<string?>("localStorage.getItem", StorageKey);
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.Deserialize<Session>(raw);
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            // Unreadable or stale payload: treat it as "no session" and let the
            // user sign in again, rather than crashing the start-up path.
            return null;
        }
    }

    /// <inheritdoc />
    public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        SaveSession(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DestroySessionAsync(CancellationToken cancellationToken = default)
    {
        DestroySession();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Session?> LoadSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LoadSession());
}
