using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Cylinders;

namespace MoaMat.UnitTests.Cylinders;

public sealed class CylinderEventTests
{
    [Theory]
    [InlineData(CylinderEventType.OpticalControl, CampaignLineOutcome.Passed, "Contrôle optique — conforme")]
    [InlineData(CylinderEventType.HydraulicControl, CampaignLineOutcome.Failed, "Épreuve hydraulique — échec")]
    [InlineData(CylinderEventType.Commissioning, null, "Mise en service")]
    public void The_title_appends_the_outcome_when_there_is_one(
        CylinderEventType type,
        CampaignLineOutcome? outcome,
        string expected)
    {
        var entry = new CylinderEvent { Id = 1, Type = type, Outcome = outcome };

        Assert.Equal(expected, entry.Title);
    }

    [Theory]
    [InlineData(CylinderEventType.OpticalControl, null, false)]
    [InlineData(CylinderEventType.OpticalControl, CampaignLineOutcome.Passed, false)]
    [InlineData(CylinderEventType.OpticalControl, CampaignLineOutcome.Failed, true)]
    [InlineData(CylinderEventType.Withdrawn, null, true)]
    [InlineData(CylinderEventType.Scrapped, null, true)]
    [InlineData(CylinderEventType.Incident, null, true)]
    [InlineData(CylinderEventType.Commissioning, null, false)]
    public void Only_bad_news_is_adverse(CylinderEventType type, CampaignLineOutcome? outcome, bool expected)
    {
        var entry = new CylinderEvent { Id = 1, Type = type, Outcome = outcome };

        Assert.Equal(expected, entry.IsAdverse);
    }
}

public sealed class RequalificationRequestTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    private static Domain.Common.OperationResult<RequalificationRequest> Create(
        DateOnly? on = null,
        CampaignLineOutcome? outcome = CampaignLineOutcome.Passed,
        decimal? cost = null) =>
        RequalificationRequest.Create(
            1, CylinderControlType.Optical, on ?? Today, outcome, "  Apragaz ", cost, " ", null, Today, Guid.NewGuid());

    [Fact]
    public void A_valid_request_is_built_with_blank_optional_texts_dropped()
    {
        var result = Create(cost: 0m);

        Assert.True(result.Succeeded);
        Assert.Equal("Apragaz", result.Value!.Provider);
        Assert.Null(result.Value.CertificateNumber);
    }

    [Fact]
    public void The_outcome_must_be_chosen() => Assert.False(Create(outcome: null).Succeeded);

    [Fact]
    public void A_date_after_the_reference_day_is_refused() =>
        Assert.False(Create(on: Today.AddDays(1)).Succeeded);

    [Fact]
    public void Today_itself_is_accepted() => Assert.True(Create(on: Today).Succeeded);

    [Fact]
    public void A_negative_cost_is_refused() => Assert.False(Create(cost: -0.01m).Succeeded);
}
