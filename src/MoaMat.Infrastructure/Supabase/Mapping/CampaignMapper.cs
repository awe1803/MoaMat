using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>
/// Maps <see cref="CampaignRecord"/>/<see cref="CampaignLineRecord"/> and
/// <see cref="CylinderPricingRuleRecord"/> to their domain equivalents.
/// </summary>
internal static class CampaignMapper
{
    /// <summary>Converts one campaign row plus its already-fetched lines.</summary>
    /// <param name="record">PostgREST row of <c>public.campagne</c>.</param>
    /// <param name="lines">Lines belonging to this campaign, already mapped.</param>
    public static Campaign ToDomain(CampaignRecord record, IReadOnlyList<CampaignLine> lines) => new()
    {
        Id = record.Id,
        Provider = record.Prestataire,
        Status = ToStatus(record.Statut),
        BordereauNumber = record.NumeroBon,
        SentOn = SqlDateConverter.ToDateOnly(record.DateEnvoi),
        ReturnedOn = SqlDateConverter.ToDateOnly(record.DateRetour),
        Lines = lines,
        CreatedAt = record.CreeLe,
        UpdatedAt = record.ModifieLe,
    };

    /// <summary>Converts one campaign line row.</summary>
    /// <param name="record">PostgREST row of <c>public.v_campagne_ligne</c>.</param>
    public static CampaignLine ToDomain(CampaignLineRecord record) => new()
    {
        Id = record.Id,
        CampaignId = record.CampagneId,
        ItemId = record.ItemId,
        ItemClubCode = record.ItemCodeClub,
        ServiceType = CylinderServiceType.FromCode(record.TypePrestation),
        EstimatedCostEur = record.CoutEstimeEur,
        ActualCostEur = record.CoutReelEur,
        CertificateNumber = record.NumCertificat,
        Outcome = ToOutcome(record.Resultat),
        ReturnedOn = SqlDateConverter.ToDateOnly(record.DateRetourLigne),
    };

    /// <summary>Converts one pricing referential row.</summary>
    /// <param name="record">PostgREST row of <c>public.ref_tarif_apragaz</c>.</param>
    public static CylinderPricingRule? ToDomain(CylinderPricingRuleRecord record)
    {
        var serviceType = CylinderServiceType.FromCode(record.TypePrestation);
        if (serviceType is null)
        {
            return null;
        }

        if (SqlDateConverter.ToDateOnly(record.DateEffet) is not { } effectiveOn)
        {
            // date_effet is "not null" in the schema, so this can only mean the
            // row and the app have drifted out of sync — surfacing it as a
            // clear DataAccessException beats an opaque NullReferenceException
            // from the null-forgiving operator this replaces.
            throw new DataAccessException(
                $"Référentiel tarifaire incohérent : date d'effet manquante pour la prestation « {record.TypePrestation} » (id {record.Id}).");
        }

        return new CylinderPricingRule(serviceType, record.PrixEur, effectiveOn);
    }

    /// <summary>
    /// <c>resultat</c> is nullable (not yet pointed) unlike <c>statut</c>, so
    /// <c>null</c> is a legitimate value here — only a non-null, out-of-domain
    /// code is a real drift and throws.
    /// </summary>
    private static CampaignLineOutcome? ToOutcome(string? code) => code switch
    {
        null => null,
        "conforme" => CampaignLineOutcome.Passed,
        "echec" => CampaignLineOutcome.Failed,
        _ => throw new DataAccessException($"Résultat de campagne inconnu : « {code} »."),
    };

    /// <summary>Discriminator stored in <c>public.campagne_ligne.resultat</c>.</summary>
    /// <param name="outcome">Domain value to encode.</param>
    public static string ToCode(CampaignLineOutcome outcome) => outcome switch
    {
        CampaignLineOutcome.Passed => "conforme",
        CampaignLineOutcome.Failed => "echec",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Résultat de campagne inconnu."),
    };

    private static CampaignStatus ToStatus(string code) => code switch
    {
        "preparation" => CampaignStatus.Preparation,
        "envoyee" => CampaignStatus.Sent,
        "retournee" => CampaignStatus.Returned,
        // "statut" is CHECK-constrained to exactly these 3 values in
        // db/campagne.sql: an unrecognized code here means the app and the
        // database schema have drifted apart, not a legitimate unknown state
        // to fall back silently from — same reasoning as
        // CylinderReferenceType.Resolve for an out-of-domain material code.
        _ => throw new DataAccessException($"Statut de campagne inconnu : « {code} »."),
    };
}
