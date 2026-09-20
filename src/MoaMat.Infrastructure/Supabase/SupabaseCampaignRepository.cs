using System.Globalization;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="ICampaignRepository"/>.
/// </summary>
/// <remarks>
/// Reads go through <c>public.campagne</c> and the view
/// <c>public.v_campagne_ligne</c>; every write goes through one of the
/// <c>SECURITY DEFINER</c> functions of <c>db/campagne.sql</c>
/// (<c>creer_campagne</c>, <c>definir_prestation_campagne_ligne</c>,
/// <c>envoyer_campagne</c>, <c>pointer_retour_campagne</c>) — there is no
/// direct insert/update policy on either table, so this adapter never writes
/// through <c>.Insert</c>/<c>.Update</c>, only through <c>.Rpc</c>.
/// </remarks>
internal sealed class SupabaseCampaignRepository : ICampaignRepository
{
    private const string ReadFailureMessage = "Lecture des campagnes impossible.";

    /// <summary>
    /// Largest number of campaigns the list screen loads at once — a browsing
    /// surface, not an export, the same reasoning as
    /// <see cref="MoaMat.Domain.Inventory.InventoryFilter.DefaultMaxResults"/>.
    /// </summary>
    private const int MaxCampaignResults = 200;

    private const string CreateRefusedMessage =
        "Création de la campagne refusée (droits insuffisants ou sélection invalide).";

    private const string UpdateRefusedMessage =
        "Modification refusée (droits insuffisants ou campagne dans un état incompatible).";

    private const string SendRefusedMessage =
        "Envoi refusé (droits insuffisants, bordereau invalide ou prestation manquante sur une ligne).";

    private const string ReturnRefusedMessage =
        "Enregistrement du retour refusé (droits insuffisants ou campagne non envoyée).";

    private const string DeleteRefusedMessage =
        "Suppression refusée (réservée au super-admin, ou campagne introuvable).";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseCampaignRepository(global::Supabase.Client client, ILogger<SupabaseCampaignRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Campaign>> GetCampaignsAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetCampaignsAsync),
            ReadFailureMessage,
            async () =>
            {
                var campaigns = await _client.From<CampaignRecord>()
                    .Order("cree_le", Constants.Ordering.Descending)
                    .Limit(MaxCampaignResults)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var campaignIds = campaigns.Models.Select(campaign => campaign.Id).ToArray();

                // Filtered to exactly the campaigns just fetched, NOT an
                // unfiltered read of every line ever recorded, AND paginated
                // explicitly (FetchAllLinesAsync) rather than a single
                // unbounded .Get(): PostgREST silently caps an unpaginated
                // response at its configured max-rows (commonly 1000) with no
                // error — with up to MaxCampaignResults campaigns and a
                // realistic ~20 bottles each, that ceiling is not
                // theoretical. A silent truncation here would understate
                // Campaign.TotalCostEur/MissingLines with nothing to signal
                // it — exactly what filtering by campaignIds alone does not
                // protect against on its own.
                var lines = campaignIds.Length == 0
                    ? new List<CampaignLineRecord>()
                    : await FetchAllLinesAsync(campaignIds, cancellationToken).ConfigureAwait(false);

                var linesByCampaign = lines
                    .Select(CampaignMapper.ToDomain)
                    .GroupBy(line => line.CampaignId)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<CampaignLine>)[.. group]);

                return (IReadOnlyList<Campaign>)
                [
                    .. campaigns.Models.Select(record => CampaignMapper.ToDomain(
                        record,
                        linesByCampaign.GetValueOrDefault(record.Id, []))),
                ];
            },
            cancellationToken);

    /// <summary>
    /// Reads every <c>v_campagne_ligne</c> row for <paramref name="campaignIds"/>,
    /// paging explicitly with <c>.Range</c> rather than trusting a single
    /// <c>.Get()</c> to return everything — PostgREST enforces its own
    /// max-rows ceiling server-side regardless of what the client asks for,
    /// silently, with no truncation signal in the response.
    /// </summary>
    /// <param name="campaignIds">Campaigns to read lines for; never empty (checked by the caller).</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    private async Task<List<CampaignLineRecord>> FetchAllLinesAsync(
        IReadOnlyCollection<long> campaignIds,
        CancellationToken cancellationToken)
    {
        var lines = new List<CampaignLineRecord>();

        // Comfortably under any realistic PostgREST max-rows setting
        // (commonly 1000): a full page always means "there may be more",
        // never assumed to be the end.
        const int pageSize = 500;

        // MaxPages is a circuit breaker, not an expected ceiling: at
        // pageSize 500 it allows 10 000 lines, vastly more than this club's
        // realistic history. Hitting it means something is wrong (a runaway
        // query, a data anomaly) — stop and return what was read rather than
        // loop indefinitely.
        const int maxPages = 20;

        for (var page = 0; page < maxPages; page++)
        {
            var offset = page * pageSize;
            var response = await _client.From<CampaignLineRecord>()
                .Filter("campagne_id", Constants.Operator.In, campaignIds)
                // "id" ascending: without an explicit, unique-key ORDER BY,
                // PostgREST/Postgres give no guarantee that two separate
                // .Range() requests see the same row ordering — a row could
                // be skipped or returned twice across pages purely because
                // the server picked a different scan order for each request,
                // corrupting the very totals this pagination exists to
                // protect. "id" is the primary key, so it also breaks ties
                // deterministically on its own.
                .Order("id", Constants.Ordering.Ascending)
                .Range(offset, offset + pageSize - 1)
                .Get(cancellationToken)
                .ConfigureAwait(false);

            lines.AddRange(response.Models);

            if (response.Models.Count < pageSize)
            {
                break;
            }
        }

        return lines;
    }

    /// <inheritdoc />
    public Task<Campaign?> GetCampaignAsync(long campaignId, CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetCampaignAsync),
            ReadFailureMessage,
            async () =>
            {
                var campaign = await _client.From<CampaignRecord>()
                    .Filter("id", Constants.Operator.Equals, campaignId)
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var record = campaign.Models.FirstOrDefault();
                if (record is null)
                {
                    return null;
                }

                var lines = await _client.From<CampaignLineRecord>()
                    .Filter("campagne_id", Constants.Operator.Equals, campaignId)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return CampaignMapper.ToDomain(record, [.. lines.Models.Select(CampaignMapper.ToDomain)]);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CylinderPricingRule>> GetPricingRulesAsync(
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetPricingRulesAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<CylinderPricingRuleRecord>()
                    .Order("date_effet", Constants.Ordering.Ascending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<CylinderPricingRule>)
                    [.. response.Models.Select(CampaignMapper.ToDomain).OfType<CylinderPricingRule>()];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<long>> CreateCampaignAsync(
        string provider,
        IReadOnlyCollection<long> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (string.IsNullOrWhiteSpace(provider) || itemIds.Count == 0)
        {
            return Task.FromResult(OperationResult<long>.Failure("Prestataire ou sélection manquante."));
        }

        return _guard.WriteValueAsync(
            nameof(CreateCampaignAsync),
            CreateRefusedMessage,
            () => _client.Rpc<long>("creer_campagne", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_prestataire"] = provider,
                ["p_item_ids"] = itemIds.ToArray(),
            }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SetLineServiceTypeAsync(
        long lineId,
        CylinderServiceType serviceType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return _guard.WriteAsync(
            nameof(SetLineServiceTypeAsync),
            UpdateRefusedMessage,
            () => _client.Rpc(
                "definir_prestation_campagne_ligne",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["p_ligne_id"] = lineId,
                    ["p_type_prestation"] = serviceType.Code,
                }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SendCampaignAsync(
        long campaignId,
        string bordereauNumber,
        DateOnly sentOn,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bordereauNumber))
        {
            return Task.FromResult(OperationResult.Failure("Numéro de bon manquant : envoi refusé."));
        }

        return _guard.WriteAsync(
            nameof(SendCampaignAsync),
            SendRefusedMessage,
            () => _client.Rpc("envoyer_campagne", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_campagne_id"] = campaignId,
                ["p_numero_bon"] = bordereauNumber,
                ["p_date_envoi"] = ToDateString(sentOn),
            }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> RecordReturnAsync(
        long campaignId,
        DateOnly returnedOn,
        IReadOnlyCollection<CampaignReturnLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return Task.FromResult(OperationResult.Failure("Aucune bouteille pointée : retour refusé."));
        }

        return _guard.WriteAsync(
            nameof(RecordReturnAsync),
            ReturnRefusedMessage,
            () => _client.Rpc("pointer_retour_campagne", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_campagne_id"] = campaignId,
                ["p_date_retour"] = ToDateString(returnedOn),
                ["p_lignes"] = lines.Select(line => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ligne_id"] = line.LineId,
                    ["date_retour"] = ToDateString(line.ReturnedOn),
                    ["cout_reel_eur"] = line.ActualCostEur,
                    ["num_certificat"] = line.CertificateNumber,
                    ["resultat"] = CampaignMapper.ToCode(line.Outcome),
                }).ToArray(),
            }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> DeleteCampaignAsync(long campaignId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteCampaignAsync),
            DeleteRefusedMessage,
            () => _client.Rpc("supprimer_campagne", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_campagne_id"] = campaignId,
            }),
            cancellationToken);

    private static string ToDateString(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
