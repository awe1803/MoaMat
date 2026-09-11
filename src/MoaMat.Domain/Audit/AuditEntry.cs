namespace MoaMat.Domain.Audit;

/// <summary>
/// One entry of the append-only audit trail (<c>public.audit_log</c>). Entries
/// are written by PostgreSQL triggers and by privileged edge functions; the
/// application only ever reads them.
/// </summary>
/// <param name="Id">Technical identifier.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="ActorId">Who caused it, when a user session was involved.</param>
/// <param name="ActorEmail">E-mail of that user, captured at write time.</param>
/// <param name="Action">Event kind, for instance <c>role.changed</c>.</param>
/// <param name="EntityTable">Table the event applies to.</param>
/// <param name="EntityId">Identifier of the affected row.</param>
/// <param name="Before">Relevant state before the change.</param>
/// <param name="After">Relevant state after the change.</param>
/// <param name="Context">Free-form context, such as the calling edge function.</param>
public sealed record AuditEntry(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string? ActorEmail,
    string Action,
    string EntityTable,
    string? EntityId,
    IReadOnlyDictionary<string, object>? Before,
    IReadOnlyDictionary<string, object>? After,
    IReadOnlyDictionary<string, object>? Context);
