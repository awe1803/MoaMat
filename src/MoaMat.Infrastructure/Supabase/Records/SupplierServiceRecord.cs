using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.ref_fournisseur_prestation</c>: one service
/// offered by a supplier. The key is composite, so rows are addressed with
/// filters rather than by a single primary key.
/// </summary>
[Table("ref_fournisseur_prestation")]
internal sealed class SupplierServiceRecord : BaseModel
{
    [Column("fournisseur_id")]
    public long FournisseurId { get; set; }

    [Column("type_prestation")]
    public string TypePrestation { get; set; } = string.Empty;
}
