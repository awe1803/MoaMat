using Microsoft.Extensions.Logging;
using MoaMat.Domain.Audit;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="IAuditLogRepository"/>.
/// </summary>
/// <remarks>
/// Access control is carried by the RLS policy <c>audit_log_sel</c> (permission
/// <c>audit.read</c>): an unauthorised caller reads zero rows rather than
/// getting an error, so the screen renders an empty trail instead of leaking
/// the existence of entries.
/// </remarks>
internal sealed class SupabaseAuditLogRepository : IAuditLogRepository
{
    private const string ReadFailureMessage = "Lecture du journal d'audit impossible.";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseAuditLogRepository(global::Supabase.Client client, ILogger<SupabaseAuditLogRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(
        int limit = IAuditLogRepository.DefaultPageSize,
        string? actionFilter = null,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(limit, 1, IAuditLogRepository.MaxPageSize);

        return _guard.ReadAsync(
            nameof(GetRecentAsync),
            ReadFailureMessage,
            async () =>
            {
                var query = _client
                    .From<AuditLogRecord>()
                    .Order("occurred_at", Constants.Ordering.Descending)
                    .Limit(pageSize);

                if (!string.IsNullOrWhiteSpace(actionFilter))
                {
                    query = query.Filter("action", Constants.Operator.Equals, actionFilter);
                }

                var response = await query.Get(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<AuditEntry>)[.. response.Models.Select(AuditEntryMapper.ToDomain)];
            },
            cancellationToken);
    }
}
