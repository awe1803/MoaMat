using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_piece_detachee</c>, the spare part
/// specialisation of <c>public.item</c>.
/// </summary>
[Table("item_piece_detachee")]
internal sealed class ItemSparePartRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("reference_fournisseur")]
    public string? ReferenceFournisseur { get; set; }

    [Column("dimension")]
    public string? Dimension { get; set; }

    [Column("colisage")]
    public string? Colisage { get; set; }

    [Column("conditionnement")]
    public string? Conditionnement { get; set; }

    [Column("stock")]
    public decimal? Stock { get; set; }

    [Column("prix_unitaire_eur")]
    public decimal? PrixUnitaireEur { get; set; }

    [Column("fournisseur")]
    public string? Fournisseur { get; set; }

    [Column("emplacement")]
    public string? Emplacement { get; set; }

    [Column("utilisation")]
    public string? Utilisation { get; set; }

    [Column("url_commande")]
    public string? UrlCommande { get; set; }
}
