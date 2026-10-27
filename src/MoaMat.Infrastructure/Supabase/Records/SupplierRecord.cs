using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>PostgREST shape of <c>public.ref_fournisseur</c> (<c>db/fournisseur.sql</c>).</summary>
[Table("ref_fournisseur")]
internal sealed class SupplierRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("nom")]
    public string Nom { get; set; } = string.Empty;

    [Column("adresse")]
    public string? Adresse { get; set; }

    [Column("telephone")]
    public string? Telephone { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("personne_contact")]
    public string? PersonneContact { get; set; }

    [Column("site_web")]
    public string? SiteWeb { get; set; }

    [Column("actif")]
    public bool Actif { get; set; } = true;
}
