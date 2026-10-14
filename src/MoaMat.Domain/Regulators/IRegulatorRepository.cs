using MoaMat.Domain.Common;

namespace MoaMat.Domain.Regulators;

/// <summary>
/// Port giving access to what is specific to a regulator: its characteristics
/// and its timeline (<c>db/detendeur_evenement.sql</c>).
/// </summary>
/// <remarks>
/// Writes go through a <c>SECURITY DEFINER</c> function — the timeline table
/// has no insert policy — and are <b>not</b> idempotent on their own: every
/// call appends one entry, so callers must not retry blindly (use
/// <see cref="RegulatorInterventionRequest.RequestId"/>).
/// </remarks>
public interface IRegulatorRepository
{
    /// <summary>Characteristics of one regulator, or <c>null</c> when it is not a regulator or not visible.</summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<RegulatorDetails?> GetDetailsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>Timeline of one regulator, most recent first (undated legacy rows last).</summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<RegulatorEvent>> GetEventsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an intervention. The 5 control points renew the revision
    /// counter (never backwards); the database recalculates the due date.
    /// </summary>
    /// <param name="request">The intervention.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> RecordInterventionAsync(
        RegulatorInterventionRequest request,
        CancellationToken cancellationToken = default);
}
