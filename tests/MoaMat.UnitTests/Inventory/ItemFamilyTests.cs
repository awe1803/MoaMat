using MoaMat.Domain.Inventory;

namespace MoaMat.UnitTests.Inventory;

/// <summary>
/// The database is the source of truth for families and may gain one before the
/// client does, so an unknown code must render as itself rather than as a blank.
/// </summary>
public sealed class ItemFamilyTests
{
    [Fact]
    public void All_families_have_a_distinct_code()
    {
        var codes = ItemFamily.All.Select(family => family.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("bouteille", "Bouteilles")]
    [InlineData("piece_detachee", "Pièces détachées")]
    public void DisplayNameFor_resolves_a_known_code(string code, string expected)
    {
        Assert.Equal(expected, ItemFamily.DisplayNameFor(code));
    }

    [Fact]
    public void DisplayNameFor_falls_back_to_the_raw_code_when_unknown()
    {
        Assert.Equal("combinaison", ItemFamily.DisplayNameFor("combinaison"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bouteille")]
    public void FromCode_returns_nothing_for_an_unusable_code(string? code)
    {
        // Matching is ordinal on purpose: the code is a database discriminator,
        // not a label, so "Bouteille" is genuinely a different value.
        Assert.Null(ItemFamily.FromCode(code));
    }
}
