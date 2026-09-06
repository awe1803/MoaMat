namespace MoaMat.Web.Layout;

public partial class NavMenu
{
    private bool collapseNavMenu = true;

    private string? NavMenuCssClass => collapseNavMenu ? "collapse" : null;

    private void ToggleNavMenu()
    {
        collapseNavMenu = !collapseNavMenu;
    }

    private static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var local = name.Split('@')[0];
        var parts = local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : char.ToUpperInvariant(local[0]).ToString();
    }
}
