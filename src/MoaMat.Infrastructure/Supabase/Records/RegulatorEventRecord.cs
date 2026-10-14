using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.detendeur_evenement</c> (<c>db/detendeur_evenement.sql</c>).
/// Read-only: the table has no insert/update policy, writes go through
/// <c>enregistrer_intervention_detendeur</c>.
/// </summary>
[Table("detendeur_evenement")]
internal sealed class RegulatorEventRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("item_id")]
    public long ItemId { get; set; }

    [Column("date_evenement")]
    public DateTime? DateEvenement { get; set; }

    [Column("prestataire")]
    public string? Prestataire { get; set; }

    [Column("ctrl_manometre")]
    public string? CtrlManometre { get; set; }

    [Column("ctrl_detendeur")]
    public string? CtrlDetendeur { get; set; }

    [Column("ctrl_octopus")]
    public string? CtrlOctopus { get; set; }

    [Column("ctrl_inflateur")]
    public string? CtrlInflateur { get; set; }

    [Column("ctrl_visuel_flexibles")]
    public string? CtrlVisuelFlexibles { get; set; }

    [Column("cout_eur")]
    public decimal? CoutEur { get; set; }

    [Column("remarque")]
    public string? Remarque { get; set; }
}
