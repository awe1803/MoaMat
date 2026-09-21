using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.bouteille_evenement</c> (<c>db/bouteille_evenement.sql</c>).
/// Read-only: the table has no insert/update policy, writes go through
/// <c>enregistrer_requalification_bouteille</c> and <c>signaler_incident_bouteille</c>.
/// </summary>
[Table("bouteille_evenement")]
internal sealed class CylinderEventRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("item_id")]
    public long ItemId { get; set; }

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("resultat")]
    public string? Resultat { get; set; }

    [Column("date_evenement")]
    public DateTime? DateEvenement { get; set; }

    [Column("prestataire")]
    public string? Prestataire { get; set; }

    [Column("cout_eur")]
    public decimal? CoutEur { get; set; }

    [Column("num_certificat")]
    public string? NumCertificat { get; set; }

    [Column("date_echeance_suivante")]
    public DateTime? DateEcheanceSuivante { get; set; }

    [Column("remarque")]
    public string? Remarque { get; set; }
}
