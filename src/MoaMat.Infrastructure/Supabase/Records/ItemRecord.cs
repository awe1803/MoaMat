using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST WRITE shape of <c>public.item</c> (the shared trunk of the Item
/// model, see <c>db/MODELE.md</c>). Database-owned columns are absent on
/// purpose - <c>id</c> is <c>GENERATED ALWAYS</c>, <c>code_club_ambigu</c> is
/// recomputed by trigger, and <c>cree_le</c> / <c>maj_le</c> /
/// <c>origine_table</c> / <c>origine_id</c> record provenance - so a client
/// cannot overwrite them even by mistake.
/// </summary>
[Table("item")]
internal sealed class ItemRecord : BaseModel
{
    /// <summary>Technical key. <c>shouldInsert: false</c>: never sent on insert.</summary>
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("code_club")]
    public string? CodeClub { get; set; }

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
