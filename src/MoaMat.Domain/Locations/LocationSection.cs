namespace MoaMat.Domain.Locations;

/// <summary>
/// Level 1 of the storage hierarchy (<c>public.lieu_section</c>).
/// </summary>
/// <remarks>
/// A section is a storage axis, never a permission perimeter: no authorisation
/// decision anywhere may depend on it.
/// </remarks>
/// <param name="Id">Technical identifier.</param>
/// <param name="Label">Display label.</param>
public sealed record LocationSection(long Id, string Label);
