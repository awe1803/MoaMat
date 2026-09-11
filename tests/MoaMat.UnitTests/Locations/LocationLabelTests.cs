using MoaMat.Domain.Locations;

namespace MoaMat.UnitTests.Locations;

/// <summary>
/// The locations screen used to send a trimmed empty string straight to the
/// database, creating a blank container. The value object now refuses it before
/// any request leaves the browser.
/// </summary>
public sealed class LocationLabelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryCreate_refuses_a_blank_label(string? candidate)
    {
        var result = LocationLabel.TryCreate(candidate, out var label);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(default, label);
    }

    [Fact]
    public void TryCreate_refuses_a_label_longer_than_the_column()
    {
        var candidate = new string('x', LocationLabel.MaxLength + 1);

        var result = LocationLabel.TryCreate(candidate, out _);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void TryCreate_accepts_a_label_of_exactly_the_maximum_length()
    {
        var candidate = new string('x', LocationLabel.MaxLength);

        var result = LocationLabel.TryCreate(candidate, out var label);

        Assert.True(result.Succeeded);
        Assert.Equal(candidate, label.Value);
    }

    [Fact]
    public void TryCreate_trims_the_accepted_label()
    {
        var result = LocationLabel.TryCreate("  Local technique  ", out var label);

        Assert.True(result.Succeeded);
        Assert.Equal("Local technique", label.Value);
    }
}
