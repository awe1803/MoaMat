namespace MoaMat.Domain.Regulators;

/// <summary>
/// Characteristics specific to a regulator (<c>public.item_detendeur</c>), the
/// part of the fiche that the generic <see cref="Inventory.InventoryItem"/>
/// does not carry.
/// </summary>
public sealed record RegulatorDetails
{
    /// <summary>Technical identifier of the parent item.</summary>
    public required long ItemId { get; init; }

    /// <summary>Connection type (étrier / DIN), as recorded.</summary>
    public string? ConnectionType { get; init; }

    /// <summary>Whether the set has a first stage.</summary>
    public bool? HasFirstStage { get; init; }

    /// <summary>First stage model.</summary>
    public string? FirstStageModel { get; init; }

    /// <summary>First stage serial number.</summary>
    public string? FirstStageSerial { get; init; }

    /// <summary>Whether the set has a (second stage) primary demand valve.</summary>
    public bool? HasSecondStage { get; init; }

    /// <summary>Second stage model.</summary>
    public string? SecondStageModel { get; init; }

    /// <summary>Second stage serial number.</summary>
    public string? SecondStageSerial { get; init; }

    /// <summary>Whether the set has an octopus (alternate second stage).</summary>
    public bool? HasOctopus { get; init; }

    /// <summary>Octopus model.</summary>
    public string? OctopusModel { get; init; }

    /// <summary>Octopus serial number.</summary>
    public string? OctopusSerial { get; init; }

    /// <summary>Whether the set has an inflator hose.</summary>
    public bool? HasInflator { get; init; }

    /// <summary>Whether the set has a pressure gauge.</summary>
    public bool? HasGauge { get; init; }

    /// <summary>Whether the set is cleared for loan/use.</summary>
    public bool? IsAuthorizedForUse { get; init; }

    /// <summary>Whether the set is reserved for children's use.</summary>
    public bool? IsReservedForChildren { get; init; }

    /// <summary>Whether the 5 control points have ever been performed (at least once), per the legacy mirror snapshot.</summary>
    public bool? ControlPerformed { get; init; }

    /// <summary>
    /// Last control date as recorded in the legacy Access mirror
    /// (<c>detendeur.date_dernier_controle</c>) — a snapshot, never updated by
    /// <see cref="RegulatorInterventionRequest"/>. Informational only; the due
    /// date is driven by <see cref="LastInterventionOn"/> instead.
    /// </summary>
    public DateOnly? LastKnownControlOn { get; init; }

    /// <summary>
    /// Date of the most recent intervention known to MoaMat (imported history
    /// or recorded through the application) — the counter the due date is
    /// computed from (<see cref="RegulatorDueDateEngine"/>, mirrors
    /// <c>public.item_detendeur.date_derniere_intervention</c>). <c>null</c>
    /// until at least one intervention is known, never guessed from the
    /// acquisition date.
    /// </summary>
    public DateOnly? LastInterventionOn { get; init; }

    /// <summary>Equipment composition summary, e.g. "Complet · mano, octopus, inflateur".</summary>
    public string DescribeComposition()
    {
        var parts = new List<string>();
        if (HasGauge == true)
        {
            parts.Add("mano");
        }

        if (HasOctopus == true)
        {
            parts.Add("octopus");
        }

        if (HasInflator == true)
        {
            parts.Add("inflateur");
        }

        if (parts.Count == 0)
        {
            return HasSecondStage == true ? "Détendeur seul" : "Composition inconnue";
        }

        var isComplete = HasGauge == true && HasOctopus == true && HasInflator == true;
        return isComplete ? "Complet · " + string.Join(", ", parts) : string.Join(", ", parts);
    }
}
