using System.Globalization;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="ICylinderRepository"/>.
/// </summary>
/// <remarks>
/// Reads go through <c>public.item_bouteille</c> and <c>public.bouteille_evenement</c>
/// (RLS: <c>item.read</c>); every write goes through a <c>SECURITY DEFINER</c>
/// function of <c>db/bouteille_evenement.sql</c> — the timeline has no insert
/// policy, so this adapter only ever calls <c>.Rpc</c>.
/// </remarks>
internal sealed class SupabaseCylinderRepository : ICylinderRepository
{
    private const string ReadFailureMessage = "Lecture de la fiche bouteille impossible.";

    private const string RequalificationRefusedMessage =
        "Requalification refusée (droits insuffisants ou données invalides).";

    private const string IncidentRefusedMessage =
        "Signalement refusé (droits insuffisants ou description manquante).";

    private const string CorrectionRefusedMessage =
        "Correction refusée (réservée au super-admin, motif ou données invalides).";

    /// <summary>
    /// Upper bound of a timeline. A cylinder has a few dozen entries over its
    /// whole life; the bound only protects against a runaway table.
    /// </summary>
    private const int MaxEvents = 500;

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseCylinderRepository(global::Supabase.Client client, ILogger<SupabaseCylinderRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<CylinderDetails?> GetDetailsAsync(long itemId, CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetDetailsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<CylinderDetailRecord>()
                    .Filter("item_id", Constants.Operator.Equals, itemId)
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var record = response.Models.FirstOrDefault();
                if (record is null)
                {
                    return null;
                }

                // The Access id lives on the item trunk (origine_id), not on item_bouteille.
                var origin = await _client.From<ItemViewRecord>()
                    .Select("id,origine_table,origine_id")
                    .Filter("id", Constants.Operator.Equals, itemId)
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var accessId = origin.Models.FirstOrDefault() is { OrigineTable: { } table } row
                    && table.StartsWith("bouteille", StringComparison.Ordinal)
                    ? row.OrigineId
                    : null;

                return CylinderMapper.ToDomain(record, accessId);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CylinderEvent>> GetEventsAsync(
        long itemId,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetEventsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<CylinderEventRecord>()
                    .Filter("item_id", Constants.Operator.Equals, itemId)
                    .Order("date_evenement", Constants.Ordering.Descending, Constants.NullPosition.Last)
                    .Order("id", Constants.Ordering.Descending)
                    .Limit(MaxEvents)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<CylinderEvent>)[.. response.Models.Select(CylinderMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> RecordRequalificationAsync(
        RequalificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _guard.WriteAsync(
            nameof(RecordRequalificationAsync),
            RequalificationRefusedMessage,
            () => _client.Rpc(
                "enregistrer_requalification_bouteille",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["p_item_id"] = request.ItemId,
                    ["p_type"] = CylinderMapper.ToCode(request.Control),
                    ["p_date"] = ToDateString(request.PerformedOn),
                    ["p_resultat"] = CylinderMapper.ToCode(request.Outcome),
                    ["p_prestataire"] = request.Provider,
                    ["p_cout_eur"] = request.CostEur,
                    ["p_num_certificat"] = request.CertificateNumber,
                    ["p_remarque"] = request.Remark,
                    ["p_request_id"] = request.RequestId,
                }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> ReportIncidentAsync(
        long itemId,
        DateOnly occurredOn,
        string description,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Task.FromResult(OperationResult.Failure("Description de l'incident manquante."));
        }

        return _guard.WriteAsync(
            nameof(ReportIncidentAsync),
            IncidentRefusedMessage,
            () => _client.Rpc(
                "signaler_incident_bouteille",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["p_item_id"] = itemId,
                    ["p_date"] = ToDateString(occurredOn),
                    ["p_description"] = description.Trim(),
                    ["p_request_id"] = requestId,
                }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> CorrectAsync(
        CylinderCorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _guard.WriteAsync(
            nameof(CorrectAsync),
            CorrectionRefusedMessage,
            () => _client.Rpc(
                "corriger_bouteille",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["p_item_id"] = request.ItemId,
                    ["p_famille"] = request.UsageCode,
                    ["p_matiere"] = request.MaterialCode,
                    ["p_filetage"] = request.Thread,
                    ["p_double_sortie"] = request.HasDoubleOutlet,
                    ["p_statut"] = request.StatusCode,
                    ["p_date_optique"] = request.LastOpticalControlOn is { } optical ? ToDateString(optical) : null,
                    ["p_date_hydraulique"] = request.LastHydraulicControlOn is { } hydraulic ? ToDateString(hydraulic) : null,
                    ["p_motif"] = request.Reason,
                    ["p_autorite"] = CylinderMapper.ToCode(request.Authority),
                }),
            cancellationToken);
    }

    private static string ToDateString(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
