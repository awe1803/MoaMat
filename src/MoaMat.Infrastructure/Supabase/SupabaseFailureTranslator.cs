using System.Net;
using Microsoft.Extensions.Logging;
using Supabase.Postgrest.Exceptions;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Turns a provider error into a message that is safe to display, and makes
/// sure the real cause is logged instead of being thrown away.
/// </summary>
/// <remarks>
/// The distinction that matters operationally is <em>refused</em> versus
/// <em>unreachable</em>. Reporting a network outage as "insufficient rights"
/// sends users hunting for a permission problem that does not exist, which is
/// exactly what the previous blanket <c>catch (Exception)</c> did.
/// </remarks>
internal static class SupabaseFailureTranslator
{
    private const string NetworkMessage = "Service indisponible. Vérifiez votre connexion puis réessayez.";
    private const string ConflictMessage = "Enregistrement refusé : cette valeur existe déjà.";
    private const string UnexpectedMessage = "Une erreur inattendue est survenue. Réessayez.";

    /// <summary>PostgreSQL error code for a row-level security refusal.</summary>
    private const string InsufficientPrivilegeCode = "42501";

    /// <summary>PostgreSQL error code for a unique constraint violation.</summary>
    private const string UniqueViolationCode = "23505";

    /// <summary>PostgreSQL error code for a foreign key violation.</summary>
    private const string ForeignKeyViolationCode = "23503";

    /// <summary>
    /// Describes a failed PostgREST call.
    /// </summary>
    /// <param name="exception">The provider error.</param>
    /// <param name="refusedMessage">Message to show when the database refused the operation.</param>
    /// <param name="operationName">Operation name, for the log entry.</param>
    /// <param name="logger">Logger receiving the untranslated cause.</param>
    public static string Describe(
        PostgrestException exception,
        string refusedMessage,
        string operationName,
        ILogger logger)
    {
        var category = Categorize(exception);

        logger.LogWarning(
            exception,
            "Supabase refused {Operation} ({Category}, HTTP {StatusCode}).",
            operationName,
            category,
            exception.StatusCode);

        return category switch
        {
            SupabaseFailureCategory.Refused => refusedMessage,
            SupabaseFailureCategory.Conflict => ConflictMessage,
            SupabaseFailureCategory.Unreachable => NetworkMessage,
            _ => UnexpectedMessage,
        };
    }

    /// <summary>Describes a transport-level failure that never reached PostgREST.</summary>
    /// <param name="exception">The transport error.</param>
    /// <param name="operationName">Operation name, for the log entry.</param>
    /// <param name="logger">Logger receiving the untranslated cause.</param>
    public static string DescribeTransport(Exception exception, string operationName, ILogger logger)
    {
        logger.LogWarning(exception, "Supabase unreachable during {Operation}.", operationName);
        return NetworkMessage;
    }

    private static SupabaseFailureCategory Categorize(PostgrestException exception)
    {
        var status = (HttpStatusCode)exception.StatusCode;
        var content = exception.Content ?? string.Empty;

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || content.Contains(InsufficientPrivilegeCode, StringComparison.Ordinal))
        {
            return SupabaseFailureCategory.Refused;
        }

        if (status == HttpStatusCode.Conflict
            || content.Contains(UniqueViolationCode, StringComparison.Ordinal)
            || content.Contains(ForeignKeyViolationCode, StringComparison.Ordinal))
        {
            return SupabaseFailureCategory.Conflict;
        }

        // Status 0 means the request never produced an HTTP response.
        if (exception.StatusCode is 0 or >= 500)
        {
            return SupabaseFailureCategory.Unreachable;
        }

        return SupabaseFailureCategory.Unexpected;
    }
}
