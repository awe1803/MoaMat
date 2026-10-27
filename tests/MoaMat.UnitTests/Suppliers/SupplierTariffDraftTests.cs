using MoaMat.Domain.Suppliers;

namespace MoaMat.UnitTests.Suppliers;

/// <summary>
/// A supplier price needs a service, a non-negative amount and an effective
/// date; a change is always a new dated price, never an edit.
/// </summary>
public sealed class SupplierTariffDraftTests
{
    private static readonly DateOnly Date = new(2027, 1, 1);

    [Fact]
    public void TryCreate_accepts_a_complete_price_and_rounds_to_the_cent()
    {
        var result = SupplierTariffDraft.TryCreate(5, 21.325m, Date, out var draft);

        Assert.True(result.Succeeded);
        Assert.Equal(5, draft.ServiceId);
        Assert.Equal(21.33m, draft.PriceEur);
        Assert.Equal(Date, draft.EffectiveOn);
    }

    [Fact]
    public void TryCreate_accepts_a_free_service()
    {
        Assert.True(SupplierTariffDraft.TryCreate(5, 0m, Date, out _).Succeeded);
    }

    [Fact]
    public void TryCreate_refuses_a_missing_service()
    {
        Assert.False(SupplierTariffDraft.TryCreate(null, 10m, Date, out _).Succeeded);
    }

    [Fact]
    public void TryCreate_refuses_a_missing_price()
    {
        Assert.False(SupplierTariffDraft.TryCreate(5, null, Date, out _).Succeeded);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-50)]
    public void TryCreate_refuses_a_negative_price(double price)
    {
        var result = SupplierTariffDraft.TryCreate(5, (decimal)price, Date, out var draft);

        Assert.False(result.Succeeded);
        Assert.Equal(default, draft);
    }

    [Fact]
    public void TryCreate_refuses_a_price_beyond_the_column()
    {
        var tooMuch = SupplierTariffDraft.MaxPriceEur + 1;

        Assert.False(SupplierTariffDraft.TryCreate(5, tooMuch, Date, out _).Succeeded);
    }

    [Fact]
    public void TryCreate_refuses_a_missing_effective_date()
    {
        Assert.False(SupplierTariffDraft.TryCreate(5, 10m, null, out _).Succeeded);
    }
}
