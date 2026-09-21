using MoaMat.Domain.Cylinders;

namespace MoaMat.UnitTests.Cylinders;

public sealed class CylinderCorrectionRequestTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reason_is_mandatory(string? reason)
    {
        var result = CylinderCorrectionRequest.Create(1, "plongee", "acier", null, null, null, null, null, null, reason, Today);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_future_control_date_is_refused()
    {
        var result = CylinderCorrectionRequest.Create(
            1, "plongee", "acier", null, null, null, null, Today.AddDays(1), null, "erreur", Today);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void An_earlier_control_date_is_accepted_and_blank_codes_become_null()
    {
        var result = CylinderCorrectionRequest.Create(
            1, " ", "", null, null, "  ", null, new DateOnly(2020, 1, 1), null, "  erreur de reprise ", Today);

        Assert.True(result.Succeeded);
        var request = result.Value!;
        Assert.Null(request.UsageCode);
        Assert.Null(request.MaterialCode);
        Assert.Null(request.StatusCode);
        Assert.Equal("erreur de reprise", request.Reason);
    }
}
