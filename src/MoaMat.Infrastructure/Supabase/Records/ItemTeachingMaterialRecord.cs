using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_materiel_didactique</c>, the teaching
/// material specialisation of <c>public.item</c>.
/// </summary>
[Table("item_materiel_didactique")]
internal sealed class ItemTeachingMaterialRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("designation")]
    public string? Designation { get; set; }

    [Column("quantite")]
    public decimal? Quantite { get; set; }
}
