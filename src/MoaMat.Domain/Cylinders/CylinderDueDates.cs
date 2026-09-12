using MoaMat.Domain.Inventory;

namespace MoaMat.Domain.Cylinders;

/// <summary>
/// The two independent requalification due dates of a cylinder, as computed
/// by <see cref="CylinderDueDateEngine"/>.
/// </summary>
/// <param name="OpticalDueOn">
/// Next visual inspection due date, or <c>null</c> when no optical control has
/// been recorded yet, or when the cylinder's profile has none (e.g. carbon).
/// </param>
/// <param name="HydraulicDueOn">
/// Next hydraulic requalification due date, under the same <c>null</c> rules
/// as <see cref="OpticalDueOn"/>, for the hydraulic control.
/// </param>
/// <remarks>
/// The two dates never influence one another: reading one never requires the
/// other to be set, matching the "two independent counters" requirement.
/// </remarks>
public sealed record CylinderDueDates(DateOnly? OpticalDueOn, DateOnly? HydraulicDueOn)
{
    /// <summary>The nearer of the two due dates, or the only one set, or <c>null</c> if neither is.</summary>
    public DateOnly? EarliestDueOn =>
        (OpticalDueOn, HydraulicDueOn) switch
        {
            (null, var hydraulic) => hydraulic,
            (var optical, null) => optical,
            (var optical, var hydraulic) => optical < hydraulic ? optical : hydraulic,
        };

    /// <summary>Reads a single counter against <paramref name="today"/>, independently of the other.</summary>
    /// <param name="controlType">Which counter to read.</param>
    /// <param name="today">Reference day, normally the user's current date.</param>
    public DueStatus DueStatusOn(CylinderControlType controlType, DateOnly today)
    {
        var dueOn = controlType == CylinderControlType.Optical ? OpticalDueOn : HydraulicDueOn;
        return DueStatusFor(dueOn, today);
    }

    /// <summary>
    /// Worst of the two counters' statuses — used to decide whether the
    /// cylinder as a whole is out of validity (<see cref="DueStatus.Overdue"/>
    /// on either counter is enough).
    /// </summary>
    /// <param name="today">Reference day, normally the user's current date.</param>
    public DueStatus OverallDueStatusOn(DateOnly today)
    {
        var optical = DueStatusFor(OpticalDueOn, today);
        var hydraulic = DueStatusFor(HydraulicDueOn, today);
        return (DueStatus)Math.Max((int)optical, (int)hydraulic);
    }

    private static DueStatus DueStatusFor(DateOnly? dueOn, DateOnly today)
    {
        if (dueOn is not { } value)
        {
            return DueStatus.Unknown;
        }

        if (value < today)
        {
            return DueStatus.Overdue;
        }

        return value <= today.AddDays(InventoryItem.DueSoonHorizonInDays)
            ? DueStatus.DueSoon
            : DueStatus.Valid;
    }
}
