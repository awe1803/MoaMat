using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// Read shape of <c>public.item_bouteille</c> for the cylinder fiche: the
/// characteristics plus the classification and control counters added by
/// <c>db/item_bouteille.sql</c>. Read-only — writes go through
/// <see cref="ItemCylinderRecord"/> or the timeline functions.
/// </summary>
[Table("item_bouteille")]
internal sealed class CylinderDetailRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("num_peint")]
    public string? NumPeint { get; set; }

    [Column("volume_nominal_l")]
    public decimal? VolumeNominalL { get; set; }

    [Column("pression_service_bar")]
    public int? PressionServiceBar { get; set; }

    [Column("tare_kg")]
    public decimal? TareKg { get; set; }

    [Column("famille")]
    public string? Famille { get; set; }

    [Column("matiere")]
    public string? Matiere { get; set; }

    [Column("filetage")]
    public string? Filetage { get; set; }

    [Column("double_sortie")]
    public bool? DoubleSortie { get; set; }

    [Column("date_dernier_controle_optique")]
    public DateTime? DateDernierControleOptique { get; set; }

    [Column("date_dernier_controle_hydraulique")]
    public DateTime? DateDernierControleHydraulique { get; set; }
}
