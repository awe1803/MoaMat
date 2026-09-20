using MoaMat.Domain.Common;

namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Port giving access to what is specific to a cylinder: its characteristics
/// and its timeline (<c>db/bouteille_evenement.sql</c>).
/// </summary>
/// <remarks>
/// Writes go through <c>SECURITY DEFINER</c> functions — the timeline table has
/// no insert policy — and are <b>not</b> idempotent: every call appends one
/// entry, so callers must not retry blindly.
/// </remarks>
public interface ICylinderRepository
{
    /// <summary>Characteristics of one cylinder, or <c>null</c> when it is not a cylinder or not visible.</summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<CylinderDetails?> GetDetailsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>Timeline of one cylinder, most recent first (undated legacy rows last).</summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<CylinderEvent>> GetEventsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>Records a requalification. A passed one renews the control counter; a failed one never does.</summary>
    /// <param name="request">The requalification.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> RecordRequalificationAsync(
        RequalificationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reports an incident on a cylinder. Never changes its status.</summary>
    /// <param name="itemId">Cylinder identifier.</param>
    /// <param name="occurredOn">Day of the incident.</param>
    /// <param name="description">What happened.</param>
    /// <param name="requestId">Idempotency key: a retry with the same key records one incident.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> ReportIncidentAsync(
        long itemId,
        DateOnly occurredOn,
        string description,
        Guid requestId,
        CancellationToken cancellationToken = default);
}
