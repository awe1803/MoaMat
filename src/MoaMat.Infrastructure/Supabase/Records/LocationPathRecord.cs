using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of the view <c>public.v_lieu_contenant</c>: a container with
/// its full path already resolved, for drop-downs.
/// </summary>
[Table("v_lieu_contenant")]
internal sealed class LocationPathRecord : BaseModel
{
    [PrimaryKey("contenant_id", false)]
    public long ContenantId { get; set; }

    [Column("contenant")]
    public string Contenant { get; set; } = string.Empty;

    [Column("local_id")]
    public long LocalId { get; set; }

    [Column("local")]
    public string Local { get; set; } = string.Empty;

    [Column("section_id")]
    public long SectionId { get; set; }

    [Column("section")]
    public string Section { get; set; } = string.Empty;

    [Column("chemin")]
    public string Chemin { get; set; } = string.Empty;
}
