using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of one row of <c>public.campagne</c> (<c>db/campagne.sql</c>).
/// Read-only from the client — every write goes through a dedicated RPC
/// function (<c>creer_campagne</c>, <c>envoyer_campagne</c>,
/// <c>pointer_retour_campagne</c>).
/// </summary>
[Table("campagne")]
internal sealed class CampaignRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("prestataire")]
    public string Prestataire { get; set; } = string.Empty;

    [Column("statut")]
    public string Statut { get; set; } = string.Empty;

    [Column("numero_bon")]
    public string? NumeroBon { get; set; }

    [Column("date_envoi")]
    public DateTime? DateEnvoi { get; set; }

    [Column("date_retour")]
    public DateTime? DateRetour { get; set; }

    [Column("cree_le")]
    public DateTimeOffset CreeLe { get; set; }

    [Column("modifie_le")]
    public DateTimeOffset ModifieLe { get; set; }
}
