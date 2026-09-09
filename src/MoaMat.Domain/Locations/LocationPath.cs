namespace MoaMat.Domain.Locations;

/// <summary>
/// A container together with its resolved full path
/// (<c>public.v_lieu_contenant</c>). Feeds the filter and assignment drop-downs
/// with a single readable string instead of three joined lookups.
/// </summary>
/// <param name="ContainerId">Container identifier, the value stored on an item.</param>
/// <param name="ContainerLabel">Container label.</param>
/// <param name="RoomId">Owning room identifier.</param>
/// <param name="RoomLabel">Owning room label.</param>
/// <param name="SectionId">Owning section identifier.</param>
/// <param name="SectionLabel">Owning section label.</param>
/// <param name="FullPath">Human-readable path, "Section > Room > Container".</param>
public sealed record LocationPath(
    long ContainerId,
    string ContainerLabel,
    long RoomId,
    string RoomLabel,
    long SectionId,
    string SectionLabel,
    string FullPath);
