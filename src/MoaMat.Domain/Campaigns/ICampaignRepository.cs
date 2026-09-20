using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;

namespace MoaMat.Domain.Campaigns;

/// <summary>
/// Port giving access to bottle requalification campaigns.
/// </summary>
/// <remarks>
/// Unlike <see cref="MoaMat.Domain.Accounts.IAccountRepository"/> and
/// <see cref="MoaMat.Domain.Inventory.IInventoryRepository"/>, idempotency is
/// <b>not</b> a blanket contract here — it varies per write, because a
/// campaign's lifecycle steps are one-shot by nature:
/// <list type="bullet">
/// <item><see cref="SetLineServiceTypeAsync"/> and <see cref="RecordReturnAsync"/>
/// are idempotent: replaying the same service assignment, or the same return
/// submission against an already-returned campaign, converges on the same
/// state.</item>
/// <item><see cref="CreateCampaignAsync"/> and <see cref="SendCampaignAsync"/>
/// are <b>not</b>: a retry creates a second campaign, or is refused outright
/// once the campaign is no longer in the state the call expects (already sent,
/// bordereau already generated). Callers must not blindly retry these on a
/// transient failure — re-check the resulting state first (e.g. via
/// <see cref="GetCampaignAsync"/>) rather than resubmitting.</item>
/// </list>
/// </remarks>
public interface ICampaignRepository
{
    /// <summary>Campaigns visible to the caller, most recently created first.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<Campaign>> GetCampaignsAsync(CancellationToken cancellationToken = default);

    /// <summary>One campaign with its lines, or <c>null</c> when it does not exist or is not visible.</summary>
    /// <param name="campaignId">Campaign identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<Campaign?> GetCampaignAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Apragaz pricing referential, for client-side estimation via <see cref="CylinderPricingEngine"/>.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<CylinderPricingRule>> GetPricingRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a campaign in <see cref="CampaignStatus.Preparation"/> with one
    /// line per selected bottle; each line's service type starts unset.
    /// </summary>
    /// <param name="provider">External provider the bottles will be sent to.</param>
    /// <param name="itemIds">Bottles to include, from the inventory's due-soon list.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult<long>> CreateCampaignAsync(
        string provider,
        IReadOnlyCollection<long> itemIds,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or corrects one line's billed service, while the campaign is still in preparation.</summary>
    /// <param name="lineId">Line to update.</param>
    /// <param name="serviceType">Service billed for this bottle.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetLineServiceTypeAsync(
        long lineId,
        CylinderServiceType serviceType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the bordereau and sends the campaign. The database refuses
    /// the call when any line is still missing its service type. Every bottle
    /// of the campaign transitions to the "En contrôle" status.
    /// </summary>
    /// <param name="campaignId">Campaign to send.</param>
    /// <param name="bordereauNumber">Dispatch note number.</param>
    /// <param name="sentOn">Date the campaign is sent.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SendCampaignAsync(
        long campaignId,
        string bordereauNumber,
        DateOnly sentOn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a grouped return: every NEWLY pointed line gets its actual
    /// cost, certificate and return date, its bottle's last-control counter
    /// is updated (recomputing its due date) and it returns to "En stock".
    /// Lines not present in <paramref name="lines"/> stay unpointed — the
    /// missing bottles the campaign then reports. <paramref name="lines"/>
    /// must not be empty: an empty batch would mark the whole campaign
    /// "retournee" with every bottle reported missing, without a single one
    /// actually pointed.
    /// </summary>
    /// <remarks>
    /// Re-submitting a line that was already pointed in an <b>earlier</b> call
    /// is a true no-op when the values match exactly (a network retry) — it
    /// touches nothing, not even re-applying the same status/counter update,
    /// so a manual correction made to that bottle in the meantime (e.g.
    /// reclassified as lost) is never silently overwritten. Re-submitting it
    /// with <b>different</b> values is refused outright: callers building
    /// <paramref name="lines"/> must therefore exclude already-pointed lines
    /// from a later, catch-up session rather than resend them with a new date
    /// — see <c>ReturnCampaign.razor.cs</c>, which locks those rows in the UI
    /// for exactly this reason.
    /// </remarks>
    /// <param name="campaignId">Campaign whose return is being pointed.</param>
    /// <param name="returnedOn">
    /// Date of this pointing session. Overwrites <see cref="Campaign.ReturnedOn"/>
    /// on the campaign — there is no per-session history, so an earlier
    /// session's date is lost once a later one is recorded.
    /// </param>
    /// <param name="lines">Bottles actually pointed back in this session; must be non-empty.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> RecordReturnAsync(
        long campaignId,
        DateOnly returnedOn,
        IReadOnlyCollection<CampaignReturnLine> lines,
        CancellationToken cancellationToken = default);
}
