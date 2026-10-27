using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of one row of <c>public.v_tarif_campagne</c>
/// (<c>db/fournisseur.sql</c>): the price history of the Apragaz supplier. Read-only
/// from the client — prices are maintained on the suppliers screen.
/// </summary>
[Table("v_tarif_campagne")]
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
