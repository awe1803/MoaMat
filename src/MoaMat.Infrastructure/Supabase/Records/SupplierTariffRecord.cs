using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.ref_fournisseur_tarif</c>: one dated price of a
/// supplier's service. Append-only from the client (insert, and delete for
/// administrators); there is no update.
/// </summary>
[Table("ref_fournisseur_tarif")]
internal sealed class SupplierTariffRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("fournisseur_id")]
    public long FournisseurId { get; set; }

    [Column("type_prestation")]
    public string TypePrestation { get; set; } = string.Empty;

    [Column("prix_eur")]
    public decimal PrixEur { get; set; }

    [Column("date_effet")]
    public DateTime DateEffet { get; set; }
}
