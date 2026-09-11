using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>PostgREST shape of <c>public.lieu_section</c> (hierarchy level 1).</summary>
[Table("lieu_section")]
internal sealed class LocationSectionRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = string.Empty;
}
