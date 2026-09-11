using MoaMat.Infrastructure.Supabase.Mapping;

namespace MoaMat.UnitTests.Supabase;

/// <summary>
/// Due dates decide when a cylinder must be requalified, so a date must not
/// drift by a day on its way through the browser.
/// </summary>
public sealed class SqlDateConverterTests
{
    [Fact]
    public void A_date_survives_a_round_trip()
    {
        var original = new DateOnly(2026, 2, 28);

        var roundTripped = SqlDateConverter.ToDateOnly(SqlDateConverter.ToDateTime(original));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void A_converted_date_carries_no_time_and_no_zone()
    {
        var converted = SqlDateConverter.ToDateTime(new DateOnly(2026, 2, 28));

        Assert.NotNull(converted);
        Assert.Equal(TimeSpan.Zero, converted.Value.TimeOfDay);
        Assert.Equal(DateTimeKind.Unspecified, converted.Value.Kind);
    }

    [Fact]
    public void A_time_component_is_dropped_rather_than_rounded()
    {
        var value = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 2, 28), SqlDateConverter.ToDateOnly(value));
    }

    [Fact]
    public void Null_maps_to_null_in_both_directions()
    {
        Assert.Null(SqlDateConverter.ToDateOnly(null));
        Assert.Null(SqlDateConverter.ToDateTime(null));
    }
}
