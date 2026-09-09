using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.utilisateur_role</c> - one row per auth account,
/// and the source of truth for the application role. Writes are arbitrated by
/// the RLS policies <c>utilisateur_role_upd</c> / <c>utilisateur_role_del</c>.
/// </summary>
[Table("utilisateur_role")]
internal sealed class UserRoleRecord : BaseModel
{
    [PrimaryKey("user_id", false)]
    public Guid UserId { get; set; }

    [Column("role")]
    public string Role { get; set; } = string.Empty;
}
