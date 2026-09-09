using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Ligne de <c>public.item_reject</c> : une valeur Access non convertible
/// rencontrée lors de <c>db/transform_item.sql</c> (constats A11/A22),
/// conservée verbatim. Lecture seule (permission <c>item.read</c>) ; la table
/// n'est alimentée que par le script de reprise.
/// </summary>
[Table("item_reject")]
public sealed class ItemReject : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("origine_table")]
    public string OrigineTable { get; set; } = "";

    [Column("origine_id")]
    public long? OrigineId { get; set; }

    [Column("colonne")]
    public string Colonne { get; set; } = "";

    [Column("valeur_brute")]
    public string ValeurBrute { get; set; } = "";

    [Column("raison")]
    public string Raison { get; set; } = "";

    [Column("cree_le")]
    public DateTimeOffset CreeLe { get; set; }
}
