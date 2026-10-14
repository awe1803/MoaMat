namespace MoaMat.Domain.Regulators;

/// <summary>
/// One entry of a regulator's timeline: an intervention recording the 5
/// control points. Imported history and entries recorded in the application
/// share the same shape.
/// </summary>
public sealed record RegulatorEvent
{
    /// <summary>Identifier of the entry.</summary>
    public required long Id { get; init; }

    /// <summary>Day the intervention happened; <c>null</c> when the legacy row carried no date.</summary>
    public DateOnly? OccurredOn { get; init; }

    /// <summary>The 5 control points.</summary>
    public required RegulatorControlPoints ControlPoints { get; init; }

    /// <summary>Service provider, when known (never recorded in the legacy Access history).</summary>
    public string? Provider { get; init; }

    /// <summary>Cost billed, when known.</summary>
    public decimal? CostEur { get; init; }

    /// <summary>Free-text remark.</summary>
    public string? Remark { get; init; }

    /// <summary>
    /// True for imported legacy history recorded without an actual control
    /// (the "NO CTL" drift, ticket "reprise détendeurs") — never true for an
    /// intervention recorded through <see cref="RegulatorInterventionRequest"/>.
    /// </summary>
    public bool IsLegacyGap => !ControlPoints.IsComplete;
}
