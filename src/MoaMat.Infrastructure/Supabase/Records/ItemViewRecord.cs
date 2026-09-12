using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST READ shape of the view <c>public.v_item</c>: an inventory item with
/// its resolved status, its full location path and its club code ambiguity
/// flags. The view is <c>security_invoker</c>, so it applies the RLS of
/// <c>public.item</c> to the caller.
/// </summary>
[Table("v_item")]
internal sealed class ItemViewRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("code_club")]
    public string? CodeClub { get; set; }

    /// <summary>Materialised flag: club code duplicated or not following the naming scheme.</summary>
    [Column("code_club_ambigu")]
    public bool CodeClubAmbigu { get; set; }

    [Column("code_club_duplique")]
    public bool? CodeClubDuplique { get; set; }

    [Column("code_club_non_structurant")]
    public bool? CodeClubNonStructurant { get; set; }

    [Column("famille")]
    public string Famille { get; set; } = string.Empty;

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
    public string StatutCode { get; set; } = string.Empty;

    [Column("statut_libelle")]
    public string? StatutLibelle { get; set; }

    [Column("statut_terminal")]
    public bool StatutTerminal { get; set; }

    /// <summary>Calculated availability (never an editable field) — see <c>public.item_est_disponible()</c>.</summary>
    [Column("disponible")]
    public bool Disponible { get; set; }

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
