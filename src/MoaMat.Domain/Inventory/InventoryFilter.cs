namespace MoaMat.Domain.Inventory;

/// <summary>
/// Criteria narrowing an inventory query. Every criterion is optional; an
/// instance with no criterion set returns the active inventory.
/// </summary>
/// <remarks>
/// <see cref="MaxResults"/> exists so a query can never walk the whole table:
/// the screen is a browsing surface, not an export, and an unbounded read over
/// a WebAssembly connection degrades badly as the inventory grows.
/// </remarks>
public sealed record InventoryFilter
{
    /// <summary>Largest page an inventory query may ask for.</summary>
    public const int MaxAllowedResults = 1000;

    /// <summary>Default page size when the caller does not care.</summary>
    public const int DefaultMaxResults = 500;

    private readonly int _maxResults = DefaultMaxResults;

    /// <summary>Family code (see <see cref="ItemFamily.Code"/>), or null for all families.</summary>
    public string? FamilyCode { get; init; }

    /// <summary>Status code (see <see cref="ItemStatus.Code"/>), or null for all statuses.</summary>
    public string? StatusCode { get; init; }

    /// <summary>Container the item is stored in - the finest level of the location hierarchy.</summary>
    public long? ContainerId { get; init; }

    /// <summary>Keep only items whose due date is on or before this date.</summary>
    public DateOnly? DueOnOrBefore { get; init; }

    /// <summary>Which activation states to include.</summary>
    public ActivationScope Activation { get; init; } = ActivationScope.ActiveOnly;

    /// <summary>Keep only items whose club code was flagged ambiguous.</summary>
    public bool AmbiguousCodesOnly { get; init; }

    /// <summary>
    /// Maximum number of rows to return. Values outside
    /// <c>[1, <see cref="MaxAllowedResults"/>]</c> are clamped rather than
    /// rejected, so a caller mistake degrades into a sane page instead of an error.
    /// </summary>
    public int MaxResults
    {
        get => _maxResults;
        init => _maxResults = Math.Clamp(value, 1, MaxAllowedResults);
    }
}
