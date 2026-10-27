using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of one row of <c>public.fournisseur_prestation</c>
/// (<c>db/fournisseur_prestation.sql</c>). Read-only from the client — writes go
/// through <c>enregistrer_prestation_fournisseur</c> and
/// <c>supprimer_prestation_fournisseur</c>.
/// </summary>
[Table("fournisseur_prestation")]
internal sealed class SupplierServiceRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("fournisseur_id")]
    public long FournisseurId { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = string.Empty;

    [Column("controle_cylindre")]
    public string? ControleCylindre { get; set; }

    [Column("methode_hydraulique")]
    public string? MethodeHydraulique { get; set; }

    [Column("actif")]
    public bool Actif { get; set; }

    [Column("commentaire")]
    public string? Commentaire { get; set; }
}
