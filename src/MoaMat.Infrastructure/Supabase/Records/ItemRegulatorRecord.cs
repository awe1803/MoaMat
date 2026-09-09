using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase.Records;

/// <summary>
/// PostgREST shape of <c>public.item_detendeur</c>, the regulator specialisation
/// of <c>public.item</c>.
/// </summary>
[Table("item_detendeur")]
internal sealed class ItemRegulatorRecord : BaseModel
{
    [PrimaryKey("item_id", false)]
    public long ItemId { get; set; }

    [Column("num_ordre")]
    public string? NumOrdre { get; set; }

    [Column("type_connexion")]
    public string? TypeConnexion { get; set; }

    [Column("a_premier_etage")]
    public bool? APremierEtage { get; set; }

    [Column("modele_premier_etage")]
    public string? ModelePremierEtage { get; set; }

    [Column("num_serie_premier_etage")]
    public string? NumSeriePremierEtage { get; set; }

    [Column("a_second_etage")]
    public bool? ASecondEtage { get; set; }

    [Column("modele_second_etage")]
    public string? ModeleSecondEtage { get; set; }

    [Column("num_serie_second_etage")]
    public string? NumSerieSecondEtage { get; set; }

    [Column("a_octopus")]
    public bool? AOctopus { get; set; }

    [Column("modele_octopus")]
    public string? ModeleOctopus { get; set; }

    [Column("num_serie_octopus")]
    public string? NumSerieOctopus { get; set; }

    [Column("a_inflateur")]
    public bool? AInflateur { get; set; }

    [Column("a_manometre")]
    public bool? AManometre { get; set; }

    [Column("sortie_autorisee")]
    public bool? SortieAutorisee { get; set; }

    [Column("reserve_enfants")]
    public bool? ReserveEnfants { get; set; }

    [Column("controle_effectue")]
    public bool? ControleEffectue { get; set; }

    [Column("date_dernier_controle")]
    public DateTime? DateDernierControle { get; set; }
}
