using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.preference_utilisateur</c>, read to know the
/// display choices of the signed-in account. The RLS policy
/// <c>preference_utilisateur_sel</c> restricts reads to their own row; writes
/// go through RPCs.
/// </summary>
[Table("preference_utilisateur")]
internal sealed class UserPreferenceRecord : BaseModel
{
    [PrimaryKey("user_id", false)]
    public Guid UserId { get; set; }

    [Column("invite_installation_masquee")]
    public bool InviteInstallationMasquee { get; set; }
}
