namespace MoaMat.Domain.Common;

/// <summary>
/// <see cref="OperationResult"/> variant that carries a value on success — used
/// when the caller needs what the store produced (a generated identifier, for
/// instance).
/// </summary>
/// <typeparam name="TValue">Type of the value produced on success.</typeparam>
public readonly record struct OperationResult<TValue>
{
    private OperationResult(bool succeeded, TValue? value, string? error)
    {
        Succeeded = succeeded;
        Value = value;
        Error = error;
    }

    /// <summary>True when the operation was accepted by the backing store.</summary>
    public bool Succeeded { get; }

    /// <summary>Value produced by the operation; meaningful only when <see cref="Succeeded"/>.</summary>
    public TValue? Value { get; }

    /// <summary>User-facing reason for the failure; <c>null</c> on success.</summary>
    public string? Error { get; }

    /// <summary>Builds a successful result carrying <paramref name="value"/>.</summary>
    public static OperationResult<TValue> Success(TValue value) => new(true, value, null);

    /// <summary>Builds a failed result carrying a user-facing message.</summary>
    /// <param name="error">Non-empty message shown to the user.</param>
    public static OperationResult<TValue> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new OperationResult<TValue>(false, default, error);
    }
}
