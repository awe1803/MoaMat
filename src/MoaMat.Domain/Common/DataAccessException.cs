namespace MoaMat.Domain.Common;

/// <summary>
/// Raised by an adapter when a read cannot be served. The message is already
/// user-facing and provider-neutral: adapters translate their own errors before
/// throwing, so no SQL text, header or stack detail reaches a screen.
/// </summary>
/// <remarks>
/// Only reads throw. Writes report through <see cref="OperationResult"/>,
/// because a refused write is an expected outcome the user must act on, not an
/// exceptional condition.
/// </remarks>
public sealed class DataAccessException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public DataAccessException()
        : base("Lecture des données impossible.")
    {
    }

    /// <summary>Creates the exception with a user-facing message.</summary>
    /// <param name="message">Message safe to display as-is.</param>
    public DataAccessException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a user-facing message and its cause.</summary>
    /// <param name="message">Message safe to display as-is.</param>
    /// <param name="innerException">Underlying provider error, for logging only.</param>
    public DataAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
