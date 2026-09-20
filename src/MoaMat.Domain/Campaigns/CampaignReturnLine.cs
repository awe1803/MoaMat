namespace MoaMat.Domain.Campaigns;

/// <summary>
/// One bottle being pointed back as part of a grouped return, as submitted to
/// <see cref="ICampaignRepository.RecordReturnAsync"/>.
/// </summary>
/// <param name="LineId">Identifier of the <see cref="CampaignLine"/> being pointed.</param>
/// <param name="ReturnedOn">Date this bottle was actually returned.</param>
/// <param name="ActualCostEur">
/// Cost billed for this bottle. Deliberately <b>nullable rather than defaulted
/// to zero</b>: the database refuses a line with no cost or a negative one
/// (<c>db/campagne.sql</c>) — an unentered cost must surface as a blocking
/// error, never silently bill the bottle as free.
/// </param>
/// <param name="CertificateNumber">Certificate number issued for this bottle.</param>
/// <param name="Outcome">
/// Requalification result. No default: the manager must explicitly say
/// whether the bottle passed or was condemned — see
/// <see cref="CampaignLineOutcome"/> for why a failed bottle is never forced
/// back to "En stock".
/// </param>
/// <remarks>
/// Lines of the campaign not present in the submitted batch stay unpointed —
/// that omission is exactly how a missing bottle is detected
/// (<see cref="CampaignLine.IsMissing"/>), never a separate flag to set.
/// </remarks>
public sealed record CampaignReturnLine(
    long LineId,
    DateOnly ReturnedOn,
    decimal? ActualCostEur,
    string CertificateNumber,
    CampaignLineOutcome Outcome);
