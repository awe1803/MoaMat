using Microsoft.AspNetCore.Components;
using MoaMat.Web.Navigation;

namespace MoaMat.Web.Pages;

/// <summary>
/// Screen shown for a module of the catalogue that is not built yet.
/// </summary>
/// <remarks>
/// The perimeter agreed with the club is wider than what is implemented, and
/// hiding the rest would make the roadmap invisible in the product. A tile that
/// leads here states what the module will cover and which phase it belongs to,
/// which is more useful than a link that does nothing.
/// </remarks>
public partial class ModulePlaceholder : ComponentBase
{
    /// <summary>Module key taken from the route.</summary>
    [Parameter]
    public string? Key { get; set; }

    /// <summary>Catalogue entry the route points at, or <c>null</c> when unknown.</summary>
    private AppModule? Module => AppModules.FromKey(Key);
}
