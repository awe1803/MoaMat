using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_petit_materiel</c>, the small equipment
/// specialisation of <c>public.item</c>.
/// </summary>
[Table("item_petit_materiel")]
internal sealed class ItemSmallEquipmentRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("sous_famille")]
    public string? SousFamille { get; set; }

    [Column("couleur")]
    public string? Couleur { get; set; }

    [Column("taille")]
    public string? Taille { get; set; }

    [Column("pointure")]
    public string? Pointure { get; set; }

    [Column("quantite")]
    public decimal? Quantite { get; set; }
}
