using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>
/// Maps <see cref="CylinderDetailRecord"/> and <see cref="CylinderEventRecord"/>
/// to their domain equivalents, and the domain codes back to the database ones.
/// </summary>
internal static class CylinderMapper
{
    /// <summary>Converts one <c>item_bouteille</c> row.</summary>
    /// <param name="record">PostgREST row.</param>
    /// <param name="accessId">Legacy Access identifier (<c>item.origine_id</c>), if known.</param>
    public static CylinderDetails ToDomain(CylinderDetailRecord record, long? accessId = null) => new()
    {
        Thread = record.Filetage,
        HasDoubleOutlet = record.DoubleSortie,
        AccessId = accessId,
        ItemId = record.ItemId,
        PaintedNumber = record.NumPeint,
        VolumeLitres = record.VolumeNominalL,
        ServicePressureBar = record.PressionServiceBar,
        TareKg = record.TareKg,
        UsageCode = record.Famille,
        MaterialCode = record.Matiere,
        LastOpticalControlOn = SqlDateConverter.ToDateOnly(record.DateDernierControleOptique),
        LastHydraulicControlOn = SqlDateConverter.ToDateOnly(record.DateDernierControleHydraulique),
    };

    /// <summary>Converts one <c>bouteille_evenement</c> row.</summary>
    /// <param name="record">PostgREST row.</param>
    public static CylinderEvent ToDomain(CylinderEventRecord record) => new()
    {
        Id = record.Id,
        Type = ToType(record.Type),
        Outcome = record.Resultat switch
        {
            "conforme" => CampaignLineOutcome.Passed,
            "echec" => CampaignLineOutcome.Failed,
            _ => null,
        },
        OccurredOn = SqlDateConverter.ToDateOnly(record.DateEvenement),
        Provider = record.Prestataire,
        CostEur = record.CoutEur,
        CertificateNumber = record.NumCertificat,
        NextDueOn = SqlDateConverter.ToDateOnly(record.DateEcheanceSuivante),
        Remark = record.Remarque,
    };

    /// <summary>Database code of a control type accepted by <c>enregistrer_requalification_bouteille</c>.</summary>
    /// <param name="control">Control carried out.</param>
    public static string ToCode(CylinderControlType control) => control switch
    {
        CylinderControlType.Optical => "controle_optique",
        CylinderControlType.Hydraulic => "controle_hydraulique",
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, "Unknown control type."),
    };

    /// <summary>Database code of a requalification outcome.</summary>
    /// <param name="outcome">Outcome.</param>
    public static string ToCode(CampaignLineOutcome outcome) => outcome switch
    {
        CampaignLineOutcome.Passed => "conforme",
        CampaignLineOutcome.Failed => "echec",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown outcome."),
    };

    /// <summary>Database code of a deciding authority (<c>statut_autorite</c>), or <c>null</c>.</summary>
    /// <param name="authority">Authority.</param>
    public static string? ToCode(TransitionAuthority? authority) => authority switch
    {
        TransitionAuthority.OrganismeControle => "organisme_controle",
        TransitionAuthority.Ca => "ca",
        TransitionAuthority.GestionnaireMateriel => "gestionnaire_materiel",
        _ => null,
    };

    private static CylinderEventType ToType(string code) => code switch
    {
        "mise_en_service" => CylinderEventType.Commissioning,
        "controle_optique" => CylinderEventType.OpticalControl,
        "controle_hydraulique" => CylinderEventType.HydraulicControl,
        "ecartee" => CylinderEventType.Withdrawn,
        "rebut" => CylinderEventType.Scrapped,
        "incident" => CylinderEventType.Incident,
        // A type added in the database after this build is shown neutrally,
        // never as an incident.
        _ => CylinderEventType.Unknown,
    };
}
