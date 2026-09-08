using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Modèle d'ÉCRITURE de la table <c>public.item</c> (tronc commun du modèle
/// Item — cf. <c>db/MODELE.md</c>). Les colonnes gérées par la base ne sont pas
/// exposées : <c>id</c> (GENERATED ALWAYS), <c>code_club_ambigu</c> (recalculé
/// par trigger), <c>cree_le</c> / <c>maj_le</c>, <c>origine_table</c> /
/// <c>origine_id</c> (traçabilité de reprise).
///
/// <para>Les policies RLS (<c>db/rls.sql</c>, permission <c>item.*</c>) sont la
/// seule ligne de sécurité : ce modèle ne fait que structurer l'appel.</para>
/// </summary>
[Table("item")]
public sealed class Item : BaseModel
{
    /// <summary>Clé technique. <c>shouldInsert: false</c> : jamais envoyée (GENERATED ALWAYS).</summary>
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("code_club")]
    public string? CodeClub { get; set; }

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
    public string StatutCode { get; set; } = "en_service";

    [Column("lieu_contenant_id")]
    public long? LieuContenantId { get; set; }

    [Column("destination")]
    public string? Destination { get; set; }

    [Column("remarque")]
    public string? Remarque { get; set; }

    [Column("date_echeance")]
    public DateTime? DateEcheance { get; set; }

    [Column("actif")]
    public bool Actif { get; set; } = true;
}
