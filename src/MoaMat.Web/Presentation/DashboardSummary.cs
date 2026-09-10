using MoaMat.Domain.Inventory;

namespace MoaMat.Web.Presentation;

/// <summary>
/// Figures the dashboard puts in front of the club, derived from one read of
/// the active inventory.
/// </summary>
/// <remarks>
/// Everything is computed from a single list rather than from one query per
/// tile: over a WebAssembly connection a dozen round trips cost far more than
/// the arithmetic, and counts taken at the same instant cannot contradict each
/// other on screen.
/// </remarks>
/// <param name="Total">Number of active items read.</param>
/// <param name="Overdue">Items whose due date has passed.</param>
/// <param name="DueSoon">Items due within <see cref="InventoryItem.DueSoonHorizonInDays"/>.</param>
/// <param name="AmbiguousCodes">Items whose club code was flagged ambiguous.</param>
/// <param name="CountByFamily">Number of items per family code.</param>
/// <param name="NextDue">Most urgent items, overdue first, then by due date.</param>
public sealed record DashboardSummary(
    int Total,
    int Overdue,
    int DueSoon,
    int AmbiguousCodes,
    IReadOnlyDictionary<string, int> CountByFamily,
    IReadOnlyList<InventoryItem> NextDue)
{
    /// <summary>How many rows the "prochaines échéances" preview shows.</summary>
    public const int NextDuePreviewSize = 5;

    /// <summary>An empty dashboard, used while the inventory is loading.</summary>
    public static DashboardSummary Empty { get; } = new(
        Total: 0,
        Overdue: 0,
        DueSoon: 0,
        AmbiguousCodes: 0,
        CountByFamily: new Dictionary<string, int>(StringComparer.Ordinal),
        NextDue: []);

    /// <summary>Summarises <paramref name="items"/> as of <paramref name="today"/>.</summary>
    /// <param name="items">Active inventory, already read.</param>
    /// <param name="today">Reference day used to evaluate every due date.</param>
    public static DashboardSummary From(IReadOnlyCollection<InventoryItem> items, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(items);

        var overdue = 0;
        var dueSoon = 0;

        foreach (var item in items)
        {
            switch (item.DueStatusOn(today))
            {
                case DueStatus.Overdue:
                    overdue++;
                    break;
                case DueStatus.DueSoon:
                    dueSoon++;
                    break;
                default:
                    break;
            }
        }

        return new DashboardSummary(
            Total: items.Count,
            Overdue: overdue,
            DueSoon: dueSoon,
            AmbiguousCodes: items.Count(item => item.HasAmbiguousClubCode),
            CountByFamily: items
                .GroupBy(item => item.FamilyCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            NextDue: items
                .Where(item => item.DueOn is not null)
                .OrderBy(item => item.DueOn)
                .Take(NextDuePreviewSize)
                .ToArray());
    }

    /// <summary>Number of active items in <paramref name="familyCode"/>.</summary>
    /// <param name="familyCode">Family discriminator, see <see cref="ItemFamily.Code"/>.</param>
    public int CountIn(string familyCode) =>
        CountByFamily.TryGetValue(familyCode, out var count) ? count : 0;
}
