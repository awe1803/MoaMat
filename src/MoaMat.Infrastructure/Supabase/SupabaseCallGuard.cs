using System.Net.Http;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using Supabase.Postgrest.Exceptions;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Single place where a Supabase call is executed and its failures are handled.
/// </summary>
/// <remarks>
/// <para>Reads surface as <see cref="DataAccessException"/>; writes surface as a
/// failed <see cref="OperationResult"/>. Either way the provider error is logged
/// once and never reaches the user verbatim.</para>
/// <para>Caller-requested cancellation is rethrown untouched: a cancelled render
/// is not an error to report, and swallowing it would leave a screen spinning.</para>
/// </remarks>
internal sealed class SupabaseCallGuard
{
    private readonly ILogger _logger;

    /// <summary>Creates a guard logging to <paramref name="logger"/>.</summary>
    /// <param name="logger">Logger of the owning repository.</param>
    public SupabaseCallGuard(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Runs a read, translating any failure into <see cref="DataAccessException"/>.</summary>
    /// <typeparam name="TResult">Type returned by the read.</typeparam>
    /// <param name="operationName">Operation name, used for logging.</param>
    /// <param name="failureMessage">User-facing message when the read cannot be served.</param>
    /// <param name="read">The read to perform.</param>
    /// <param name="cancellationToken">Token the caller may have cancelled.</param>
    /// <exception cref="DataAccessException">The read could not be served.</exception>
    public async Task<TResult> ReadAsync<TResult>(
        string operationName,
        string failureMessage,
        Func<Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgrestException exception)
        {
            throw new DataAccessException(
                SupabaseFailureTranslator.Describe(exception, failureMessage, operationName, _logger),
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new DataAccessException(
                SupabaseFailureTranslator.DescribeTransport(exception, operationName, _logger),
                exception);
        }
    }

    /// <summary>Runs a write, translating any failure into a failed result.</summary>
    /// <param name="operationName">Operation name, used for logging.</param>
    /// <param name="refusedMessage">User-facing message when the database refuses the write.</param>
    /// <param name="write">The write to perform.</param>
    /// <param name="cancellationToken">Token the caller may have cancelled.</param>
    public async Task<OperationResult> WriteAsync(
        string operationName,
        string refusedMessage,
        Func<Task> write,
        CancellationToken cancellationToken)
    {
        var result = await WriteValueAsync<bool>(
            operationName,
            refusedMessage,
            async () =>
            {
                await write().ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded ? OperationResult.Success : OperationResult.Failure(result.Error!);
    }

    /// <summary>Runs a write that produces a value, translating any failure into a failed result.</summary>
    /// <typeparam name="TValue">Type produced by the write.</typeparam>
    /// <param name="operationName">Operation name, used for logging.</param>
    /// <param name="refusedMessage">User-facing message when the database refuses the write.</param>
    /// <param name="write">The write to perform.</param>
    /// <param name="cancellationToken">Token the caller may have cancelled.</param>
    public async Task<OperationResult<TValue>> WriteValueAsync<TValue>(
        string operationName,
        string refusedMessage,
        Func<Task<TValue>> write,
        CancellationToken cancellationToken)
    {
        try
        {
            return OperationResult<TValue>.Success(await write().ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgrestException exception)
        {
            return OperationResult<TValue>.Failure(
                SupabaseFailureTranslator.Describe(exception, refusedMessage, operationName, _logger));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return OperationResult<TValue>.Failure(
                SupabaseFailureTranslator.DescribeTransport(exception, operationName, _logger));
        }
    }
}
