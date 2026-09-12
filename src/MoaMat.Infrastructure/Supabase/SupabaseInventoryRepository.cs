using System.Globalization;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="IInventoryRepository"/>.
/// </summary>
/// <remarks>
/// Reads go through the <c>security_invoker</c> view <c>public.v_item</c> and
/// writes through <c>public.item</c>; in both directions the RLS policies of
/// <c>db/rls.sql</c> are the enforcing authority. Status and activation changes
/// are last-write-wins updates, so replaying one is harmless.
/// </remarks>
internal sealed class SupabaseInventoryRepository : IInventoryRepository
{
    private const string ReadFailureMessage = "Lecture de l'inventaire impossible.";

    private const string SaveRefusedMessage =
        "Enregistrement refusé (droits insuffisants ou données invalides).";

    private const string ActivationRefusedMessage =
        "Changement d'état refusé (droits insuffisants).";

    private const string StatusRefusedMessage =
        "Changement de statut refusé (droits insuffisants ou statut terminal).";

    /// <summary>PostgREST expects a textual predicate for a boolean "is" filter.</summary>
    private const string TrueLiteral = "true";

    private const string FalseLiteral = "false";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseInventoryRepository(global::Supabase.Client client, ILogger<SupabaseInventoryRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
        InventoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _guard.ReadAsync(
            nameof(GetItemsAsync),
            ReadFailureMessage,
            async () =>
            {
                var query = _client.From<ItemViewRecord>()
                    .Order("code_club", Constants.Ordering.Ascending)
                    .Order("id", Constants.Ordering.Ascending)
                    .Limit(filter.MaxResults);

                if (!string.IsNullOrWhiteSpace(filter.FamilyCode))
                {
                    query = query.Filter("famille", Constants.Operator.Equals, filter.FamilyCode);
                }

                if (!string.IsNullOrWhiteSpace(filter.StatusCode))
                {
                    query = query.Filter("statut_code", Constants.Operator.Equals, filter.StatusCode);
                }

                if (filter.ContainerId is { } containerId)
                {
                    query = query.Filter("lieu_contenant_id", Constants.Operator.Equals, containerId);
                }

                if (filter.DueOnOrBefore is { } dueDate)
                {
                    query = query.Filter(
                        "date_echeance",
                        Constants.Operator.LessThanOrEqual,
                        dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                if (filter.Activation is not ActivationScope.All)
                {
                    query = query.Filter(
                        "actif",
                        Constants.Operator.Is,
                        filter.Activation is ActivationScope.ActiveOnly ? TrueLiteral : FalseLiteral);
                }

                if (filter.AmbiguousCodesOnly)
                {
                    query = query.Filter("code_club_ambigu", Constants.Operator.Is, TrueLiteral);
                }

                var response = await query.Get(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<InventoryItem>)[.. response.Models.Select(InventoryItemMapper.ToDomain)];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<InventoryItem?> GetItemAsync(long itemId, CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetItemAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<ItemViewRecord>()
                    .Filter("id", Constants.Operator.Equals, itemId)
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var record = response.Models.FirstOrDefault();
                return record is null ? null : InventoryItemMapper.ToDomain(record);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ItemStatus>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetStatusesAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<ItemStatusRecord>()
                    .Order("ordre", Constants.Ordering.Ascending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<ItemStatus>)[.. response.Models.Select(ItemStatusMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ItemImportRejection>> GetImportRejectionsAsync(
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetImportRejectionsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<ItemRejectRecord>()
                    .Order("origine_table", Constants.Ordering.Ascending)
                    .Order("origine_id", Constants.Ordering.Ascending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<ItemImportRejection>)
                    [.. response.Models.Select(ItemImportRejectionMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<long>> CreateItemAsync(
        ItemDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return _guard.WriteValueAsync(
            nameof(CreateItemAsync),
            SaveRefusedMessage,
            async () =>
            {
                var response = await _client.From<ItemRecord>()
                    .Insert(ItemDraftMapper.ToRecord(draft), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var created = response.Models.FirstOrDefault()
                    ?? throw new DataAccessException("Création acceptée mais aucune ligne renvoyée.");

                return created.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> UpdateItemAsync(ItemDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.Id <= 0)
        {
            return Task.FromResult(OperationResult.Failure("Item inconnu : mise à jour refusée."));
        }

        return _guard.WriteAsync(
            nameof(UpdateItemAsync),
            SaveRefusedMessage,
            () => _client.From<ItemRecord>()
                .Update(ItemDraftMapper.ToRecord(draft), cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SetItemActivationAsync(
        long itemId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(SetItemActivationAsync),
            ActivationRefusedMessage,
            () => _client.From<ItemRecord>()
                .Where(record => record.Id == itemId)
                .Set(record => record.Actif, isActive)
                .Update(cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> SetItemStatusAsync(
        long itemId,
        StatusTransitionRequest transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (string.IsNullOrWhiteSpace(transition.StatusCode) || string.IsNullOrWhiteSpace(transition.Reason))
        {
            return Task.FromResult(OperationResult.Failure("Statut ou motif manquant : changement refusé."));
        }

        // The four statut_* columns below travel in the SAME update as
        // statut_code: public.tg_item_valider_transition_statut (db/item_etat.sql)
        // validates them and resets them to NULL once the transition is recorded.
        return _guard.WriteAsync(
            nameof(SetItemStatusAsync),
            StatusRefusedMessage,
            () => _client.From<ItemRecord>()
                .Where(record => record.Id == itemId)
                .Set(record => record.StatutCode, transition.StatusCode)
                .Set(record => record.StatutMotif!, transition.Reason)
                .Set(record => record.StatutDateEffet!, SqlDateConverter.ToDateTime(transition.EffectiveOn))
                .Set(record => record.StatutPieceJointeUrl!, transition.AttachmentUrl)
                .Set(record => record.StatutAutorite!, ToAutoriteCode(transition.Authority))
                .Update(cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ItemStatusTransition>> GetStatusHistoryAsync(
        long itemId,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetStatusHistoryAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<ItemStatusTransitionRecord>()
                    .Filter("item_id", Constants.Operator.Equals, itemId)
                    .Order("cree_le", Constants.Ordering.Descending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<ItemStatusTransition>)[.. response.Models.Select(ItemStatusTransitionMapper.ToDomain)];
            },
            cancellationToken);

    /// <summary>Matches the <c>statut_autorite</c> check constraint in <c>db/model_item.sql</c>.</summary>
    private static string? ToAutoriteCode(TransitionAuthority? authority) => authority switch
    {
        TransitionAuthority.OrganismeControle => "organisme_controle",
        TransitionAuthority.Ca => "ca",
        TransitionAuthority.GestionnaireMateriel => "gestionnaire_materiel",
        _ => null,
    };
}
