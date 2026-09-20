using MoaMat.Domain.Inventory;

namespace MoaMat.Domain.Cylinders;

/// <summary>Everything the printable "fiche de vie" of a cylinder is made of.</summary>
/// <param name="Item">The inventory item.</param>
/// <param name="Details">Cylinder-specific characteristics, when known.</param>
/// <param name="Events">Timeline, most recent first.</param>
/// <param name="GeneratedOn">Day the sheet was produced.</param>
public sealed record CylinderLifeSheet(
    InventoryItem Item,
    CylinderDetails? Details,
    IReadOnlyList<CylinderEvent> Events,
    DateOnly GeneratedOn);
