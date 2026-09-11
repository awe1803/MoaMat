using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MoaMat.Web.Pages;

namespace MoaMat.Web.Layout;

/// <summary>
/// Application shell: desktop top bar, side navigation, and the mobile top bar
/// and bottom tab bar of the MOANA mock-up.
/// </summary>
/// <remarks>
/// Which chrome is visible is decided in CSS by a media query, not here, so the
/// same markup serves both form factors and a resize needs no re-render. The
/// only piece of state the shell owns is the mobile navigation drawer.
/// </remarks>
public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private bool _isNavOpen;
    private string _query = string.Empty;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Inventory filtered on the items whose due date has passed.</summary>
    private static string OverdueHref => Inventory.DueFilterHref(Inventory.DueFilterOverdue);

    /// <inheritdoc />
    protected override void OnInitialized() =>
        Navigation.LocationChanged += OnLocationChanged;

    /// <summary>Closes the mobile drawer, which must never survive a navigation.</summary>
    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
        GC.SuppressFinalize(this);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (!_isNavOpen)
        {
            return;
        }

        _isNavOpen = false;
        StateHasChanged();
    }

    private void ToggleNav() => _isNavOpen = !_isNavOpen;

    private void CloseNav() => _isNavOpen = false;

    private void Search()
    {
        var query = _query.Trim();
        Navigation.NavigateTo(Inventory.SearchHref(query));
    }
}
