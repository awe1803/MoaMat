using MoaMat.Domain.Inventory;

namespace MoaMat.UnitTests.Inventory;

/// <summary>
/// Ambiguous club codes are reported, never renumbered, so the badge tooltip is
/// the only explanation a user gets. It has to stay accurate, including when the
/// materialised flag is set but no sub-reason is.
/// </summary>
public sealed class InventoryItemTests
{
    private static InventoryItem ItemWith(bool ambiguous, bool duplicate, bool nonStructuring) => new()
    {
        Id = 1,
        FamilyCode = "bouteille",
        StatusCode = "en_service",
        HasAmbiguousClubCode = ambiguous,
        HasDuplicateClubCode = duplicate,
        HasNonStructuringClubCode = nonStructuring,
    };

    [Fact]
    public void A_clean_code_has_no_explanation()
    {
        var item = ItemWith(ambiguous: false, duplicate: false, nonStructuring: false);

        Assert.Null(item.DescribeClubCodeAmbiguity());
    }

    [Fact]
    public void A_duplicated_code_is_reported_as_such()
    {
        var item = ItemWith(ambiguous: true, duplicate: true, nonStructuring: false);

        Assert.Equal("code dupliqué", item.DescribeClubCodeAmbiguity());
    }

    [Fact]
    public void Both_reasons_are_listed_together()
    {
        var item = ItemWith(ambiguous: true, duplicate: true, nonStructuring: true);

        Assert.Equal("code dupliqué, code non structurant", item.DescribeClubCodeAmbiguity());
    }

    [Fact]
    public void A_flagged_code_without_a_known_reason_still_explains_itself()
    {
        var item = ItemWith(ambiguous: true, duplicate: false, nonStructuring: false);

        Assert.Equal("code ambigu", item.DescribeClubCodeAmbiguity());
    }

    [Fact]
    public void A_sub_reason_without_the_flag_stays_silent()
    {
        // The materialised flag is the authority: the view computes it, and a
        // sub-reason alone must not raise a badge the database did not raise.
        var item = ItemWith(ambiguous: false, duplicate: true, nonStructuring: true);

        Assert.Null(item.DescribeClubCodeAmbiguity());
    }
}
