using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_gilet</c>, the buoyancy vest specialisation
/// of <c>public.item</c>.
/// </summary>
[Table("item_gilet")]
internal sealed class ItemBuoyancyVestRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("couleur")]
    public string? Couleur { get; set; }

    [Column("taille")]
    public string? Taille { get; set; }

    [Column("marquage")]
    public string? Marquage { get; set; }

    [Column("sortie_autorisee")]
    public bool? SortieAutorisee { get; set; }

    [Column("emplacement_remarque")]
    public string? EmplacementRemarque { get; set; }
}
