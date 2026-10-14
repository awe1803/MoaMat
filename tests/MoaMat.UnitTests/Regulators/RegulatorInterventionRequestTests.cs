using MoaMat.Domain.Common;
using MoaMat.Domain.Regulators;

namespace MoaMat.UnitTests.Regulators;

/// <summary>
/// <see cref="RegulatorInterventionRequest.Create"/> is what makes the 5
/// control points mandatory on the client side — the database enforces the
/// same rule again (<c>public.enregistrer_intervention_detendeur</c>). The
/// ticket's acceptance criterion: saisie bloquée tant que les 5 points n'ont
/// pas été renseignés.
/// </summary>
public sealed class RegulatorInterventionRequestTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static OperationResult<RegulatorInterventionRequest> Create(
        RegulatorControlResult? gauge = RegulatorControlResult.Ok,
        RegulatorControlResult? stages = RegulatorControlResult.Ok,
        RegulatorControlResult? octopus = RegulatorControlResult.Ok,
        RegulatorControlResult? inflator = RegulatorControlResult.Ok,
        RegulatorControlResult? hoseVisual = RegulatorControlResult.Ok,
        DateOnly? performedOn = null,
        decimal? costEur = null) =>
        RegulatorInterventionRequest.Create(
            1, performedOn ?? Today, gauge, stages, octopus, inflator, hoseVisual,
            null, costEur, null, Today, Guid.NewGuid());

    [Fact]
    public void Create_succeeds_when_all_5_points_are_a_real_control()
    {
        var result = Create();

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.ControlPoints.IsComplete);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void Create_fails_when_any_single_point_is_missing(
        bool missingGauge, bool missingStages, bool missingOctopus, bool missingInflator, bool missingHoseVisual)
    {
        var result = Create(
            gauge: missingGauge ? null : RegulatorControlResult.Ok,
            stages: missingStages ? null : RegulatorControlResult.Ok,
            octopus: missingOctopus ? null : RegulatorControlResult.Ok,
            inflator: missingInflator ? null : RegulatorControlResult.Ok,
            hoseVisual: missingHoseVisual ? null : RegulatorControlResult.Ok);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Create_fails_when_a_point_is_NotControlled_the_legacy_NO_CTL_drift()
    {
        var result = Create(gauge: RegulatorControlResult.NotControlled);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Create_accepts_NotOk_and_NotApplicable_as_real_controls()
    {
        var result = Create(octopus: RegulatorControlResult.NotApplicable, stages: RegulatorControlResult.NotOk);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Create_fails_when_date_is_in_the_future()
    {
        var result = Create(performedOn: Today.AddDays(1));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Create_fails_when_cost_is_negative()
    {
        var result = Create(costEur: -1);

        Assert.False(result.Succeeded);
    }
}
