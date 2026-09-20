using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Common;

namespace MoaMat.Domain.Cylinders;

/// <summary>
/// A requalification to record on one cylinder, outside of any campaign.
/// Only <see cref="Create"/> builds one, so an instance always satisfies the
/// invariants below (the database enforces the same rules again).
/// </summary>
public sealed record RequalificationRequest
{
    private RequalificationRequest()
    {
    }

    /// <summary>Cylinder being requalified.</summary>
    public long ItemId { get; private init; }

    /// <summary>Which control was carried out: optical or hydraulic.</summary>
    public CylinderControlType Control { get; private init; }

    /// <summary>Day of the control; never after the reference day it was created against.</summary>
    public DateOnly PerformedOn { get; private init; }

    /// <summary>Explicit result — no default, a condemned cylinder is never assumed to have passed.</summary>
    public CampaignLineOutcome Outcome { get; private init; }

    /// <summary>Inspection body, when given.</summary>
    public string? Provider { get; private init; }

    /// <summary>Cost billed, when given; never negative.</summary>
    public decimal? CostEur { get; private init; }

    /// <summary>Certificate number, when given.</summary>
    public string? CertificateNumber { get; private init; }

    /// <summary>Free-text remark, when given.</summary>
    public string? Remark { get; private init; }

    /// <summary>
    /// Idempotency key: submitting the same request twice (a retry after a
    /// network timeout) records one entry, not two.
    /// </summary>
    public Guid RequestId { get; private init; }

    /// <summary>Validates and builds a request.</summary>
    /// <param name="itemId">Cylinder being requalified.</param>
    /// <param name="control">Control carried out.</param>
    /// <param name="performedOn">Day of the control.</param>
    /// <param name="outcome">Explicit result, or <c>null</c> when the user has not chosen yet.</param>
    /// <param name="provider">Inspection body, optional.</param>
    /// <param name="costEur">Cost billed, optional.</param>
    /// <param name="certificateNumber">Certificate number, optional.</param>
    /// <param name="remark">Remark, optional.</param>
    /// <param name="today">Reference day the date is checked against.</param>
    /// <param name="requestId">Idempotency key generated when the form was opened.</param>
    /// <returns>The request, or the reason it was refused.</returns>
    public static OperationResult<RequalificationRequest> Create(
        long itemId,
        CylinderControlType control,
        DateOnly performedOn,
        CampaignLineOutcome? outcome,
        string? provider,
        decimal? costEur,
        string? certificateNumber,
        string? remark,
        DateOnly today,
        Guid requestId)
    {
        if (outcome is null)
        {
            return OperationResult<RequalificationRequest>.Failure(
                "Choisissez explicitement le résultat : conforme ou échec.");
        }

        if (performedOn > today)
        {
            return OperationResult<RequalificationRequest>.Failure(
                "La date du contrôle ne peut pas être dans le futur.");
        }

        if (costEur is < 0)
        {
            return OperationResult<RequalificationRequest>.Failure("Le coût ne peut pas être négatif.");
        }

        return OperationResult<RequalificationRequest>.Success(new RequalificationRequest
        {
            ItemId = itemId,
            Control = control,
            PerformedOn = performedOn,
            Outcome = outcome.Value,
            Provider = NullIfBlank(provider),
            CostEur = costEur,
            CertificateNumber = NullIfBlank(certificateNumber),
            Remark = NullIfBlank(remark),
            RequestId = requestId,
        });
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
