namespace MoaMat.Domain.Audit;

/// <summary>
/// Read-only port over the audit trail. There is deliberately no write method:
/// the trail is append-only and fed by the database itself, so the application
/// cannot forge, amend or erase an entry.
/// </summary>
/// <remarks>
/// Access is arbitrated by the RLS policy <c>audit_log_sel</c> (permission
/// <c>audit.read</c>): an unauthorised caller simply reads zero rows.
/// </remarks>
public interface IAuditLogRepository
{
    /// <summary>Largest page the audit screen may ask for.</summary>
    const int MaxPageSize = 500;

    /// <summary>Page size used when the caller does not specify one.</summary>
    const int DefaultPageSize = 200;

    /// <summary>Most recent entries first.</summary>
    /// <param name="limit">
    /// Page size; clamped to <c>[1, <see cref="MaxPageSize"/>]</c> so a caller
    /// mistake cannot turn the screen into a full table scan.
    /// </param>
    /// <param name="actionFilter">Exact match on the action, or null for every action.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(
        int limit = DefaultPageSize,
        string? actionFilter = null,
        CancellationToken cancellationToken = default);
}
