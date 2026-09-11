using System.Collections.Frozen;
using Microsoft.AspNetCore.Components;

namespace MoaMat.Web.Components;

/// <summary>
/// Renders one icon of the MOANA design language as an inline SVG path.
/// </summary>
/// <remarks>
/// The catalogue is a literal transcription of the mock-up's <c>ICONS</c> map.
/// Drawing the glyph inline — rather than referencing a data-URL background as
/// the Blazor template did — keeps the stroke on <c>currentColor</c>, so an
/// icon follows the light or dark palette without a single duplicated rule.
/// </remarks>
public partial class Icon : ComponentBase
{
    private static readonly FrozenDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["dashboard"] = """<path d="M4 4h7v7H4zM13 4h7v4h-7zM13 11h7v9h-7zM4 14h7v6H4z"/>""",
        ["bouteille"] = """<path d="M9 2h6M10 2v3.5c0 .8-.4 1.4-1 2C8 8.6 7 10 7 13v6a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2v-6c0-3-1-4.4-2-5.5-.6-.6-1-1.2-1-2V2"/>""",
        ["detendeur"] = """<circle cx="12" cy="13" r="7"/><path d="M12 9v4l2.5 2.5M9 3h6l-1 3H10z"/>""",
        ["gilet"] = """<path d="M8 3 5 6v14a1 1 0 0 0 1 1h4V9m6-6 3 3v14a1 1 0 0 1-1 1h-4V9M8 3h8M10 9h4"/>""",
        ["petit"] = """<path d="M4 12c0-4 3-8 8-8s8 4 8 8-3 4-8 4-8 0-8-4Z"/><path d="M4 12h16"/>""",
        ["didactique"] = """<path d="M4 5h9a3 3 0 0 1 3 3v11a2 2 0 0 0-2-2H4Z"/><path d="M20 5h-4a3 3 0 0 0-3 3v11a2 2 0 0 1 2-2h5Z"/>""",
        ["sauvetage"] = """<rect x="4" y="4" width="16" height="16" rx="3"/><path d="M12 8v8M8 12h8"/>""",
        ["prets"] = """<path d="M7 7h11l-3-3M17 17H6l3 3M7 7v10M17 17V7"/>""",
        ["achats"] = """<circle cx="9" cy="20" r="1"/><circle cx="17" cy="20" r="1"/><path d="M3 4h2l2.4 11.4A2 2 0 0 0 9.4 17h7.2a2 2 0 0 0 2-1.6L20 8H6"/>""",
        ["pieces"] = """<path d="M14.7 6.3a4 4 0 0 1-5.4 5.4L4 17l3 3 5.3-5.3a4 4 0 0 1 5.4-5.4L21 6l-3-3Z"/>""",
        ["compresseur"] = """<circle cx="12" cy="12" r="8"/><path d="M12 8v4l3 2"/>""",
        ["membres"] = """<circle cx="12" cy="8" r="3"/><path d="M6 20c0-3 2.5-5 6-5s6 2 6 5"/>""",
        ["exports"] = """<path d="M12 3v12m0 0 4-4m-4 4-4-4M4 17v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3"/>""",
        ["scan"] = """<path d="M4 7V5a1 1 0 0 1 1-1h2M4 17v2a1 1 0 0 0 1 1h2M20 7V5a1 1 0 0 0-1-1h-2M20 17v2a1 1 0 0 1-1 1h-2M4 12h16"/>""",
        ["bell"] = """<path d="M6 8a6 6 0 0 1 12 0c0 4 1.5 5 1.5 5h-15S6 12 6 8Z"/><path d="M10 20a2 2 0 0 0 4 0"/>""",
        ["home"] = """<path d="m3 11 9-8 9 8"/><path d="M5 10v10h14V10"/>""",
        ["grid"] = """<rect x="4" y="4" width="7" height="7" rx="1"/><rect x="13" y="4" width="7" height="7" rx="1"/><rect x="4" y="13" width="7" height="7" rx="1"/><rect x="13" y="13" width="7" height="7" rx="1"/>""",
        ["more"] = """<circle cx="5" cy="12" r="1.6"/><circle cx="12" cy="12" r="1.6"/><circle cx="19" cy="12" r="1.6"/>""",
        ["search"] = """<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/>""",
        ["alert"] = """<path d="M12 9v4M12 17h.01M10.3 3.9 2.5 17a2 2 0 0 0 1.7 3h15.6a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/>""",
        ["chevron"] = """<path d="m9 6 6 6-6 6"/>""",
        ["back"] = """<path d="M19 12H5M11 18l-6-6 6-6"/>""",
        ["plus"] = """<path d="M12 5v14M5 12h14"/>""",
        ["refresh"] = """<path d="M20 11a8 8 0 1 0-.6 4M20 5v6h-6"/>""",
        ["lieux"] = """<path d="M12 21s7-5.7 7-11a7 7 0 1 0-14 0c0 5.3 7 11 7 11Z"/><circle cx="12" cy="10" r="2.5"/>""",
        ["comptes"] = """<circle cx="9" cy="8" r="3"/><path d="M3 20c0-3 2.5-5 6-5s6 2 6 5"/><path d="M16 5.2a3 3 0 0 1 0 5.6M18 20c0-2-.7-3.4-1.8-4.3"/>""",
        ["audit"] = """<path d="M6 3h8l4 4v14H6z"/><path d="M14 3v4h4M9 12h6M9 16h6"/>""",
        ["close"] = """<path d="M6 6l12 12M18 6 6 18"/>""",
        ["menu"] = """<path d="M4 6h16M4 12h16M4 18h16"/>""",
        ["logout"] = """<path d="M15 17l5-5-5-5M20 12H9M12 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h6"/>""",
        ["clock"] = """<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>""",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Catalogue key of the glyph to draw; an unknown key draws nothing.</summary>
    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Edge length in pixels; the glyph is always square.</summary>
    [Parameter]
    public int Size { get; set; } = 18;

    /// <summary>
    /// Inline SVG of the glyph. The markup is a compile-time constant of this
    /// class, never user input, so rendering it raw carries no injection risk.
    /// </summary>
    private MarkupString Path =>
        new(Paths.TryGetValue(Name, out var path) ? path : string.Empty);
}
