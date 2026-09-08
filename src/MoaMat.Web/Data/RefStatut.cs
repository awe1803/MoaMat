using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Entrée du catalogue de statuts <c>public.ref_statut</c> (référentiel).
/// <c>EstTerminal</c> : le retour depuis ce statut exige la permission
/// <c>status.terminal.override</c> (couche SQL / audit).
/// </summary>
[Table("ref_statut")]
public sealed class RefStatut : BaseModel
{
    [PrimaryKey("code", false)]
    public string Code { get; set; } = "";

    [Column("libelle")]
    public string Libelle { get; set; } = "";

    [Column("est_terminal")]
    public bool EstTerminal { get; set; }

    [Column("ordre")]
    public int Ordre { get; set; }
}
