using MoaMat.Domain.Inventory;

namespace MoaMat.UnitTests.Inventory;

/// <summary>
/// The inventory query used to be unbounded. The filter now caps the page size
/// so a screen can never turn into a full table read over a mobile connection.
/// </summary>
public sealed class InventoryFilterTests
{
    [Fact]
    public void A_default_filter_reads_the_active_inventory_with_a_bounded_page()
    {
        var filter = new InventoryFilter();

        Assert.Equal(ActivationScope.ActiveOnly, filter.Activation);
        Assert.Equal(InventoryFilter.DefaultMaxResults, filter.MaxResults);
        Assert.Null(filter.FamilyCode);
        Assert.False(filter.AmbiguousCodesOnly);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(50, 50)]
    [InlineData(100_000, InventoryFilter.MaxAllowedResults)]
    public void MaxResults_is_clamped_rather_than_rejected(int requested, int expected)
    {
        var filter = new InventoryFilter { MaxResults = requested };

        Assert.Equal(expected, filter.MaxResults);
    }
}
