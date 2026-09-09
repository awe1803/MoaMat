using MoaMat.Domain.Navigation;

namespace MoaMat.UnitTests.Navigation;

/// <summary>
/// The return URL is fully attacker-controlled. Everything that could take a
/// user off the application must degrade to the home page.
/// </summary>
public sealed class RelativeReturnUrlTests
{
    /// <summary>A bell character: not whitespace, so trimming cannot remove it.</summary>
    private const char ControlCharacter = (char)7;

    [Theory]
    [InlineData("inventaire")]
    [InlineData("comptes?filtre=actifs")]
    [InlineData("journal-audit#recent")]
    [InlineData("lieux/12")]
    public void FromCandidate_keeps_an_application_relative_path(string candidate)
    {
        Assert.Equal(candidate, RelativeReturnUrl.FromCandidate(candidate).Value);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/absolute")]
    [InlineData("\\\\evil.example")]
    [InlineData("\\evil")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("mailto:someone@example.com")]
    public void FromCandidate_rejects_anything_that_could_leave_the_application(string candidate)
    {
        Assert.Equal(RelativeReturnUrl.Home, RelativeReturnUrl.FromCandidate(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromCandidate_treats_an_absent_value_as_the_home_page(string? candidate)
    {
        Assert.Equal(string.Empty, RelativeReturnUrl.FromCandidate(candidate).Value);
    }

    [Fact]
    public void FromCandidate_rejects_a_value_carrying_control_characters()
    {
        Assert.Equal(
            RelativeReturnUrl.Home,
            RelativeReturnUrl.FromCandidate("java\nscript:alert(1)"));

        Assert.Equal(
            RelativeReturnUrl.Home,
            RelativeReturnUrl.FromCandidate($"inven{ControlCharacter}taire"));
    }

    [Fact]
    public void FromCandidate_trims_surrounding_whitespace_before_deciding()
    {
        Assert.Equal("inventaire", RelativeReturnUrl.FromCandidate("  inventaire  ").Value);
        Assert.Equal(RelativeReturnUrl.Home, RelativeReturnUrl.FromCandidate("   //evil.example"));
    }
}
