using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MoaMat.Domain.Common;
using MoaMat.Domain.Notifications;

namespace MoaMat.Web.Notifications;

/// <summary>
/// Drives the Web Push opt-in of the current browser: the browser side lives in
/// <c>wwwroot/js/push-notifications.js</c>, the subscription is stored through
/// <see cref="IPushSubscriptionRepository"/>.
/// </summary>
/// <remarks>
/// Who may subscribe is decided by the database (permission <c>role.assign</c>);
/// the screens only hide the control from everybody else.
/// </remarks>
internal sealed class PushNotificationService : IAsyncDisposable
{
    private const string ModulePath = "./js/push-notifications.js";

    private readonly IJSRuntime _jsRuntime;
    private readonly IPushSubscriptionRepository _subscriptions;
    private readonly PushNotificationSettings _settings;
    private readonly ILogger<PushNotificationService> _logger;

    private IJSObjectReference? _module;

    /// <summary>Creates the service.</summary>
    /// <param name="jsRuntime">Browser JavaScript runtime.</param>
    /// <param name="subscriptions">Subscription store.</param>
    /// <param name="settings">Validated push settings.</param>
    /// <param name="logger">Logger for best-effort operations.</param>
    public PushNotificationService(
        IJSRuntime jsRuntime,
        IPushSubscriptionRepository subscriptions,
        PushNotificationSettings settings,
        ILogger<PushNotificationService> logger)
    {
        _jsRuntime = jsRuntime;
        _subscriptions = subscriptions;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Reads the state of this browser. The database is the source of truth:
    /// a browser subscription it no longer stores for the signed-in user -
    /// purged when the account lost the permission <c>role.assign</c>, or
    /// registered by another account - is unsubscribed rather than silently
    /// saved again, so regaining the permission never re-enables notifications
    /// without a fresh opt-in. A subscription still stored is saved again to
    /// refresh its keys (idempotent).
    /// </summary>
    public async Task<PushNotificationState> GetStateAsync()
    {
        if (!_settings.IsEnabled)
        {
            return PushNotificationState.NotConfigured;
        }

        var module = await GetModuleAsync();
        var status = await module.InvokeAsync<BrowserStatus>("getStatus");

        if (!status.Supported)
        {
            return status.RequiresInstall ? PushNotificationState.RequiresInstall : PushNotificationState.Unsupported;
        }

        if (status.Permission == "denied")
        {
            return PushNotificationState.Denied;
        }

        if (status.Permission != "granted" || status.Subscription is null)
        {
            return PushNotificationState.Disabled;
        }

        try
        {
            if (!await _subscriptions.IsStoredAsync(status.Subscription.Endpoint))
            {
                await module.InvokeAsync<string?>("unsubscribe");
                return PushNotificationState.Disabled;
            }
        }
        catch (DataAccessException exception)
        {
            // Unknown server state: report the browser state, touch nothing.
            _logger.LogWarning(exception, "Push subscription could not be checked.");
            return PushNotificationState.Enabled;
        }

        var resync = await SaveAsync(status.Subscription);
        if (!resync.Succeeded)
        {
            _logger.LogWarning("Push subscription could not be refreshed: {Error}", resync.Error);
        }

        return PushNotificationState.Enabled;
    }

    /// <summary>
    /// Asks for the permission and subscribes this browser. Call it straight
    /// from the click handler: browsers only show the permission prompt during
    /// a user gesture.
    /// </summary>
    public async Task<OperationResult> EnableAsync()
    {
        if (!_settings.IsEnabled)
        {
            return OperationResult.Failure("Notifications push non configurées pour ce déploiement.");
        }

        var module = await GetModuleAsync();
        var result = await module.InvokeAsync<SubscribeResult>("subscribe", _settings.VapidPublicKey);

        if (result.Subscription is null)
        {
            return OperationResult.Failure(result.Permission switch
            {
                "denied" => "Notifications bloquées : autorisez-les dans les réglages du navigateur pour ce site.",
                "unsupported" => "Ce navigateur ne prend pas en charge les notifications push.",
                _ => "Autorisation de notification non accordée.",
            });
        }

        var saved = await SaveAsync(result.Subscription);
        if (!saved.Succeeded)
        {
            // The database refused: do not leave a browser subscription nobody will use.
            await module.InvokeAsync<string?>("unsubscribe");
        }

        return saved;
    }

    /// <summary>Unsubscribes this browser and forgets its subscription.</summary>
    public async Task<OperationResult> DisableAsync()
    {
        var module = await GetModuleAsync();
        var endpoint = await module.InvokeAsync<string?>("unsubscribe");

        return string.IsNullOrEmpty(endpoint)
            ? OperationResult.Success
            : await _subscriptions.RemoveAsync(endpoint);
    }

    /// <summary>
    /// Best-effort clean-up at sign-out, so a shared device stops receiving
    /// notifications meant for the account that just left. Never throws.
    /// </summary>
    public async Task ForgetThisDeviceAsync()
    {
        if (!_settings.IsEnabled)
        {
            return;
        }

        try
        {
            var result = await DisableAsync();
            if (!result.Succeeded)
            {
                _logger.LogWarning("Push subscription could not be removed at sign-out: {Error}", result.Error);
            }
        }
        catch (JSException exception)
        {
            _logger.LogWarning(exception, "Push subscription could not be removed at sign-out.");
        }
    }

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

    private Task<OperationResult> SaveAsync(BrowserSubscription subscription) =>
        _subscriptions.SaveAsync(
            new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
            subscription.UserAgent);

    /// <remarks>
    /// <see cref="GetStateAsync"/> runs first on every screen offering the
    /// opt-in, so by the time the user clicks the module is cached and the
    /// permission request is issued without an intermediate network round-trip
    /// that could consume the user gesture.
    /// </remarks>
    private async ValueTask<IJSObjectReference> GetModuleAsync() =>
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

    private sealed record BrowserSubscription(string Endpoint, string P256dh, string Auth, string? UserAgent);

    private sealed record BrowserStatus(
        bool Supported,
        bool RequiresInstall,
        string Permission,
        BrowserSubscription? Subscription);

    private sealed record SubscribeResult(string Permission, BrowserSubscription? Subscription);
}
