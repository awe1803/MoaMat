namespace MoaMat.Domain.Campaigns;

/// <summary>
/// A requalification campaign: a batch of bottles sent to an external
/// provider (e.g. Apragaz) and the result of their return, mirroring
/// <c>public.campagne</c> plus its <c>public.campagne_ligne</c> rows
/// (<c>db/campagne.sql</c>).
/// </summary>
/// <remarks>
/// Deliberately a read/estimation model, not a state machine: every
/// lifecycle transition and invariant (mandatory service type before
/// sending, one active campaign per bottle, cost/date validation on return,
/// idempotent replay) is enforced exclusively by the <c>SECURITY DEFINER</c>
/// functions of <c>db/campagne.sql</c>, never re-implemented here. The
/// derived properties below (<see cref="TotalCostEur"/>,
/// <see cref="MissingLines"/>, ...) are the one exception — pure, read-only
/// projections of already-persisted data, not transitions.
/// </remarks>
public sealed record Campaign
{
    /// <summary>Technical identifier of <c>public.campagne</c>.</summary>
    public required long Id { get; init; }

    /// <summary>External provider the bottles are sent to (e.g. "Apragaz").</summary>
    public required string Provider { get; init; }

    /// <summary>Current step of the campaign's lifecycle.</summary>
    public required CampaignStatus Status { get; init; }

    /// <summary>Bordereau (dispatch note) number, set when the campaign is sent.</summary>
    public string? BordereauNumber { get; init; }

    /// <summary>Date the campaign was sent to the provider.</summary>
    public DateOnly? SentOn { get; init; }

    /// <summary>
    /// Date of the most recent return-pointing session — <b>not</b> necessarily
    /// when the return was first pointed: a campaign can be pointed across
    /// several sessions (stragglers arriving late), and each session
    /// overwrites this field. There is no per-session history table; the
    /// first session's date, if it matters, has to be read from
    /// <c>public.audit_log</c> (action <c>campagne.returned</c>) instead.
    /// </summary>
    public DateOnly? ReturnedOn { get; init; }

    /// <summary>Lines of this campaign, one per bottle.</summary>
    public IReadOnlyList<CampaignLine> Lines { get; init; } = [];

    /// <summary>
    /// Total actual cost, summing every line's <see cref="CampaignLine.ActualCostEur"/>;
    /// <c>null</c> until <b>every</b> line has one, never a partial total that
    /// looks final while a bottle is still missing or unpointed — a missing
    /// bottle's real cost is unknown, not zero, so the total stays unknown too.
    /// </summary>
    public decimal? TotalCostEur =>
        Lines.Count > 0 && Lines.All(line => line.ActualCostEur is not null)
            ? Lines.Sum(line => line.ActualCostEur ?? 0m)
            : null;

    /// <summary>Lines sent but never pointed back, once the return has been recorded.</summary>
    public IEnumerable<CampaignLine> MissingLines => Lines.Where(line => line.IsMissing(Status));

    /// <summary>Lines still missing their billed service — sending is blocked while this is non-empty.</summary>
    public IEnumerable<CampaignLine> LinesMissingService => Lines.Where(line => line.ServiceType is null);

    /// <summary>
    /// Sum of every line's <see cref="CampaignLine.EstimatedCostEur"/>; <c>null</c>
    /// until every line has one resolved, same "no partial total" rule as
    /// <see cref="TotalCostEur"/>.
    /// </summary>
    public decimal? EstimatedTotalEur =>
        Lines.Count > 0 && Lines.All(line => line.EstimatedCostEur is not null)
            ? Lines.Sum(line => line.EstimatedCostEur ?? 0m)
            : null;

    /// <summary>Creation instant, maintained by the database.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update instant, maintained by the database.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
