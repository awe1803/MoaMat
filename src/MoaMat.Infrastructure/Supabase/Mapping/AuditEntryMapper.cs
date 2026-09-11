using MoaMat.Domain.Audit;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="AuditLogRecord"/> to the domain <see cref="AuditEntry"/>.</summary>
internal static class AuditEntryMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row.</param>
    public static AuditEntry ToDomain(AuditLogRecord record) => new(
        record.Id,
        record.OccurredAt,
        record.ActorId,
        record.ActorEmail,
        record.Action,
        record.EntityTable,
        record.EntityId,
        record.Before,
        record.After,
        record.Context);
}
