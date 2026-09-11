using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_bouteille</c>, the cylinder specialisation
/// of <c>public.item</c> (class-table inheritance, see <c>db/MODELE.md</c> §3).
/// The primary key is also the foreign key to the parent item.
/// </summary>
[Table("item_bouteille")]
internal sealed class ItemCylinderRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("num_peint")]
    public string? NumPeint { get; set; }

    [Column("num_robinet")]
    public string? NumRobinet { get; set; }

    [Column("filetage")]
    public string? Filetage { get; set; }

    [Column("double_sortie")]
    public bool? DoubleSortie { get; set; }

    [Column("sangles")]
    public string? Sangles { get; set; }

    [Column("volume_nominal_l")]
    public decimal? VolumeNominalL { get; set; }

    [Column("pression_service_bar")]
    public int? PressionServiceBar { get; set; }

    [Column("tare_kg")]
    public decimal? TareKg { get; set; }

    [Column("capacite_reelle_l")]
    public decimal? CapaciteReelleL { get; set; }

    [Column("sortie_autorisee")]
    public bool? SortieAutorisee { get; set; }

    [Column("controle_effectue")]
    public bool? ControleEffectue { get; set; }

    [Column("date_mise_en_service")]
    public DateTime? DateMiseEnService { get; set; }

    [Column("autorite_declassement")]
    public string? AutoriteDeclassement { get; set; }
}
