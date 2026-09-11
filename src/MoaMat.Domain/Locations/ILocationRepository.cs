using MoaMat.Domain.Common;

namespace MoaMat.Domain.Locations;

/// <summary>
/// Port giving access to the storage hierarchy Section -> Room -> Container
/// (<c>db/model_item.sql</c>). Reading is open to every role; writing is
/// reserved to administrators by RLS (permission domain <c>referentiel</c>).
/// </summary>
/// <remarks>
/// Deletion is physical here, and the database refuses it when the node is
/// still referenced. That is intentional: an empty container carries no history
/// worth keeping, and a used one must never disappear from under an item.
/// </remarks>
public interface ILocationRepository
{
    /// <summary>All sections, ordered by label.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<LocationSection>> GetSectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Rooms, optionally restricted to one section.</summary>
    /// <param name="sectionId">Section to filter on, or null for every room.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<LocationRoom>> GetRoomsAsync(
        long? sectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Containers, optionally restricted to one room.</summary>
    /// <param name="roomId">Room to filter on, or null for every container.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<LocationContainer>> GetContainersAsync(
        long? roomId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Containers with their resolved full path, for drop-downs.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<LocationPath>> GetContainerPathsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a section.</summary>
    /// <param name="label">Validated label.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddSectionAsync(LocationLabel label, CancellationToken cancellationToken = default);

    /// <summary>Adds a room to a section.</summary>
    /// <param name="sectionId">Owning section.</param>
    /// <param name="label">Validated label.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddRoomAsync(
        long sectionId,
        LocationLabel label,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a container to a room.</summary>
    /// <param name="roomId">Owning room.</param>
    /// <param name="label">Validated label.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddContainerAsync(
        long roomId,
        LocationLabel label,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an empty section.</summary>
    /// <param name="sectionId">Section identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteSectionAsync(long sectionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes an empty room.</summary>
    /// <param name="roomId">Room identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteRoomAsync(long roomId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a container that no item points at.</summary>
    /// <param name="containerId">Container identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteContainerAsync(long containerId, CancellationToken cancellationToken = default);
}
