namespace MoaMat.Domain.Regulators;

/// <summary>
/// The 5 control points of one regulator intervention (<c>db/detendeur_evenement.sql</c>).
/// All 5 are mandatory for a new intervention (<see cref="RegulatorInterventionRequest.Create"/>);
/// nullable here only because imported legacy history occasionally carries a
/// blank value (2 of the 115 rows, a data anomaly — not the "NO CTL" drift,
/// which is <see cref="RegulatorControlResult.NotControlled"/>).
/// </summary>
public sealed record RegulatorControlPoints
{
    /// <summary>Pressure gauge (manomètre).</summary>
    public RegulatorControlResult? Gauge { get; init; }

    /// <summary>First/second stage breathing test (détendeur).</summary>
    public RegulatorControlResult? Stages { get; init; }

    /// <summary>Octopus (alternate second stage).</summary>
    public RegulatorControlResult? Octopus { get; init; }

    /// <summary>Inflator (direct feed to the BCD).</summary>
    public RegulatorControlResult? Inflator { get; init; }

    /// <summary>Visual inspection of the hoses.</summary>
    public RegulatorControlResult? HoseVisual { get; init; }

    /// <summary>True when every point is a real control (never <see cref="RegulatorControlResult.NotControlled"/> nor missing).</summary>
    public bool IsComplete =>
        IsReal(Gauge) && IsReal(Stages) && IsReal(Octopus) && IsReal(Inflator) && IsReal(HoseVisual);

    /// <summary>True when at least one point was found not OK.</summary>
    public bool HasAnyFailure =>
        Gauge == RegulatorControlResult.NotOk
        || Stages == RegulatorControlResult.NotOk
        || Octopus == RegulatorControlResult.NotOk
        || Inflator == RegulatorControlResult.NotOk
        || HoseVisual == RegulatorControlResult.NotOk;

    private static bool IsReal(RegulatorControlResult? result) =>
        result is RegulatorControlResult.Ok or RegulatorControlResult.NotOk or RegulatorControlResult.NotApplicable;
}
