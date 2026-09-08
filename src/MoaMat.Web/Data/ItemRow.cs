using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Ligne de la vue de lecture <c>public.v_item</c> : un item d'inventaire avec
/// son statut, son chemin de lieu et ses drapeaux d'ambiguïté déjà résolus.
/// <para>Lecture seule — la vue est <c>security_invoker</c> : elle applique la
/// RLS de <c>public.item</c> (permission <c>item.read</c>). Les écritures
/// passent par <see cref="Item"/> via <see cref="ItemService"/>.</para>
/// </summary>
[Table("v_item")]
public sealed class ItemRow : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("code_club")]
    public string? CodeClub { get; set; }

    /// <summary>Drapeau matérialisé : code club dupliqué ou non structurant.</summary>
    [Column("code_club_ambigu")]
    public bool CodeClubAmbigu { get; set; }

    [Column("code_club_duplique")]
    public bool? CodeClubDuplique { get; set; }

    [Column("code_club_non_structurant")]
    public bool? CodeClubNonStructurant { get; set; }

    [Column("famille")]
    public string Famille { get; set; } = "";

    [Column("num_serie")]
    public string? NumSerie { get; set; }

    [Column("marque")]
    public string? Marque { get; set; }

    [Column("modele")]
    public string? Modele { get; set; }

    [Column("date_acquisition")]
    public DateTime? DateAcquisition { get; set; }

    [Column("prix_eur")]
    public decimal? PrixEur { get; set; }

    [Column("statut_code")]
    public string StatutCode { get; set; } = "";

    [Column("statut_libelle")]
    public string? StatutLibelle { get; set; }

    [Column("statut_terminal")]
    public bool StatutTerminal { get; set; }

    [Column("lieu_contenant_id")]
    public long? LieuContenantId { get; set; }

    [Column("lieu_section")]
    public string? LieuSection { get; set; }

    [Column("lieu_local")]
    public string? LieuLocal { get; set; }

    [Column("lieu_contenant")]
    public string? LieuContenant { get; set; }

    [Column("lieu_chemin")]
    public string? LieuChemin { get; set; }

    [Column("destination")]
    public string? Destination { get; set; }

    [Column("remarque")]
    public string? Remarque { get; set; }

    [Column("date_echeance")]
    public DateTime? DateEcheance { get; set; }

    [Column("actif")]
    public bool Actif { get; set; }

    [Column("origine_table")]
    public string? OrigineTable { get; set; }

    [Column("origine_id")]
    public long? OrigineId { get; set; }

    [Column("cree_le")]
    public DateTimeOffset CreeLe { get; set; }

    [Column("maj_le")]
    public DateTimeOffset MajLe { get; set; }
}
