using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Common;
using MoaMat.Domain.Locations;

namespace MoaMat.Web.Pages;

/// <summary>
/// Administration of the storage hierarchy Section -&gt; Room -&gt; Container.
/// </summary>
/// <remarks>
/// Labels go through <see cref="LocationLabel"/> before they reach the
/// repository, so a blank or oversized label is refused here with a clear
/// message instead of being sent to the database.
/// </remarks>
public partial class LocationManagement : ComponentBase
{
    private IReadOnlyList<LocationSection> _sections = [];
    private IReadOnlyList<LocationRoom> _rooms = [];
    private IReadOnlyList<LocationContainer> _containers = [];

    private long? _selectedSectionId;
    private long? _selectedRoomId;

    private string _newSectionLabel = string.Empty;
    private string _newRoomLabel = string.Empty;
    private string _newContainerLabel = string.Empty;

    private bool _isBusy;
    private string? _error;

    [Inject]
    private ILocationRepository Locations { get; set; } = default!;

    /// <inheritdoc />
    protected override Task OnInitializedAsync() => ReloadSectionsAsync();

    private async Task ReloadSectionsAsync()
    {
        _isBusy = true;
        _error = null;

        try
        {
            _sections = await Locations.GetSectionsAsync();
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

    private async Task SelectSectionAsync(long sectionId)
    {
        _selectedSectionId = sectionId;
        _selectedRoomId = null;
        _containers = [];
        _rooms = await ReadAsync(() => Locations.GetRoomsAsync(sectionId), []);
    }

    private async Task SelectRoomAsync(long roomId)
    {
        _selectedRoomId = roomId;
        _containers = await ReadAsync(() => Locations.GetContainersAsync(roomId), []);
    }

    private async Task AddSectionAsync()
    {
        if (!TryReadLabel(_newSectionLabel, out var label))
        {
            return;
        }

        await WriteAsync(() => Locations.AddSectionAsync(label));
        _newSectionLabel = string.Empty;
        await ReloadSectionsAsync();
    }

    private async Task AddRoomAsync()
    {
        if (_selectedSectionId is not { } sectionId || !TryReadLabel(_newRoomLabel, out var label))
        {
            return;
        }

        await WriteAsync(() => Locations.AddRoomAsync(sectionId, label));
        _newRoomLabel = string.Empty;
        _rooms = await ReadAsync(() => Locations.GetRoomsAsync(sectionId), _rooms);
    }

    private async Task AddContainerAsync()
    {
        if (_selectedRoomId is not { } roomId || !TryReadLabel(_newContainerLabel, out var label))
        {
            return;
        }

        await WriteAsync(() => Locations.AddContainerAsync(roomId, label));
        _newContainerLabel = string.Empty;
        _containers = await ReadAsync(() => Locations.GetContainersAsync(roomId), _containers);
    }

    private async Task DeleteSectionAsync(long sectionId)
    {
        await WriteAsync(() => Locations.DeleteSectionAsync(sectionId));

        if (_selectedSectionId == sectionId)
        {
            _selectedSectionId = null;
            _selectedRoomId = null;
            _rooms = [];
            _containers = [];
        }

        await ReloadSectionsAsync();
    }

    private async Task DeleteRoomAsync(long roomId)
    {
        await WriteAsync(() => Locations.DeleteRoomAsync(roomId));

        if (_selectedRoomId == roomId)
        {
            _selectedRoomId = null;
            _containers = [];
        }

        if (_selectedSectionId is { } sectionId)
        {
            _rooms = await ReadAsync(() => Locations.GetRoomsAsync(sectionId), _rooms);
        }
    }

    private async Task DeleteContainerAsync(long containerId)
    {
        await WriteAsync(() => Locations.DeleteContainerAsync(containerId));

        if (_selectedRoomId is { } roomId)
        {
            _containers = await ReadAsync(() => Locations.GetContainersAsync(roomId), _containers);
        }
    }

    /// <summary>Validates a typed label, surfacing the reason when it is refused.</summary>
    private bool TryReadLabel(string candidate, out LocationLabel label)
    {
        var validation = LocationLabel.TryCreate(candidate, out label);

        if (!validation.Succeeded)
        {
            _error = validation.Error;
        }

        return validation.Succeeded;
    }

    private async Task WriteAsync(Func<Task<OperationResult>> write)
    {
        _isBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var result = await write();

            if (!result.Succeeded)
            {
                _error = result.Error;
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<TResult> ReadAsync<TResult>(Func<Task<TResult>> read, TResult fallback)
    {
        try
        {
            return await read();
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
            return fallback;
        }
    }
}
