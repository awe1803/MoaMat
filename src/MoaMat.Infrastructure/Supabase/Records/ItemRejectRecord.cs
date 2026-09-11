using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_reject</c>: a legacy Access value the
/// migration could not convert, kept verbatim. Written only by the migration
/// script (<c>db/transform_item.sql</c>).
/// </summary>
[Table("item_reject")]
internal sealed class ItemRejectRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("origine_table")]
    public string OrigineTable { get; set; } = string.Empty;

    [Column("origine_id")]
    public long? OrigineId { get; set; }

    [Column("colonne")]
    public string Colonne { get; set; } = string.Empty;

    [Column("valeur_brute")]
    public string ValeurBrute { get; set; } = string.Empty;

    [Column("raison")]
    public string Raison { get; set; } = string.Empty;

    [Column("cree_le")]
    public DateTimeOffset CreeLe { get; set; }
}
