using MoaMat.Domain.Campaigns;

namespace MoaMat.UnitTests.Campaigns;

/// <summary>
/// Purely derived campaign behaviour: missing-bottle detection and total cost,
/// no I/O involved (the actual writes are exercised by <c>db/tests/campagne_tests.sql</c>).
/// </summary>
public sealed class CampaignTests
{
    private static CampaignLine Line(
        long id, DateOnly? returnedOn, decimal? actualCost = null, CampaignLineOutcome? outcome = null) => new()
    {
        Id = id,
        CampaignId = 1,
        ItemId = id,
        ReturnedOn = returnedOn,
        ActualCostEur = actualCost,
        Outcome = outcome,
    };

    [Fact]
    public void A_line_is_not_missing_while_the_campaign_has_not_been_returned_yet()
    {
        var line = Line(1, returnedOn: null);

        Assert.False(line.IsMissing(CampaignStatus.Preparation));
        Assert.False(line.IsMissing(CampaignStatus.Sent));
    }

    [Fact]
    public void A_line_never_pointed_back_is_missing_once_the_campaign_is_returned()
    {
        var line = Line(1, returnedOn: null);

        Assert.True(line.IsMissing(CampaignStatus.Returned));
    }

    [Fact]
    public void A_line_pointed_back_is_never_missing()
    {
        var line = Line(1, returnedOn: new DateOnly(2026, 1, 15));

        Assert.False(line.IsMissing(CampaignStatus.Returned));
    }

    [Fact]
    public void MissingLines_reports_only_the_unpointed_lines_of_a_returned_campaign()
    {
        var campaign = new Campaign
        {
            Id = 1,
            Provider = "Apragaz",
            Status = CampaignStatus.Returned,
            Lines =
            [
                Line(1, returnedOn: new DateOnly(2026, 1, 15), actualCost: 20m),
                Line(2, returnedOn: null),
            ],
        };

        var missing = Assert.Single(campaign.MissingLines);
        Assert.Equal(2, missing.Id);
    }

    [Fact]
    public void TotalCostEur_is_null_until_at_least_one_line_has_an_actual_cost()
    {
        var campaign = new Campaign
        {
            Id = 1,
            Provider = "Apragaz",
            Status = CampaignStatus.Sent,
            Lines = [Line(1, returnedOn: null)],
        };

        Assert.Null(campaign.TotalCostEur);
    }

    [Fact]
    public void TotalCostEur_stays_null_while_any_line_is_still_missing_a_cost()
    {
        // A missing/unpointed bottle's real cost is unknown, not zero: summing
        // the other lines would show a total that looks final while it is
        // actually understated.
        var campaign = new Campaign
        {
            Id = 1,
            Provider = "Apragaz",
            Status = CampaignStatus.Returned,
            Lines =
            [
                Line(1, returnedOn: new DateOnly(2026, 1, 15), actualCost: 19.90m),
                Line(2, returnedOn: new DateOnly(2026, 1, 15), actualCost: 45.92m),
                Line(3, returnedOn: null),
            ],
        };

        Assert.Null(campaign.TotalCostEur);
    }

    [Fact]
    public void TotalCostEur_sums_every_line_once_all_have_an_actual_cost()
    {
        var campaign = new Campaign
        {
            Id = 1,
            Provider = "Apragaz",
            Status = CampaignStatus.Returned,
            Lines =
            [
                Line(1, returnedOn: new DateOnly(2026, 1, 15), actualCost: 19.90m),
                Line(2, returnedOn: new DateOnly(2026, 1, 15), actualCost: 45.92m),
            ],
        };

        Assert.Equal(65.82m, campaign.TotalCostEur);
    }

    [Fact]
    public void RequiresManualFollowUp_is_false_for_a_passed_or_unpointed_line()
    {
        Assert.False(Line(1, returnedOn: null).RequiresManualFollowUp);
        Assert.False(Line(1, returnedOn: new DateOnly(2026, 1, 15), outcome: CampaignLineOutcome.Passed).RequiresManualFollowUp);
    }

    [Fact]
    public void RequiresManualFollowUp_is_true_for_a_failed_line()
    {
        var line = Line(1, returnedOn: new DateOnly(2026, 1, 15), outcome: CampaignLineOutcome.Failed);

        Assert.True(line.RequiresManualFollowUp);
    }

    [Fact]
    public void A_failed_line_is_never_reported_as_missing()
    {
        // It WAS accounted for at the pointing session, just condemned — a
        // different situation from a bottle that was simply never pointed.
        var line = Line(1, returnedOn: new DateOnly(2026, 1, 15), outcome: CampaignLineOutcome.Failed);

        Assert.False(line.IsMissing(CampaignStatus.Returned));
    }
}
