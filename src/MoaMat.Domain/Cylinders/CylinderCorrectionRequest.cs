using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;

namespace MoaMat.Domain.Cylinders;

/// <summary>
/// A super-admin correction of one cylinder record: gas type, material,
/// status and last control dates. It carries the complete TARGET state (the
/// unchanged fields are sent back as they are); the database refuses a
/// correction that changes nothing. Only <see cref="Create"/> builds one.
/// </summary>
public sealed record CylinderCorrectionRequest
{
    private CylinderCorrectionRequest()
    {
    }

    /// <summary>Cylinder being corrected.</summary>
    public long ItemId { get; private init; }

    /// <summary>Target usage code (<c>plongee</c>…), or <c>null</c> when unknown.</summary>
    public string? UsageCode { get; private init; }

    /// <summary>Target material code, or <c>null</c> when unknown.</summary>
    public string? MaterialCode { get; private init; }

    /// <summary>Target valve thread, or <c>null</c> when unknown.</summary>
    public string? Thread { get; private init; }

    /// <summary>Target double-outlet flag, or <c>null</c> when unknown.</summary>
    public bool? HasDoubleOutlet { get; private init; }

    /// <summary>Target status code, or <c>null</c> to leave the status as it is.</summary>
    public string? StatusCode { get; private init; }

    /// <summary>Deciding authority, required when the target status is terminal.</summary>
    public TransitionAuthority? Authority { get; private init; }

    /// <summary>Target date of the last optical control, or <c>null</c> when unknown.</summary>
    public DateOnly? LastOpticalControlOn { get; private init; }

    /// <summary>Target date of the last hydraulic control, or <c>null</c> when unknown.</summary>
    public DateOnly? LastHydraulicControlOn { get; private init; }

    /// <summary>Why the record is corrected — mandatory, it goes to the audit log.</summary>
    public string Reason { get; private init; } = string.Empty;

    /// <summary>Validates and builds a correction.</summary>
    /// <param name="itemId">Cylinder being corrected.</param>
    /// <param name="usageCode">Target usage code.</param>
    /// <param name="materialCode">Target material code.</param>
    /// <param name="thread">Target valve thread.</param>
    /// <param name="hasDoubleOutlet">Target double-outlet flag.</param>
    /// <param name="statusCode">Target status code, or <c>null</c> to keep the current one.</param>
    /// <param name="authority">Deciding authority, when the target status is terminal.</param>
    /// <param name="lastOpticalControlOn">Target last optical control date.</param>
    /// <param name="lastHydraulicControlOn">Target last hydraulic control date.</param>
    /// <param name="reason">Reason of the correction.</param>
    /// <param name="today">Reference day the dates are checked against.</param>
    /// <returns>The request, or the reason it was refused.</returns>
    public static OperationResult<CylinderCorrectionRequest> Create(
        long itemId,
        string? usageCode,
        string? materialCode,
        string? thread,
        bool? hasDoubleOutlet,
        string? statusCode,
        TransitionAuthority? authority,
        DateOnly? lastOpticalControlOn,
        DateOnly? lastHydraulicControlOn,
        string? reason,
        DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult<CylinderCorrectionRequest>.Failure("Indiquez le motif de la correction.");
        }

        if (lastOpticalControlOn > today || lastHydraulicControlOn > today)
        {
            return OperationResult<CylinderCorrectionRequest>.Failure(
                "Une date de contrôle ne peut pas être dans le futur.");
        }

        return OperationResult<CylinderCorrectionRequest>.Success(new CylinderCorrectionRequest
        {
            ItemId = itemId,
            UsageCode = NullIfBlank(usageCode),
            MaterialCode = NullIfBlank(materialCode),
            Thread = NullIfBlank(thread),
            HasDoubleOutlet = hasDoubleOutlet,
            StatusCode = NullIfBlank(statusCode),
            Authority = authority,
            LastOpticalControlOn = lastOpticalControlOn,
            LastHydraulicControlOn = lastHydraulicControlOn,
            Reason = reason.Trim(),
        });
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
