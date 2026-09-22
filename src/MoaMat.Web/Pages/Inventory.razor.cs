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
/// <para>Every criterion — family, status, location, activation, validity chip
/// and free text — is pushed down to the database
/// (<see cref="InventoryFilter"/>): the browser never filters the inventory
/// itself, and the chip counters are counted by the database too, so they stay
/// right even when the list is capped at <see cref="InventoryFilter.MaxAllowedResults"/>.
/// A change of family, chip or search text therefore costs one round trip.</para>
/// </remarks>
public partial class Inventory : ComponentBase
{
    /// <summary>Chip value listing the items whose due date has passed.</summary>
    public const string DueFilterOverdue = "depassee";

    /// <summary>Chip value listing the items due within the campaign horizon.</summary>
    public const string DueFilterSoon = "proche";

    /// <summary>Chip value listing the items still comfortably valid.</summary>
    public const string DueFilterValid = "valide";

    /// <summary>Chip value listing the items with no due date recorded.</summary>
    public const string DueFilterNone = "aucune";

    private const string RoutePath = "inventaire";

    private readonly List<InventoryItem> _items = [];

    private IReadOnlyList<ItemStatus> _statuses = [];
    private IReadOnlyList<LocationPath> _containers = [];

    // Bound filter fields. They are kept separate from InventoryFilter because
    // the filter is an immutable domain record, rebuilt on every reload.
    private string _statusCode = string.Empty;
    private string _containerId = string.Empty;
    private ActivationScope _activation = ActivationScope.ActiveOnly;
    private bool _ambiguousCodesOnly;

    private string? _loadedFamily;
    private string? _loadedDue;
    private string? _loadedSearch;
    private bool _isBusy = true;
    private string? _error;

    // Counters of the four validity chips, counted by the database.
    private int _countAll;
    private int _countOverdue;
    private int _countSoon;
    private int _countValid;
    private int _countNone;

    // Guards against an older, slower response overwriting a newer one when
    // the user changes the filter faster than the network answers.
    private int _loadSequence;

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

    /// <summary>Rows returned by the database for the current criteria.</summary>
    private IReadOnlyList<InventoryItem> VisibleItems => _items;

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

    /// <summary>Link opening the fiche of a cylinder.</summary>
    /// <param name="itemId">Identifier of the cylinder item.</param>
    public static string CylinderHref(long itemId) => $"bouteilles/{itemId}";

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
    protected override async Task OnParametersSetAsync()
    {
        if (_isBusy
            || !string.Equals(_loadedFamily, Family, StringComparison.Ordinal)
            || !string.Equals(_loadedDue, Due, StringComparison.Ordinal)
            || !string.Equals(_loadedSearch, Search, StringComparison.Ordinal))
        {
            await ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        var sequence = ++_loadSequence;
        _isBusy = true;
        _error = null;
        _loadedFamily = Family;
        _loadedDue = Due;
        _loadedSearch = Search;
        StateHasChanged();

        try
        {
            var listTask = Items.GetItemsAsync(BuildFilter(DueBandFor(Due)));
            var counts = await Task.WhenAll(
                CountAsync(null),
                CountAsync(DueStatus.Overdue),
                CountAsync(DueStatus.DueSoon),
                CountAsync(DueStatus.Valid),
                CountAsync(DueStatus.Unknown));
            var items = await listTask;

            if (sequence != _loadSequence)
            {
                return;
            }

            (_countAll, _countOverdue, _countSoon, _countValid, _countNone) =
                (counts[0], counts[1], counts[2], counts[3], counts[4]);
            _items.Clear();
            _items.AddRange(items);
        }
        catch (DataAccessException exception)
        {
            if (sequence == _loadSequence)
            {
                _error = exception.Message;
                _items.Clear();
            }
        }
        finally
        {
            if (sequence == _loadSequence)
            {
                _isBusy = false;
            }
        }
    }

    private Task<int> CountAsync(DueStatus? band) => Items.CountItemsAsync(BuildFilter(band));

    private InventoryFilter BuildFilter(DueStatus? band) => new()
    {
        FamilyCode = string.IsNullOrWhiteSpace(Family) ? null : Family,
        StatusCode = string.IsNullOrWhiteSpace(_statusCode) ? null : _statusCode,
        ContainerId = long.TryParse(_containerId, out var containerId) ? containerId : null,
        Activation = _activation,
        AmbiguousCodesOnly = _ambiguousCodesOnly,
        DueBand = band,
        ReferenceDay = Today,
        SearchText = Search,
        MaxResults = InventoryFilter.MaxAllowedResults,
    };

    /// <summary>Validity band selected by the chip value of the URL, or <c>null</c> for no restriction.</summary>
    /// <param name="due">Value of the <c>echeance</c> query parameter.</param>
    private static DueStatus? DueBandFor(string? due) => due switch
    {
        DueFilterOverdue => DueStatus.Overdue,
        DueFilterSoon => DueStatus.DueSoon,
        DueFilterValid => DueStatus.Valid,
        DueFilterNone => DueStatus.Unknown,
        _ => null,
    };

    /// <summary>Number of items matching one validity chip, counted by the database.</summary>
    /// <param name="due">Chip value, or <c>null</c> for every item.</param>
    private int CountFor(string? due) => due switch
    {
        DueFilterOverdue => _countOverdue,
        DueFilterSoon => _countSoon,
        DueFilterValid => _countValid,
        DueFilterNone => _countNone,
        _ => _countAll,
    };

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
