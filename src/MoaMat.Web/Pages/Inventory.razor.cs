using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;
using MoaMat.Domain.Locations;
using MoaMat.Web.Presentation;

namespace MoaMat.Web.Pages;

/// <summary>
/// Inventory browsing screen: the unified gear list with its validity badge,
/// storage path and club code ambiguity flag.
/// </summary>
/// <remarks>
/// <para>The family, the validity chip and the search text live in the query
/// string rather than in fields. That is what lets a dashboard tile, the alert
/// banner and the top bar search open this screen already filtered, and what
/// makes the back button of the browser behave.</para>
/// <para>Family, status, location and activation are pushed down to the
/// database; validity and free text are applied on the page. Validity is a
/// window around today that the repository filter cannot express, and the free
/// text search spans five columns — both would otherwise cost a round trip per
/// keystroke for a list the client already holds.</para>
/// </remarks>
public partial class Inventory : ComponentBase
{
    /// <summary>Chip value listing the items whose due date has passed.</summary>
    public const string DueFilterOverdue = "depassee";

    /// <summary>Chip value listing the items due within the campaign horizon.</summary>
    public const string DueFilterSoon = "proche";

    /// <summary>Chip value listing the items still comfortably valid.</summary>
    public const string DueFilterValid = "valide";

    private const string RoutePath = "inventaire";

    private readonly List<InventoryItem> _items = [];

    private IReadOnlyList<InventoryItem> _visible = [];

    private IReadOnlyList<ItemStatus> _statuses = [];
    private IReadOnlyList<LocationPath> _containers = [];

    // Bound filter fields. They are kept separate from InventoryFilter because
    // the filter is an immutable domain record, rebuilt on every reload.
    private string _statusCode = string.Empty;
    private string _containerId = string.Empty;
    private ActivationScope _activation = ActivationScope.ActiveOnly;
    private bool _ambiguousCodesOnly;

    private string? _loadedFamily;
    private bool _isBusy = true;
    private string? _error;

    /// <summary>Family the list is restricted to, from the <c>famille</c> query parameter.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "famille")]
    public string? Family { get; set; }

    /// <summary>Validity chip, from the <c>echeance</c> query parameter.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "echeance")]
    public string? Due { get; set; }

    /// <summary>Free text, from the <c>q</c> query parameter.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "q")]
    public string? Search { get; set; }

    [Inject]
    private IInventoryRepository Items { get; set; } = default!;

    [Inject]
    private ILocationRepository Locations { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Reference day for every validity badge on this screen.</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Screen title: the family being browsed, or the whole inventory.</summary>
    private string PageHeading =>
        Family is null ? "Inventaire" : ItemFamily.DisplayNameFor(Family);

    /// <summary>
    /// Rows left once the validity chip and the free text are applied. Computed
    /// by <see cref="RefreshView"/> rather than on read: the markup consults it
    /// several times per render, and re-filtering a thousand rows each time
    /// would be paid on every keystroke.
    /// </summary>
    private IReadOnlyList<InventoryItem> VisibleItems => _visible;

    /// <summary>Link opening the inventory on one validity chip.</summary>
    /// <param name="due">Chip value, or <c>null</c> for every item.</param>
    public static string DueFilterHref(string? due) =>
        due is null ? RoutePath : $"{RoutePath}?echeance={Uri.EscapeDataString(due)}";

    /// <summary>Link opening the inventory on a free text search.</summary>
    /// <param name="search">Text typed by the user; may be empty.</param>
    public static string SearchHref(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? RoutePath
            : $"{RoutePath}?q={Uri.EscapeDataString(search.Trim())}";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _statuses = await Items.GetStatusesAsync();
            _containers = await Locations.GetContainerPathsAsync();
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only a family change reaches the database: the validity chip and the
    /// search text narrow the list the page already holds.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (_isBusy || !string.Equals(_loadedFamily, Family, StringComparison.Ordinal))
        {
            await ReloadAsync();
            return;
        }

        RefreshView();
    }

    private async Task ReloadAsync()
    {
        _isBusy = true;
        _error = null;
        _loadedFamily = Family;
        StateHasChanged();

        try
        {
            var items = await Items.GetItemsAsync(BuildFilter());
            _items.Clear();
            _items.AddRange(items);
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _isBusy = false;
            RefreshView();
        }
    }

    /// <summary>Reapplies the validity chip and the free text to the loaded page.</summary>
    private void RefreshView() =>
        _visible = _items.Where(MatchesDueFilter).Where(MatchesSearch).ToArray();

    private InventoryFilter BuildFilter() => new()
    {
        FamilyCode = string.IsNullOrWhiteSpace(Family) ? null : Family,
        StatusCode = string.IsNullOrWhiteSpace(_statusCode) ? null : _statusCode,
        ContainerId = long.TryParse(_containerId, out var containerId) ? containerId : null,
        Activation = _activation,
        AmbiguousCodesOnly = _ambiguousCodesOnly,
        MaxResults = InventoryFilter.MaxAllowedResults,
    };

    private bool MatchesDueFilter(InventoryItem item) => Due switch
    {
        DueFilterOverdue => item.DueStatusOn(Today) == DueStatus.Overdue,
        DueFilterSoon => item.DueStatusOn(Today) == DueStatus.DueSoon,
        DueFilterValid => item.DueStatusOn(Today) == DueStatus.Valid,
        _ => true,
    };

    private bool MatchesSearch(InventoryItem item)
    {
        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }

        var needle = Search.Trim();

        return Contains(item.ClubCode, needle)
            || Contains(item.SerialNumber, needle)
            || Contains(item.Brand, needle)
            || Contains(item.Model, needle)
            || Contains(item.LocationPath, needle);
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Number of loaded items matching one validity chip.</summary>
    /// <param name="due">Chip value, or <c>null</c> for every item.</param>
    private int CountFor(string? due)
    {
        var today = Today;

        return due switch
        {
            DueFilterOverdue => _items.Count(item => item.DueStatusOn(today) == DueStatus.Overdue),
            DueFilterSoon => _items.Count(item => item.DueStatusOn(today) == DueStatus.DueSoon),
            DueFilterValid => _items.Count(item => item.DueStatusOn(today) == DueStatus.Valid),
            _ => _items.Count,
        };
    }

    /// <summary>Rewrites the current URL with one query parameter changed.</summary>
    /// <param name="name">Query parameter name.</param>
    /// <param name="value">New value, or <c>null</c> to drop the parameter.</param>
    private void Go(string name, string? value) =>
        Navigation.NavigateTo(
            Navigation.GetUriWithQueryParameter(name, string.IsNullOrWhiteSpace(value) ? null : value));

    private void OnSearchInput(ChangeEventArgs args) => Go("q", args.Value as string);

    /// <summary>Short due date, or "échue" once the date has passed.</summary>
    /// <param name="dueOn">Due date of the item.</param>
    private static string FormatDue(DateOnly? dueOn) => dueOn switch
    {
        null => "—",
        { } date when date < Today => "échue",
        { } date => date.ToString("dd/MM/yy", AppCulture.French),
    };

    private async Task ToggleActivationAsync(InventoryItem item)
    {
        _error = null;
        var result = await Items.SetItemActivationAsync(item.Id, isActive: !item.IsActive);

        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        await ReloadAsync();
    }

    /// <summary>Joins brand and model, falling back to the family name when both are empty.</summary>
    /// <param name="item">Item being described.</param>
    private static string DescribeModel(InventoryItem item)
    {
        var joined = string.Join(
            ' ',
            new[] { item.Brand, item.Model }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return joined.Length == 0 ? ItemFamily.DisplayNameFor(item.FamilyCode) : joined;
    }

    /// <summary>Family, serial number and location, on one line under the title.</summary>
    /// <param name="item">Item being described.</param>
    private static string DescribeContext(InventoryItem item)
    {
        var parts = new[]
        {
            ItemFamily.DisplayNameFor(item.FamilyCode),
            item.SerialNumber is null ? null : $"n° {item.SerialNumber}",
            item.LocationPath,
        };

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
