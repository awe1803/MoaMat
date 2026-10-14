using System.Globalization;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Regulators;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="IRegulatorRepository"/>.
/// </summary>
/// <remarks>
/// Reads go through <c>public.item_detendeur</c> and <c>public.detendeur_evenement</c>
/// (RLS: <c>item.read</c>); every write goes through the <c>SECURITY DEFINER</c>
/// function of <c>db/detendeur_evenement.sql</c> — the timeline has no insert
/// policy, so this adapter only ever calls <c>.Rpc</c> to write.
/// </remarks>
internal sealed class SupabaseRegulatorRepository : IRegulatorRepository
{
    private const string ReadFailureMessage = "Lecture de la fiche détendeur impossible.";

    private const string InterventionRefusedMessage =
        "Intervention refusée (droits insuffisants ou données invalides).";

    /// <summary>
    /// Upper bound of a timeline. A regulator has a few dozen entries over its
    /// whole life; the bound only protects against a runaway table.
    /// </summary>
    private const int MaxEvents = 500;

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseRegulatorRepository(global::Supabase.Client client, ILogger<SupabaseRegulatorRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<RegulatorDetails?> GetDetailsAsync(long itemId, CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetDetailsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<RegulatorDetailRecord>()
                    .Filter("item_id", Constants.Operator.Equals, itemId)
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var record = response.Models.FirstOrDefault();
                return record is null ? null : RegulatorMapper.ToDomain(record);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RegulatorEvent>> GetEventsAsync(
        long itemId,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetEventsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client.From<RegulatorEventRecord>()
                    .Filter("item_id", Constants.Operator.Equals, itemId)
                    .Order("date_evenement", Constants.Ordering.Descending, Constants.NullPosition.Last)
                    .Order("id", Constants.Ordering.Descending)
                    .Limit(MaxEvents)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<RegulatorEvent>)[.. response.Models.Select(RegulatorMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> RecordInterventionAsync(
        RegulatorInterventionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _guard.WriteAsync(
            nameof(RecordInterventionAsync),
            InterventionRefusedMessage,
            () => _client.Rpc(
                "enregistrer_intervention_detendeur",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["p_item_id"] = request.ItemId,
                    ["p_date"] = ToDateString(request.PerformedOn),
                    ["p_ctrl_manometre"] = RegulatorMapper.ToCode(request.ControlPoints.Gauge!.Value),
                    ["p_ctrl_detendeur"] = RegulatorMapper.ToCode(request.ControlPoints.Stages!.Value),
                    ["p_ctrl_octopus"] = RegulatorMapper.ToCode(request.ControlPoints.Octopus!.Value),
                    ["p_ctrl_inflateur"] = RegulatorMapper.ToCode(request.ControlPoints.Inflator!.Value),
                    ["p_ctrl_visuel_flexibles"] = RegulatorMapper.ToCode(request.ControlPoints.HoseVisual!.Value),
                    ["p_prestataire"] = request.Provider,
                    ["p_cout_eur"] = request.CostEur,
                    ["p_remarque"] = request.Remark,
                    ["p_request_id"] = request.RequestId,
                }),
            cancellationToken);
    }

    private static string ToDateString(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
