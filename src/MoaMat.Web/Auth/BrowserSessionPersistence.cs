using System.Text.Json;
using Microsoft.JSInterop;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace MoaMat.Web.Auth;

/// <summary>
/// Persistance de la session Gotrue dans le <c>localStorage</c> du navigateur.
/// Permet de retrouver l'utilisateur connecté après un rechargement de page ou
/// une réouverture de la PWA. Seuls les jetons (access/refresh) émis pour la clé
/// <c>anon</c> y sont stockés — aucune information sensible côté serveur.
/// </summary>
public sealed class BrowserSessionPersistence : IGotrueSessionPersistence<Session>
{
    private const string StorageKey = "moamat.supabase.session";

    private readonly IJSInProcessRuntime _js;

    public BrowserSessionPersistence(IJSRuntime js)
    {
        // Sur WebAssembly, IJSRuntime est toujours une implémentation in-process :
        // l'accès synchrone exigé par l'interface Gotrue est donc disponible.
        _js = (IJSInProcessRuntime)js;
    }

    public void SaveSession(Session session)
    {
        try
        {
            _js.InvokeVoid("localStorage.setItem", StorageKey, JsonSerializer.Serialize(session));
        }
        catch (JSException)
        {
            // localStorage indisponible (mode privé, quota) : on ignore, la
            // session restera simplement en mémoire pour la durée de l'onglet.
        }
    }

    public void DestroySession()
    {
        try
        {
            _js.InvokeVoid("localStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
        }
    }

    public Session? LoadSession()
    {
        try
        {
            var raw = _js.Invoke<string?>("localStorage.getItem", StorageKey);
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.Deserialize<Session>(raw);
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            return null;
        }
    }

    public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        SaveSession(session);
        return Task.CompletedTask;
    }

    public Task DestroySessionAsync(CancellationToken cancellationToken = default)
    {
        DestroySession();
        return Task.CompletedTask;
    }

    public Task<Session?> LoadSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(LoadSession());
}
