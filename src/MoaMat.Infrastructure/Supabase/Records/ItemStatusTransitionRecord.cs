using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>PostgREST READ shape of the view <c>public.v_item_transition</c>.</summary>
[Table("v_item_transition")]
internal sealed class ItemStatusTransitionRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("item_id")]
    public long ItemId { get; set; }

    [Column("ancien_statut")]
    public string? AncienStatut { get; set; }

    [Column("ancien_statut_libelle")]
    public string? AncienStatutLibelle { get; set; }

    [Column("nouveau_statut")]
    public string NouveauStatut { get; set; } = string.Empty;

    [Column("nouveau_statut_libelle")]
    public string? NouveauStatutLibelle { get; set; }

    [Column("motif")]
    public string Motif { get; set; } = string.Empty;

    [Column("date_effet")]
    public DateTime DateEffet { get; set; }

    [Column("autorite_decision")]
    public string? AutoriteDecision { get; set; }

    [Column("piece_jointe_url")]
    public string? PieceJointeUrl { get; set; }

    [Column("decide_par")]
    public Guid? DecidePar { get; set; }

    [Column("decide_par_email")]
    public string? DecideParEmail { get; set; }

    [Column("cree_le")]
    public DateTimeOffset CreeLe { get; set; }
}
