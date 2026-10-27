using MoaMat.Domain.Suppliers;

namespace MoaMat.UnitTests.Suppliers;

/// <summary>
/// A supplier form must be refused with a clear message before anything is sent
/// to the database: blank name, malformed e-mail, a website that is not http(s).
/// </summary>
public sealed class SupplierDraftTests
{
    private static Domain.Common.OperationResult Create(
        string? name = "Apragaz",
        string? address = null,
        string? phone = null,
        string? email = null,
        string? contact = null,
        string? website = null) =>
        SupplierDraft.TryCreate(name, address, phone, email, contact, website, out _);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_refuses_a_blank_name(string? name)
    {
        var result = SupplierDraft.TryCreate(name, null, null, null, null, null, out var draft);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(default, draft);
    }

    [Fact]
    public void TryCreate_trims_values_and_turns_blank_optionals_into_null()
    {
        var result = SupplierDraft.TryCreate(
            "  Apragaz  ", "  Rue 1  ", "   ", " info@apragaz.be ", "", "  ", out var draft);

        Assert.True(result.Succeeded);
        Assert.Equal("Apragaz", draft.Name);
        Assert.Equal("Rue 1", draft.Address);
        Assert.Null(draft.Phone);
        Assert.Equal("info@apragaz.be", draft.Email);
        Assert.Null(draft.ContactPerson);
        Assert.Null(draft.WebsiteUrl);
    }

    [Theory]
    [InlineData("pas-un-email")]
    [InlineData("a@b")]
    [InlineData("@b.be")]
    [InlineData("a@@b.be")]
    [InlineData("a b@c.be")]
    [InlineData("a@b.")]
    public void TryCreate_refuses_a_malformed_email(string email)
    {
        Assert.False(Create(email: email).Succeeded);
    }

    [Theory]
    [InlineData("https://www.apragaz.be", "https://www.apragaz.be")]
    [InlineData("http://apragaz.be/contact", "http://apragaz.be/contact")]
    [InlineData("www.apragaz.be", "https://www.apragaz.be")]
    public void TryCreate_accepts_and_normalises_a_website(string typed, string expected)
    {
        var result = SupplierDraft.TryCreate("Apragaz", null, null, null, null, typed, out var draft);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, draft.WebsiteUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://apragaz.be")]
    [InlineData("apra gaz.be")]
    [InlineData("https://")]
    public void TryCreate_refuses_a_website_that_is_not_http(string website)
    {
        Assert.False(Create(website: website).Succeeded);
    }

    [Fact]
    public void TryCreate_refuses_a_name_longer_than_the_column()
    {
        Assert.False(Create(name: new string('x', SupplierDraft.NameMaxLength + 1)).Succeeded);
    }

    [Fact]
    public void TryCreate_accepts_a_name_of_exactly_the_maximum_length()
    {
        Assert.True(Create(name: new string('x', SupplierDraft.NameMaxLength)).Succeeded);
    }
}
