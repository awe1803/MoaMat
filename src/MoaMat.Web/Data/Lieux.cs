using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

// Hiérarchie de lieux Section -> Local -> Contenant (cf. db/model_item.sql).
// Domaine de permission : « referentiel » (lecture : tous ; écriture : admin+).
// AUCUNE logique d'autorisation ne s'appuie sur la section (Q18.3).

/// <summary>Niveau 1 : <c>public.lieu_section</c>.</summary>
[Table("lieu_section")]
public sealed class LieuSection : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = "";
}

/// <summary>Niveau 2 : <c>public.lieu_local</c> (rattaché à une section).</summary>
[Table("lieu_local")]
public sealed class LieuLocal : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("section_id")]
    public long SectionId { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = "";
}

/// <summary>Niveau 3 : <c>public.lieu_contenant</c> (rattaché à un local).</summary>
[Table("lieu_contenant")]
public sealed class LieuContenant : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("local_id")]
    public long LocalId { get; set; }

    [Column("libelle")]
    public string Libelle { get; set; } = "";
}

/// <summary>
/// Ligne de la vue <c>public.v_lieu_contenant</c> : un contenant avec son chemin
/// complet (<c>Section › Local › Contenant</c>). Lecture seule — sert les listes
/// déroulantes de filtre / d'affectation.
/// </summary>
[Table("v_lieu_contenant")]
public sealed class LieuContenantRow : BaseModel
{
    [PrimaryKey("contenant_id", false)]
    public long ContenantId { get; set; }

    [Column("contenant")]
    public string Contenant { get; set; } = "";

    [Column("local_id")]
    public long LocalId { get; set; }

    [Column("local")]
    public string Local { get; set; } = "";

    [Column("section_id")]
    public long SectionId { get; set; }

    [Column("section")]
    public string Section { get; set; } = "";

    [Column("chemin")]
    public string Chemin { get; set; } = "";
}
