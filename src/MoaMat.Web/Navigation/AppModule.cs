namespace MoaMat.Web.Navigation;

/// <summary>
/// One entry of the application's module catalogue: a tile on the dashboard and
/// a line in the side navigation.
/// </summary>
/// <remarks>
/// The catalogue is the single source of truth shared by the sidebar, the
/// dashboard tiles and the "coming later" screen, exactly as the mock-up's
/// <c>MODULES</c> registry was. A module that is not built yet still appears —
/// the club needs to see the whole perimeter, not only the finished part — but
/// it routes to a placeholder instead of a dead link.
/// </remarks>
public sealed record AppModule
{
    /// <summary>Stable key, used in the placeholder route.</summary>
    public required string Key { get; init; }

    /// <summary>Side navigation heading this module is filed under.</summary>
    public required string Group { get; init; }

    /// <summary>Key of the glyph in <see cref="Components.Icon"/>.</summary>
    public required string Icon { get; init; }

    /// <summary>Label shown on the tile and in the sidebar.</summary>
    public required string Title { get; init; }

    /// <summary>Static caption, used when no live count is available.</summary>
    public required string Subtitle { get; init; }

    /// <summary>
    /// Inventory family this module lists, or <c>null</c> when the module is not
    /// a view over the inventory. Drives both the deep link and the live count.
    /// </summary>
    public string? FamilyCode { get; init; }

    /// <summary>Route the module opens; the placeholder route when it is not built yet.</summary>
    public required string Href { get; init; }

    /// <summary>Authorization policy gating the module, or <c>null</c> for any signed-in user.</summary>
    public string? Policy { get; init; }

    /// <summary>False when the screen is still a placeholder.</summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>Roadmap tag shown on the placeholder screen ("phase 2"...).</summary>
    public string? PlannedTag { get; init; }

    /// <summary>What the module will cover, shown on the placeholder screen.</summary>
    public string? PlannedText { get; init; }
}
