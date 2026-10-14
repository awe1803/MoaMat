namespace MoaMat.Domain.Regulators;

/// <summary>
/// Outcome of one of the 5 control points of a regulator intervention
/// (<c>db/detendeur_evenement.sql</c>).
/// </summary>
public enum RegulatorControlResult
{
    /// <summary>Control carried out, nothing to report.</summary>
    Ok,

    /// <summary>Control carried out, a problem was found.</summary>
    NotOk,

    /// <summary>This control point does not apply to this regulator (e.g. no octopus fitted).</summary>
    NotApplicable,

    /// <summary>
    /// Imported legacy history only: the control was recorded without actually
    /// being performed (the "NO CTL" drift the ticket corrects — 48 of the 115
    /// historical interventions). Never a valid choice for a new intervention:
    /// <see cref="RegulatorInterventionRequest.Create"/> rejects it, which is
    /// what makes the 5 points mandatory in practice.
    /// </summary>
    NotControlled,
}
