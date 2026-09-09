using MoaMat.Domain.Common;

namespace MoaMat.Domain.Locations;

/// <summary>
/// Label of a node in the location hierarchy. The value object carries the
/// invariant: a <see cref="LocationLabel"/> is always trimmed, non-empty and
/// within the column length, so no caller can insert a blank container.
/// </summary>
public readonly record struct LocationLabel
{
    /// <summary>Maximum number of characters accepted for a label.</summary>
    public const int MaxLength = 120;

    private LocationLabel(string value) => Value = value;

    /// <summary>The trimmed, non-empty label.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and builds a label.
    /// </summary>
    /// <param name="candidate">Raw text typed by the user.</param>
    /// <param name="label">The validated label when the method returns a success.</param>
    /// <returns>A success, or a failure carrying a message to display as-is.</returns>
    public static OperationResult TryCreate(string? candidate, out LocationLabel label)
    {
        label = default;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return OperationResult.Failure("Le libellé est obligatoire.");
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length > MaxLength)
        {
            return OperationResult.Failure($"Le libellé ne peut pas dépasser {MaxLength} caractères.");
        }

        label = new LocationLabel(trimmed);
        return OperationResult.Success;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
