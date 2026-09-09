using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>PostgREST shape of <c>public.lieu_local</c> (hierarchy level 2).</summary>
[Table("lieu_local")]
internal sealed class LocationRoomRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("section_id")]
    public long SectionId { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = string.Empty;
}
