using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of one row of <c>public.fournisseur_prestation_tarif</c>
/// (<c>db/fournisseur_prestation.sql</c>) — one dated, append-only tariff.
/// Read-only from the client; writes go through <c>definir_tarif_prestation</c>.
/// </summary>
[Table("fournisseur_prestation_tarif")]
internal sealed class SupplierServiceTariffRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("prestation_id")]
    public long PrestationId { get; set; }

    [Column("prix_eur")]
    public decimal PrixEur { get; set; }

    [Column("date_effet")]
    public DateTime? DateEffet { get; set; }
}
