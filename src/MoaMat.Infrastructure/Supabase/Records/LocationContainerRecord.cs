using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>PostgREST shape of <c>public.lieu_contenant</c> (hierarchy level 3).</summary>
[Table("lieu_contenant")]
internal sealed class LocationContainerRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("local_id")]
    public long LocalId { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = string.Empty;
}
