using System.Globalization;

namespace MoaMat.Web.Presentation;

/// <summary>
/// Culture the interface is written in.
/// </summary>
/// <remarks>
/// The application is French-only and its dates are read by Belgian club
/// members, so the culture is pinned rather than taken from the browser: a
/// member browsing in English would otherwise get "Friday 4 September" in the
/// middle of an otherwise French screen. The lookup falls back to the invariant
/// culture, because a trimmed WebAssembly bundle can ship without ICU data and
/// a missing culture must not take the whole application down.
/// <para>
/// It is passed explicitly at every call site rather than assigned to
/// <see cref="CultureInfo.DefaultThreadCurrentCulture"/> at start-up: Blazor
/// WebAssembly loads a single ICU shard chosen from the browser culture, and
/// detects a culture switched during start-up as a fatal error unless the whole
/// globalization data set is downloaded
/// (<c>BlazorWebAssemblyLoadAllGlobalizationData</c>). Formatting per call keeps
/// the bundle small and the start-up path supported.
/// </para>
/// </remarks>
public static class AppCulture
{
    /// <summary>Culture applied to every date and number the interface renders.</summary>
    public static CultureInfo French { get; } = Resolve();

    private static CultureInfo Resolve()
    {
        try
        {
            return CultureInfo.GetCultureInfo("fr-BE");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
