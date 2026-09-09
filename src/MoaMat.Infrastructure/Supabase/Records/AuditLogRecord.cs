using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.audit_log</c>. Append-only table fed by
/// database triggers; the application only reads it.
/// </summary>
[Table("audit_log")]
internal sealed class AuditLogRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    [Column("actor_id")]
    public Guid? ActorId { get; set; }

    [Column("actor_email")]
    public string? ActorEmail { get; set; }

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("entity_table")]
    public string EntityTable { get; set; } = string.Empty;

    [Column("entity_id")]
    public string? EntityId { get; set; }

    [Column("before")]
    public Dictionary<string, object>? Before { get; set; }

    [Column("after")]
    public Dictionary<string, object>? After { get; set; }

    [Column("context")]
    public Dictionary<string, object>? Context { get; set; }
}
