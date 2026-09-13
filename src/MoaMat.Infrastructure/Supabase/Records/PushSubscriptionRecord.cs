using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.abonnement_push</c>, read only to know whether
/// a browser is still subscribed. The RLS policy <c>abonnement_push_sel</c>
/// restricts reads to the signed-in user's rows; writes go through RPCs.
/// </summary>
[Table("abonnement_push")]
internal sealed class PushSubscriptionRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }
}
