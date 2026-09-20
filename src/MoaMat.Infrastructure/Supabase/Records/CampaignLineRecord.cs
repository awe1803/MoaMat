using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of one row of <c>public.v_campagne_ligne</c>
/// (<c>db/campagne.sql</c>) — a campaign line with its bottle's club code
/// resolved. Read-only; writes go through <c>definir_prestation_campagne_ligne</c>
/// and <c>pointer_retour_campagne</c>.
/// </summary>
[Table("v_campagne_ligne")]
internal sealed class CampaignLineRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("campagne_id")]
    public long CampagneId { get; set; }

    [Column("item_id")]
    public long ItemId { get; set; }

    [Column("item_code_club")]
    public string? ItemCodeClub { get; set; }

    [Column("type_prestation")]
    public string? TypePrestation { get; set; }

    [Column("cout_estime_eur")]
    public decimal? CoutEstimeEur { get; set; }

    [Column("cout_reel_eur")]
    public decimal? CoutReelEur { get; set; }

    [Column("num_certificat")]
    public string? NumCertificat { get; set; }

    [Column("resultat")]
    public string? Resultat { get; set; }

    [Column("date_retour_ligne")]
    public DateTime? DateRetourLigne { get; set; }
}
