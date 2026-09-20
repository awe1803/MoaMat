using System.Net;
using System.Text.Json;
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

    /// <summary>
    /// PostgreSQL error code for a CHECK constraint or an explicit
    /// <c>RAISE ... USING ERRCODE = 'check_violation'</c> — the SQLSTATE this
    /// codebase's own business-rule refusals deliberately raise (missing
    /// service type, a date out of order, a negative cost, an ineligible
    /// bottle selection, ...). Categorized as <see cref="SupabaseFailureCategory.Refused"/>,
    /// same as a permission refusal: a deterministic "this call is invalid"
    /// outcome the caller's own <c>refusedMessage</c> already explains, never
    /// worth surfacing as "Une erreur inattendue est survenue" — that wording
    /// suggests a transient failure worth retrying, which a business-rule
    /// refusal never is.
    /// </summary>
    private const string CheckViolationCode = "23514";

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
        var category = Categorize(exception.StatusCode, exception.Content);

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

    /// <summary>
    /// Pure categorization logic, taking plain values rather than a
    /// <see cref="PostgrestException"/> so it can be unit tested without one
    /// — the exception type's <c>Content</c>/<c>StatusCode</c> setters are
    /// internal to the Supabase package, unconstructible from outside it.
    /// </summary>
    /// <param name="statusCode">HTTP status code PostgREST responded with.</param>
    /// <param name="content">Raw PostgREST response body, or <c>null</c>.</param>
    internal static SupabaseFailureCategory Categorize(int statusCode, string? content)
    {
        var status = (HttpStatusCode)statusCode;
        var sqlState = ExtractSqlState(content);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || sqlState is InsufficientPrivilegeCode or CheckViolationCode)
        {
            return SupabaseFailureCategory.Refused;
        }

        if (status == HttpStatusCode.Conflict
            || sqlState is UniqueViolationCode or ForeignKeyViolationCode)
        {
            return SupabaseFailureCategory.Conflict;
        }

        // Status 0 means the request never produced an HTTP response.
        if (statusCode is 0 or >= 500)
        {
            return SupabaseFailureCategory.Unreachable;
        }

        return SupabaseFailureCategory.Unexpected;
    }

    /// <summary>
    /// Reads the <c>code</c> field of a PostgREST error body — <c>{"code":
    /// "23514", "message": "...", ...}</c> — as an exact value, never a
    /// substring match on the raw body.
    /// </summary>
    /// <remarks>
    /// A substring search (the previous implementation) is unsafe here: this
    /// codebase's own <c>RAISE EXCEPTION</c> messages routinely embed a
    /// numeric id in the <c>message</c> field (e.g. "La bouteille 123514 est
    /// déjà engagée…", <c>db/campagne.sql</c>), and that id can itself
    /// contain one of the SQLSTATE digit strings being searched for —
    /// misclassifying an unrelated error (or worse, silently flipping a
    /// <see cref="SupabaseFailureCategory.Conflict"/> into
    /// <see cref="SupabaseFailureCategory.Refused"/> or vice versa) purely
    /// because an id happened to contain the right six digits.
    /// </remarks>
    /// <param name="content">Raw PostgREST response body, or <c>null</c>.</param>
    private static string? ExtractSqlState(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            // Not a PostgREST JSON error body (an HTML error page from a
            // proxy, an empty body, ...) — no SQLSTATE to extract, fall
            // through to the HTTP-status-only categorization.
            return null;
        }
    }
}
