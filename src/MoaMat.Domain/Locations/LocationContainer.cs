namespace MoaMat.Domain.Locations;

/// <summary>
/// Level 3 of the storage hierarchy (<c>public.lieu_contenant</c>), attached to
/// a <see cref="LocationRoom"/>. This is the level an inventory item points at.
/// </summary>
/// <param name="Id">Technical identifier.</param>
/// <param name="RoomId">Owning room.</param>
/// <param name="Label">Display label.</param>
public sealed record LocationContainer(long Id, long RoomId, string Label);
