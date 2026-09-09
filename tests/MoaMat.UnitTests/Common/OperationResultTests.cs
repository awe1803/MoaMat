using MoaMat.Domain.Common;

namespace MoaMat.UnitTests.Common;

/// <summary>
/// A failed result is shown to the user verbatim, so an empty failure message
/// would surface as a blank alert. The type refuses to be built that way.
/// </summary>
public sealed class OperationResultTests
{
    [Fact]
    public void Success_carries_no_error()
    {
        Assert.True(OperationResult.Success.Succeeded);
        Assert.Null(OperationResult.Success.Error);
    }

    [Fact]
    public void Failure_carries_the_message()
    {
        var result = OperationResult.Failure("Refusé.");

        Assert.False(result.Succeeded);
        Assert.Equal("Refusé.", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failure_refuses_an_empty_message(string? error)
    {
        Assert.ThrowsAny<ArgumentException>(() => OperationResult.Failure(error!));
    }

    [Fact]
    public void A_default_result_is_not_a_success()
    {
        // Guards against a struct that silently reads as "succeeded" when it was
        // never assigned - the failure mode of a poorly designed result type.
        Assert.False(default(OperationResult).Succeeded);
        Assert.False(default(OperationResult<long>).Succeeded);
    }

    [Fact]
    public void A_generic_success_carries_its_value()
    {
        var result = OperationResult<long>.Success(42);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void A_generic_failure_carries_no_value()
    {
        var result = OperationResult<long>.Failure("Refusé.");

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Value);
        Assert.Equal("Refusé.", result.Error);
    }
}
