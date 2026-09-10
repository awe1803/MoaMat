using MoaMat.Domain.Inventory;

namespace MoaMat.UnitTests.Inventory;

/// <summary>
/// Validity is what the club actually reads on every screen: the dashboard
/// counters, the chips of the inventory list and the badge on a row all call
/// <see cref="InventoryItem.DueStatusOn"/>. The boundaries below are therefore
/// the ones that decide whether a cylinder may be handed out.
/// </summary>
public sealed class DueStatusTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    private static InventoryItem ItemDue(DateOnly? dueOn) => new()
    {
        Id = 1,
        FamilyCode = "bouteille",
        StatusCode = "en_service",
        DueOn = dueOn,
    };

    [Fact]
    public void An_item_without_a_due_date_is_never_reported_as_valid()
    {
        // A missing date is missing information, not a clean bill of health.
        Assert.Equal(DueStatus.Unknown, ItemDue(null).DueStatusOn(Today));
    }

    [Fact]
    public void Yesterday_is_out_of_validity()
    {
        Assert.Equal(DueStatus.Overdue, ItemDue(Today.AddDays(-1)).DueStatusOn(Today));
    }

    [Fact]
    public void The_due_day_itself_is_still_usable()
    {
        // The gear stays usable up to and including its due date; only the day
        // after does it drop out of validity.
        Assert.Equal(DueStatus.DueSoon, ItemDue(Today).DueStatusOn(Today));
    }

    [Fact]
    public void The_last_day_of_the_horizon_is_still_coming_up()
    {
        var lastDay = Today.AddDays(InventoryItem.DueSoonHorizonInDays);

        Assert.Equal(DueStatus.DueSoon, ItemDue(lastDay).DueStatusOn(Today));
    }

    [Fact]
    public void One_day_past_the_horizon_is_plainly_valid()
    {
        var afterHorizon = Today.AddDays(InventoryItem.DueSoonHorizonInDays + 1);

        Assert.Equal(DueStatus.Valid, ItemDue(afterHorizon).DueStatusOn(Today));
    }

    [Fact]
    public void A_far_future_date_is_valid()
    {
        Assert.Equal(DueStatus.Valid, ItemDue(new DateOnly(2030, 1, 1)).DueStatusOn(Today));
    }
}
