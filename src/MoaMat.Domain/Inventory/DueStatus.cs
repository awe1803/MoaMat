namespace MoaMat.Domain.Inventory;

/// <summary>
/// Where an item stands against its next inspection or requalification date.
/// </summary>
/// <remarks>
/// This is the club's day-to-day reading of the inventory: gear past its due
/// date must not be handed out, gear approaching it has to be booked in for a
/// campaign. Keeping the rule in the domain — rather than recomputing a date
/// comparison on every screen — is what stops the dashboard, the list and the
/// badge from disagreeing on the same item.
/// </remarks>
public enum DueStatus
{
    /// <summary>No due date recorded, so nothing can be asserted.</summary>
    Unknown = 0,

    /// <summary>Due date is far enough away.</summary>
    Valid = 1,

    /// <summary>Due date falls within the campaign horizon.</summary>
    DueSoon = 2,

    /// <summary>Due date has passed; the item is out of validity.</summary>
    Overdue = 3,
}
