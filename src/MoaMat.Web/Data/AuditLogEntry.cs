using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Ligne du journal d'audit (<c>public.audit_log</c>). Table append-only,
/// alimentée par des triggers PostgreSQL ; l'application ne fait que la lire
/// (policy RLS <c>audit_log_sel</c> : permission <c>audit.read</c>).
/// </summary>
[Table("audit_log")]
public sealed class AuditLogEntry : BaseModel
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
    public string Action { get; set; } = "";

    [Column("entity_table")]
    public string EntityTable { get; set; } = "";

    [Column("entity_id")]
    public string? EntityId { get; set; }

    [Column("before")]
    public Dictionary<string, object>? Before { get; set; }

    [Column("after")]
    public Dictionary<string, object>? After { get; set; }

    [Column("context")]
    public Dictionary<string, object>? Context { get; set; }
}
