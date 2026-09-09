using Microsoft.AspNetCore.Components;

namespace MoaMat.Web.Layout;

/// <summary>Side navigation. Entries are filtered by role through policies.</summary>
public partial class NavMenu : ComponentBase
{
    private bool _isCollapsed = true;

    private string? NavMenuCssClass => _isCollapsed ? "collapse" : null;

    private void ToggleNavMenu() => _isCollapsed = !_isCollapsed;
}
