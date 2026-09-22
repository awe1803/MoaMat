using System.Globalization;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;
using Supabase.Postgrest.Interfaces;

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

    private const string NullLiteral = "null";

    /// <summary>Characters stripped from free-text search: see <see cref="SanitizeSearchTerm"/>.</summary>
    private const string SearchReservedCharacters = """,()*%"\&#+""";

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
                var query = ApplyFilter(_client.From<ItemViewRecord>(), filter)
                    .Order("code_club", Constants.Ordering.Ascending)
                    .Order("id", Constants.Ordering.Ascending)
                    .Limit(filter.MaxResults);

                var response = await query.Get(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<InventoryItem>)[.. response.Models.Select(InventoryItemMapper.ToDomain)];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountItemsAsync(InventoryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _guard.ReadAsync(
            nameof(CountItemsAsync),
            ReadFailureMessage,
            () => ApplyFilter(_client.From<ItemViewRecord>(), filter)
                .Count(Constants.CountType.Exact, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Translates every criterion of <paramref name="filter"/> into a PostgREST
    /// predicate, so the database — not the browser — narrows the list.
    /// </summary>
    /// <param name="query">Query on <c>public.v_item</c>.</param>
    /// <param name="filter">Criteria to apply.</param>
    private static IPostgrestTable<ItemViewRecord> ApplyFilter(IPostgrestTable<ItemViewRecord> query, InventoryFilter filter)
    {
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
            query = query.Filter("date_echeance", Constants.Operator.LessThanOrEqual, ToDateString(dueDate));
        }

        query = ApplyDueBand(query, filter);

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

        return ApplySearch(query, filter.SearchText);
    }

    /// <summary>
    /// Validity band as a window on <c>date_echeance</c>; the boundaries are the
    /// ones of <see cref="InventoryItem.DueStatusOn"/>, so the list, the badge
    /// and the chip counters cannot disagree.
    /// </summary>
    private static IPostgrestTable<ItemViewRecord> ApplyDueBand(IPostgrestTable<ItemViewRecord> query, InventoryFilter filter)
    {
        var today = filter.ReferenceDay;
        var horizon = ToDateString(today.AddDays(InventoryItem.DueSoonHorizonInDays));

        switch (filter.DueBand)
        {
            case DueStatus.Overdue:
                return query.Filter("date_echeance", Constants.Operator.LessThan, ToDateString(today));
            case DueStatus.DueSoon:
                return query
                    .Filter("date_echeance", Constants.Operator.GreaterThanOrEqual, ToDateString(today))
                    .Filter("date_echeance", Constants.Operator.LessThanOrEqual, horizon);
            case DueStatus.Valid:
                return query.Filter("date_echeance", Constants.Operator.GreaterThan, horizon);
            case DueStatus.Unknown:
                return query.Filter("date_echeance", Constants.Operator.Is, NullLiteral);
            default:
                return query;
        }
    }

    /// <summary>Free text as one <c>or</c> group of case-insensitive substring predicates.</summary>
    private static IPostgrestTable<ItemViewRecord> ApplySearch(IPostgrestTable<ItemViewRecord> query, string? searchText)
    {
        var term = SanitizeSearchTerm(searchText);
        if (term.Length == 0)
        {
            return query;
        }

        var pattern = $"*{term}*";

        return query.Or(
        [
            new QueryFilter("code_club", Constants.Operator.ILike, pattern),
            new QueryFilter("num_serie", Constants.Operator.ILike, pattern),
            new QueryFilter("marque", Constants.Operator.ILike, pattern),
            new QueryFilter("modele", Constants.Operator.ILike, pattern),
            new QueryFilter("lieu_chemin", Constants.Operator.ILike, pattern),
        ]);
    }

    /// <summary>
    /// Removes the characters that carry meaning inside a PostgREST <c>or</c>
    /// group (separators, grouping, wildcards, quoting, URL escapes): user text
    /// must never be able to alter the structure of the filter.
    /// </summary>
    /// <param name="searchText">Text typed by the user.</param>
    internal static string SanitizeSearchTerm(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return string.Empty;
        }

        var kept = searchText.Where(character =>
            !char.IsControl(character) && !SearchReservedCharacters.Contains(character));

        return new string([.. kept]).Trim();
    }

    private static string ToDateString(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);


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
