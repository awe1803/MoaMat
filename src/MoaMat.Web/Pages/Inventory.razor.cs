using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;
using MoaMat.Domain.Locations;

namespace MoaMat.Web.Pages;

/// <summary>
/// Inventory browsing screen: unified list of the gear with its status, storage
/// path and club code ambiguity badge.
/// </summary>
public partial class Inventory : ComponentBase
{
    private readonly List<InventoryItem> _items = [];

    private IReadOnlyList<ItemStatus> _statuses = [];
    private IReadOnlyList<LocationPath> _containers = [];

    // Bound filter fields. They are kept separate from InventoryFilter because
    // the filter is an immutable domain record, rebuilt on every reload.
    private string _familyCode = string.Empty;
    private string _statusCode = string.Empty;
    private string _containerId = string.Empty;
    private DateOnly? _dueOnOrBefore;
    private ActivationScope _activation = ActivationScope.ActiveOnly;
    private bool _ambiguousCodesOnly;

    private bool _isBusy = true;
    private string? _error;

    [Inject]
    private IInventoryRepository Items { get; set; } = default!;

    [Inject]
    private ILocationRepository Locations { get; set; } = default!;

    /// <summary>Number of listed items whose club code is flagged ambiguous.</summary>
    private int AmbiguousCount => _items.Count(item => item.HasAmbiguousClubCode);

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

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _isBusy = true;
        _error = null;
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
        }
    }

    private InventoryFilter BuildFilter() => new()
    {
        FamilyCode = string.IsNullOrWhiteSpace(_familyCode) ? null : _familyCode,
        StatusCode = string.IsNullOrWhiteSpace(_statusCode) ? null : _statusCode,
        ContainerId = long.TryParse(_containerId, out var containerId) ? containerId : null,
        DueOnOrBefore = _dueOnOrBefore,
        Activation = _activation,
        AmbiguousCodesOnly = _ambiguousCodesOnly,
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

    /// <summary>Joins brand and model, falling back to a dash when both are empty.</summary>
    private static string DescribeModel(InventoryItem item)
    {
        var parts = new[] { item.Brand, item.Model }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var joined = string.Join(' ', parts);
        return joined.Length == 0 ? "—" : joined;
    }
}
