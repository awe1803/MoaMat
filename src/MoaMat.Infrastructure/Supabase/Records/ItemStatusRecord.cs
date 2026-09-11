using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>PostgREST shape of the status catalogue <c>public.ref_statut</c>.</summary>
[Table("ref_statut")]
internal sealed class ItemStatusRecord : BaseModel
{
    [PrimaryKey("code", false)]
    public string Code { get; set; } = string.Empty;

    [Column("libelle")]
    public string Libelle { get; set; } = string.Empty;

    [Column("est_terminal")]
    public bool EstTerminal { get; set; }

    [Column("ordre")]
    public int Ordre { get; set; }
}
