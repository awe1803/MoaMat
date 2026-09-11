namespace MoaMat.Domain.Common;

/// <summary>
/// Outcome of a write operation that the caller is expected to surface to the
/// user. Failures carry a message that is safe to display: adapters must never
/// leak a raw provider error (SQL text, stack trace, header) into it.
/// </summary>
public readonly record struct OperationResult
{
    private OperationResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    /// <summary>True when the operation was accepted by the backing store.</summary>
    public bool Succeeded { get; }

    /// <summary>User-facing reason for the failure; <c>null</c> on success.</summary>
    public string? Error { get; }

    /// <summary>The single successful result value.</summary>
    public static OperationResult Success { get; } = new(true, null);

    /// <summary>Builds a failed result carrying a user-facing message.</summary>
    /// <param name="error">Non-empty message shown to the user.</param>
    public static OperationResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new OperationResult(false, error);
    }
}
