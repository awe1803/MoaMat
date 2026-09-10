using System.Globalization;

namespace MoaMat.Web.Presentation;

/// <summary>
/// Renders the instants shown on the administration screens.
/// </summary>
/// <remarks>
/// Audit and account timestamps stay in UTC with an ISO layout, deliberately
/// against the French culture used elsewhere: these values are compared with
/// database rows and pasted into incident reports, where an unambiguous,
/// sortable form matters more than a local reading.
/// </remarks>
public static class Timestamps
{
    /// <summary>Day and minute, in UTC, or a dash when absent.</summary>
    /// <param name="instant">Instant to render.</param>
    public static string Minute(DateTimeOffset? instant) =>
        instant?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Day, minute and second, in UTC.</summary>
    /// <param name="instant">Instant to render.</param>
    public static string Second(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Day only, in UTC.</summary>
    /// <param name="instant">Instant to render.</param>
    public static string Day(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
