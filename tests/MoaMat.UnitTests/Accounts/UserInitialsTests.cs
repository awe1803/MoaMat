using MoaMat.Domain.Accounts;

namespace MoaMat.UnitTests.Accounts;

/// <summary>
/// The previous implementation indexed the local part directly and crashed on
/// an address with no local part, such as "@club.be". These cases pin the
/// defensive behaviour.
/// </summary>
public sealed class UserInitialsTests
{
    [Theory]
    [InlineData("jean.dupont@club.be", "JD")]
    [InlineData("jean-luc@club.be", "JL")]
    [InlineData("jean_luc_marie@club.be", "JL")]
    [InlineData("Jean Dupont", "JD")]
    [InlineData("plongeur@club.be", "P")]
    [InlineData("a@b.c", "A")]
    public void FromDisplayName_takes_up_to_two_initials(string displayName, string expected)
    {
        Assert.Equal(expected, UserInitials.FromDisplayName(displayName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@club.be")]
    [InlineData("...")]
    [InlineData("---@club.be")]
    public void FromDisplayName_falls_back_when_nothing_usable_is_present(string? displayName)
    {
        Assert.Equal("?", UserInitials.FromDisplayName(displayName));
    }

    [Fact]
    public void FromDisplayName_uses_a_digit_when_the_word_starts_with_one()
    {
        Assert.Equal("2D", UserInitials.FromDisplayName("2000.dupont@club.be"));
    }
}
