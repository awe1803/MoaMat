using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of one row of <c>public.ref_tarif_apragaz</c>
/// (<c>db/item_bouteille.sql</c>). Read-only from the client — the referential
/// is append-only, written by an administrator through <c>referentiel.create</c>.
/// </summary>
[Table("ref_tarif_apragaz")]
internal sealed class CylinderPricingRuleRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("type_prestation")]
    public string TypePrestation { get; set; } = string.Empty;

    [Column("prix_eur")]
    public decimal PrixEur { get; set; }

    [Column("date_effet")]
    public DateTime DateEffet { get; set; }
}
