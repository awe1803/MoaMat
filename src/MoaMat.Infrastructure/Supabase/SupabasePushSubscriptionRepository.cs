using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Notifications;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="IPushSubscriptionRepository"/>.
/// </summary>
/// <remarks>
/// Writes go through RPCs of <c>db/notifications.sql</c>: the table has no
/// write policy. <c>public.enregistrer_abonnement_push</c> raises <c>42501</c>
/// for an account lacking the permission <c>role.assign</c>; this adapter only
/// turns that refusal into a message.
/// </remarks>
internal sealed class SupabasePushSubscriptionRepository : IPushSubscriptionRepository
{
    private const string SaveRefusedMessage =
        "Activation des notifications refusée : réservée aux comptes habilités à valider les inscriptions.";
    private const string RemoveRefusedMessage = "Désactivation des notifications impossible.";
    private const string ReadFailureMessage = "Lecture de l'abonnement aux notifications impossible.";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabasePushSubscriptionRepository(
        global::Supabase.Client client,
        ILogger<SupabasePushSubscriptionRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<OperationResult> SaveAsync(
        PushSubscription subscription,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (!subscription.IsComplete)
        {
            return Task.FromResult(OperationResult.Failure("Abonnement push incomplet renvoyé par le navigateur."));
        }

        return _guard.WriteAsync(
            nameof(SaveAsync),
            SaveRefusedMessage,
            () => _client.Rpc("enregistrer_abonnement_push", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["p_endpoint"] = subscription.Endpoint,
                ["p_p256dh"] = subscription.P256dh,
                ["p_auth"] = subscription.Auth,
                ["p_user_agent"] = userAgent,
            }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsStoredAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        return _guard.ReadAsync(
            nameof(IsStoredAsync),
            ReadFailureMessage,
            async () =>
            {
                // RLS only returns the signed-in user's rows.
                var response = await _client.From<PushSubscriptionRecord>()
                    .Select("id")
                    .Filter("endpoint", Constants.Operator.Equals, endpoint)
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return response.Models.Count > 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> RemoveAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        return _guard.WriteAsync(
            nameof(RemoveAsync),
            RemoveRefusedMessage,
            () => _client.Rpc("supprimer_abonnement_push", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_endpoint"] = endpoint,
            }),
            cancellationToken);
    }
}
