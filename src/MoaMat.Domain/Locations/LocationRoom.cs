namespace MoaMat.Domain.Locations;

/// <summary>
/// Level 2 of the storage hierarchy (<c>public.lieu_local</c>), attached to a
/// <see cref="LocationSection"/>.
/// </summary>
/// <param name="Id">Technical identifier.</param>
/// <param name="SectionId">Owning section.</param>
/// <param name="Label">Display label.</param>
public sealed record LocationRoom(long Id, long SectionId, string Label);
