using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of the view <c>public.compte_utilisateur</c>: an auth account
/// with its application role and activation state. Read-only - role changes go
/// through <see cref="UserRoleRecord"/>, activation through the
/// <c>set_compte_actif</c> function.
/// </summary>
[Table("compte_utilisateur")]
internal sealed class UserAccountRecord : BaseModel
{
    [PrimaryKey("user_id", false)]
    public Guid UserId { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("desactive")]
    public bool Desactive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("last_sign_in_at")]
    public DateTimeOffset? LastSignInAt { get; set; }
}
