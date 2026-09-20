namespace MoaMat.Domain.Campaigns;

/// <summary>
/// Result of a bottle's requalification, mirroring
/// <c>public.campagne_ligne.resultat</c> (<c>db/campagne.sql</c>).
/// </summary>
/// <remarks>
/// Chosen explicitly by the manager pointing the return, no default: a
/// bottle condemned at the hydraulic or optical test must never be silently
/// treated as if it passed. See <see cref="CampaignLine.RequiresManualFollowUp"/>
/// for what happens on <see cref="Failed"/> — the bottle is deliberately left
/// exactly where it is (never forced back to "En stock", never auto-recorded
/// as freshly controlled), for the gestionnaire to retire or declassify it
/// through the normal item status screen, with the reason and deciding
/// authority that transition requires.
/// </remarks>
public enum CampaignLineOutcome
{
    /// <summary>The bottle passed its requalification (<c>conforme</c>).</summary>
    Passed = 0,

    /// <summary>The bottle failed its requalification (<c>echec</c>) — condemned, not fit for service.</summary>
    Failed = 1,
}
