using MoaMat.Domain.Regulators;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>
/// Maps <see cref="RegulatorDetailRecord"/> and <see cref="RegulatorEventRecord"/>
/// to their domain equivalents, and the domain control codes back to the
/// database ones.
/// </summary>
internal static class RegulatorMapper
{
    /// <summary>Converts one <c>item_detendeur</c> row.</summary>
    /// <param name="record">PostgREST row.</param>
    public static RegulatorDetails ToDomain(RegulatorDetailRecord record) => new()
    {
        ItemId = record.ItemId,
        ConnectionType = record.TypeConnexion,
        HasFirstStage = record.APremierEtage,
        FirstStageModel = record.ModelePremierEtage,
        FirstStageSerial = record.NumSeriePremierEtage,
        HasSecondStage = record.ASecondEtage,
        SecondStageModel = record.ModeleSecondEtage,
        SecondStageSerial = record.NumSerieSecondEtage,
        HasOctopus = record.AOctopus,
        OctopusModel = record.ModeleOctopus,
        OctopusSerial = record.NumSerieOctopus,
        HasInflator = record.AInflateur,
        HasGauge = record.AManometre,
        IsAuthorizedForUse = record.SortieAutorisee,
        IsReservedForChildren = record.ReserveEnfants,
        ControlPerformed = record.ControleEffectue,
        LastKnownControlOn = SqlDateConverter.ToDateOnly(record.DateDernierControle),
        LastInterventionOn = SqlDateConverter.ToDateOnly(record.DateDerniereIntervention),
    };

    /// <summary>Converts one <c>detendeur_evenement</c> row.</summary>
    /// <param name="record">PostgREST row.</param>
    public static RegulatorEvent ToDomain(RegulatorEventRecord record) => new()
    {
        Id = record.Id,
        OccurredOn = SqlDateConverter.ToDateOnly(record.DateEvenement),
        ControlPoints = new RegulatorControlPoints
        {
            Gauge = ToResult(record.CtrlManometre),
            Stages = ToResult(record.CtrlDetendeur),
            Octopus = ToResult(record.CtrlOctopus),
            Inflator = ToResult(record.CtrlInflateur),
            HoseVisual = ToResult(record.CtrlVisuelFlexibles),
        },
        Provider = record.Prestataire,
        CostEur = record.CoutEur,
        Remark = record.Remarque,
    };

    /// <summary>Database code of a control result accepted by <c>enregistrer_intervention_detendeur</c>.</summary>
    /// <param name="result">Control result. <see cref="RegulatorControlResult.NotControlled"/> has no code: a new intervention never writes it.</param>
    public static string ToCode(RegulatorControlResult result) => result switch
    {
        RegulatorControlResult.Ok => "ok",
        RegulatorControlResult.NotOk => "non_ok",
        RegulatorControlResult.NotApplicable => "sans_objet",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Not a recordable control result."),
    };

    private static RegulatorControlResult? ToResult(string? code) => code switch
    {
        "ok" => RegulatorControlResult.Ok,
        "non_ok" => RegulatorControlResult.NotOk,
        "sans_objet" => RegulatorControlResult.NotApplicable,
        "no_ctl" => RegulatorControlResult.NotControlled,
        _ => null,
    };
}
