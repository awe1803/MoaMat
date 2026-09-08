using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

// Spécialisations 1:1 de public.item (class-table inheritance — db/MODELE.md §3).
// Clé primaire = clé étrangère item_id. Domaine de permission : « item ».

/// <summary><c>public.item_bouteille</c>.</summary>
[Table("item_bouteille")]
public sealed class ItemBouteille : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("num_peint")] public string? NumPeint { get; set; }
    [Column("num_robinet")] public string? NumRobinet { get; set; }
    [Column("filetage")] public string? Filetage { get; set; }
    [Column("double_sortie")] public bool? DoubleSortie { get; set; }
    [Column("sangles")] public string? Sangles { get; set; }
    [Column("volume_nominal_l")] public decimal? VolumeNominalL { get; set; }
    [Column("pression_service_bar")] public int? PressionServiceBar { get; set; }
    [Column("tare_kg")] public decimal? TareKg { get; set; }
    [Column("capacite_reelle_l")] public decimal? CapaciteReelleL { get; set; }
    [Column("sortie_autorisee")] public bool? SortieAutorisee { get; set; }
    [Column("controle_effectue")] public bool? ControleEffectue { get; set; }
    [Column("date_mise_en_service")] public DateTime? DateMiseEnService { get; set; }
    [Column("autorite_declassement")] public string? AutoriteDeclassement { get; set; }
}

/// <summary><c>public.item_detendeur</c>.</summary>
[Table("item_detendeur")]
public sealed class ItemDetendeur : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("num_ordre")] public string? NumOrdre { get; set; }
    [Column("type_connexion")] public string? TypeConnexion { get; set; }
    [Column("a_premier_etage")] public bool? APremierEtage { get; set; }
    [Column("modele_premier_etage")] public string? ModelePremierEtage { get; set; }
    [Column("num_serie_premier_etage")] public string? NumSeriePremierEtage { get; set; }
    [Column("a_second_etage")] public bool? ASecondEtage { get; set; }
    [Column("modele_second_etage")] public string? ModeleSecondEtage { get; set; }
    [Column("num_serie_second_etage")] public string? NumSerieSecondEtage { get; set; }
    [Column("a_octopus")] public bool? AOctopus { get; set; }
    [Column("modele_octopus")] public string? ModeleOctopus { get; set; }
    [Column("num_serie_octopus")] public string? NumSerieOctopus { get; set; }
    [Column("a_inflateur")] public bool? AInflateur { get; set; }
    [Column("a_manometre")] public bool? AManometre { get; set; }
    [Column("sortie_autorisee")] public bool? SortieAutorisee { get; set; }
    [Column("reserve_enfants")] public bool? ReserveEnfants { get; set; }
    [Column("controle_effectue")] public bool? ControleEffectue { get; set; }
    [Column("date_dernier_controle")] public DateTime? DateDernierControle { get; set; }
}

/// <summary><c>public.item_gilet</c>.</summary>
[Table("item_gilet")]
public sealed class ItemGilet : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("couleur")] public string? Couleur { get; set; }
    [Column("taille")] public string? Taille { get; set; }
    [Column("marquage")] public string? Marquage { get; set; }
    [Column("sortie_autorisee")] public bool? SortieAutorisee { get; set; }
    [Column("emplacement_remarque")] public string? EmplacementRemarque { get; set; }
}

/// <summary><c>public.item_petit_materiel</c>.</summary>
[Table("item_petit_materiel")]
public sealed class ItemPetitMateriel : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("sous_famille")] public string? SousFamille { get; set; }
    [Column("couleur")] public string? Couleur { get; set; }
    [Column("taille")] public string? Taille { get; set; }
    [Column("pointure")] public string? Pointure { get; set; }
    [Column("quantite")] public decimal? Quantite { get; set; }
}

/// <summary><c>public.item_materiel_didactique</c>.</summary>
[Table("item_materiel_didactique")]
public sealed class ItemMaterielDidactique : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("designation")] public string? Designation { get; set; }
    [Column("quantite")] public decimal? Quantite { get; set; }
}

/// <summary><c>public.item_piece_detachee</c>.</summary>
[Table("item_piece_detachee")]
public sealed class ItemPieceDetachee : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("reference_fournisseur")] public string? ReferenceFournisseur { get; set; }
    [Column("dimension")] public string? Dimension { get; set; }
    [Column("colisage")] public string? Colisage { get; set; }
    [Column("conditionnement")] public string? Conditionnement { get; set; }
    [Column("stock")] public decimal? Stock { get; set; }
    [Column("prix_unitaire_eur")] public decimal? PrixUnitaireEur { get; set; }
    [Column("fournisseur")] public string? Fournisseur { get; set; }
    [Column("emplacement")] public string? Emplacement { get; set; }
    [Column("utilisation")] public string? Utilisation { get; set; }
    [Column("url_commande")] public string? UrlCommande { get; set; }
}
