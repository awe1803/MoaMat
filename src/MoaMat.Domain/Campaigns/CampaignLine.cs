using MoaMat.Domain.Cylinders;

namespace MoaMat.Domain.Campaigns;

/// <summary>
/// One bottle within a <see cref="Campaign"/>, mirroring a row of
/// <c>public.campagne_ligne</c> (<c>db/campagne.sql</c>).
/// </summary>
public sealed record CampaignLine
{
    /// <summary>Technical identifier of <c>public.campagne_ligne</c>.</summary>
    public required long Id { get; init; }

    /// <summary>Campaign this line belongs to.</summary>
    public required long CampaignId { get; init; }

    /// <summary>The bottle (<c>public.item</c>) this line is about.</summary>
    public required long ItemId { get; init; }

    /// <summary>Club code of the bottle, for display without a second lookup.</summary>
    public string? ItemClubCode { get; init; }

    /// <summary>
    /// Service billed for this bottle. <c>null</c> until the manager picks it
    /// — mandatory before the campaign can be sent (no default between the two
    /// hydraulic methods, see <c>db/campagne.sql</c>).
    /// </summary>
    public CylinderServiceType? ServiceType { get; init; }

    /// <summary>Cost estimated at preparation time, resolved via <see cref="CylinderPricingEngine"/>.</summary>
    public decimal? EstimatedCostEur { get; init; }

    /// <summary>Actual cost, entered when the return is pointed.</summary>
    public decimal? ActualCostEur { get; init; }

    /// <summary>Certificate number, entered when the return is pointed.</summary>
    public string? CertificateNumber { get; init; }

    /// <summary>
    /// Requalification result, entered when the return is pointed; <c>null</c>
    /// until then. See <see cref="RequiresManualFollowUp"/> for what a
    /// <see cref="CampaignLineOutcome.Failed"/> result means for the bottle.
    /// </summary>
    public CampaignLineOutcome? Outcome { get; init; }

    /// <summary>
    /// Date this specific bottle was pointed back, or <c>null</c> while it has
    /// not been (either the campaign has not been returned yet, or this
    /// bottle is missing — see <see cref="IsMissing"/>).
    /// </summary>
    public DateOnly? ReturnedOn { get; init; }

    /// <summary>
    /// True when the campaign's return has been recorded but this particular
    /// bottle was never pointed back — sent, never accounted for. Purely
    /// derived, never stored: the manager resolves the situation manually,
    /// there is no automatic status change (ticket's acceptance decision).
    /// </summary>
    /// <param name="campaignStatus">Status of the owning <see cref="Campaign"/>.</param>
    public bool IsMissing(CampaignStatus campaignStatus) => campaignStatus == CampaignStatus.Returned && ReturnedOn is null;

    /// <summary>
    /// True once this bottle has been pointed back as <see cref="CampaignLineOutcome.Failed"/>:
    /// condemned at requalification, deliberately left exactly where it is
    /// (never forced back to "En stock", its control counter never touched) —
    /// the gestionnaire still has to retire or declassify it through the
    /// normal item status screen. A failed line is never "missing"
    /// (<see cref="IsMissing"/>): it WAS accounted for, just not fit for
    /// service, which is a different situation entirely.
    /// </summary>
    public bool RequiresManualFollowUp => Outcome == CampaignLineOutcome.Failed;
}
