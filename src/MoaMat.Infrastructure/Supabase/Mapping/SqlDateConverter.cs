namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>
/// Bridges the PostgREST <c>date</c> representation (a <see cref="DateTime"/>)
/// and the domain representation (a <see cref="DateOnly"/>).
/// </summary>
/// <remarks>
/// The domain uses <see cref="DateOnly"/> so a calendar date can never pick up
/// a time component or a time-zone shift on its way through a browser.
/// </remarks>
internal static class SqlDateConverter
{
    /// <summary>Reads a nullable SQL date into the domain type.</summary>
    /// <param name="value">Value as deserialised by PostgREST.</param>
    public static DateOnly? ToDateOnly(DateTime? value) =>
        value is { } date ? DateOnly.FromDateTime(date) : null;

    /// <summary>Writes a domain date back as a SQL date at midnight, unspecified kind.</summary>
    /// <param name="value">Domain value.</param>
    public static DateTime? ToDateTime(DateOnly? value) =>
        value is { } date ? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified) : null;
}
