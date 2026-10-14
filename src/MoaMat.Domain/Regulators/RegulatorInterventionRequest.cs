using MoaMat.Domain.Common;

namespace MoaMat.Domain.Regulators;

/// <summary>
/// An intervention to record on one regulator. Only <see cref="Create"/>
/// builds one, so an instance always satisfies the invariants below (the
/// database enforces the same rules again) — in particular, the 5 control
/// points are all present and are a real control
/// (<see cref="RegulatorControlResult.Ok"/>, <see cref="RegulatorControlResult.NotOk"/>
/// or <see cref="RegulatorControlResult.NotApplicable"/>): this is what makes
/// the "NO CTL" drift (ticket "reprise détendeurs") impossible to reproduce
/// from the application.
/// </summary>
public sealed record RegulatorInterventionRequest
{
    private RegulatorInterventionRequest()
    {
    }

    /// <summary>Regulator being controlled.</summary>
    public long ItemId { get; private init; }

    /// <summary>Day of the intervention; never after the reference day it was created against.</summary>
    public DateOnly PerformedOn { get; private init; }

    /// <summary>The 5 control points — all mandatory, all a real control result.</summary>
    public RegulatorControlPoints ControlPoints { get; private init; } = new();

    /// <summary>Service provider, when given.</summary>
    public string? Provider { get; private init; }

    /// <summary>Cost billed, when given; never negative.</summary>
    public decimal? CostEur { get; private init; }

    /// <summary>Free-text remark, when given.</summary>
    public string? Remark { get; private init; }

    /// <summary>
    /// Idempotency key: submitting the same request twice (a retry after a
    /// network timeout) records one entry, not two.
    /// </summary>
    public Guid RequestId { get; private init; }

    /// <summary>Validates and builds a request.</summary>
    /// <param name="itemId">Regulator being controlled.</param>
    /// <param name="performedOn">Day of the intervention.</param>
    /// <param name="gauge">Pressure gauge control result.</param>
    /// <param name="stages">First/second stage breathing test result.</param>
    /// <param name="octopus">Octopus control result.</param>
    /// <param name="inflator">Inflator control result.</param>
    /// <param name="hoseVisual">Hose visual inspection result.</param>
    /// <param name="provider">Service provider, optional.</param>
    /// <param name="costEur">Cost billed, optional.</param>
    /// <param name="remark">Remark, optional.</param>
    /// <param name="today">Reference day the date is checked against.</param>
    /// <param name="requestId">Idempotency key generated when the form was opened.</param>
    /// <returns>The request, or the reason it was refused.</returns>
    public static OperationResult<RegulatorInterventionRequest> Create(
        long itemId,
        DateOnly performedOn,
        RegulatorControlResult? gauge,
        RegulatorControlResult? stages,
        RegulatorControlResult? octopus,
        RegulatorControlResult? inflator,
        RegulatorControlResult? hoseVisual,
        string? provider,
        decimal? costEur,
        string? remark,
        DateOnly today,
        Guid requestId)
    {
        if (performedOn > today)
        {
            return OperationResult<RegulatorInterventionRequest>.Failure(
                "La date de l'intervention ne peut pas être dans le futur.");
        }

        var points = new RegulatorControlPoints
        {
            Gauge = gauge,
            Stages = stages,
            Octopus = octopus,
            Inflator = inflator,
            HoseVisual = hoseVisual,
        };

        if (!points.IsComplete)
        {
            return OperationResult<RegulatorInterventionRequest>.Failure(
                "Les 5 points de contrôle (manomètre, détendeur, octopus, inflateur, visuel flexibles) sont obligatoires.");
        }

        if (costEur is < 0)
        {
            return OperationResult<RegulatorInterventionRequest>.Failure("Le coût ne peut pas être négatif.");
        }

        return OperationResult<RegulatorInterventionRequest>.Success(new RegulatorInterventionRequest
        {
            ItemId = itemId,
            PerformedOn = performedOn,
            ControlPoints = points,
            Provider = NullIfBlank(provider),
            CostEur = costEur,
            Remark = NullIfBlank(remark),
            RequestId = requestId,
        });
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
