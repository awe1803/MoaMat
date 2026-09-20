namespace MoaMat.Domain.Campaigns;

/// <summary>
/// The three fixed steps of a requalification campaign's lifecycle, mirroring
/// <c>public.campagne.statut</c> (<c>db/campagne.sql</c>).
/// </summary>
/// <remarks>
/// Unlike <see cref="MoaMat.Domain.Inventory.ItemStatus"/>, this is not an
/// administrable catalogue: a campaign always goes through the same three
/// steps in the same order, so a plain enum is enough.
/// </remarks>
public enum CampaignStatus
{
    /// <summary>Bottles selected, service types being filled in, not yet sent (<c>preparation</c>).</summary>
    Preparation = 0,

    /// <summary>Bordereau generated, bottles sent to the provider (<c>envoyee</c>).</summary>
    Sent = 1,

    /// <summary>Return pointed, at least once (<c>retournee</c>).</summary>
    Returned = 2,
}
