using MoaMat.Domain.Campaigns;

namespace MoaMat.Domain.Cylinders;

/// <summary>
/// One entry of a cylinder's timeline: a requalification, an incident, the
/// commissioning… Imported history and entries recorded in the application
/// share the same shape.
/// </summary>
public sealed record CylinderEvent
{
    /// <summary>Identifier of the entry.</summary>
    public required long Id { get; init; }

    /// <summary>Kind of entry.</summary>
    public required CylinderEventType Type { get; init; }

    /// <summary>
    /// Outcome of a requalification. <c>null</c> for imported history — the
    /// legacy database never recorded it in a usable way — and for entries
    /// that are not requalifications.
    /// </summary>
    public CampaignLineOutcome? Outcome { get; init; }

    /// <summary>Day the event happened; <c>null</c> when the legacy row carried no date.</summary>
    public DateOnly? OccurredOn { get; init; }

    /// <summary>Inspection body, when known.</summary>
    public string? Provider { get; init; }

    /// <summary>Cost billed, when known.</summary>
    public decimal? CostEur { get; init; }

    /// <summary>Certificate number, when any.</summary>
    public string? CertificateNumber { get; init; }

    /// <summary>Next due date recorded with the event (legacy history only).</summary>
    public DateOnly? NextDueOn { get; init; }

    /// <summary>Free-text remark or incident description.</summary>
    public string? Remark { get; init; }

    /// <summary>Human label of the event kind, e.g. "Contrôle optique".</summary>
    public string Label => Type switch
    {
        CylinderEventType.Commissioning => "Mise en service",
        CylinderEventType.OpticalControl => "Contrôle optique",
        CylinderEventType.HydraulicControl => "Épreuve hydraulique",
        CylinderEventType.Withdrawn => "Écartée (défectuosité)",
        CylinderEventType.Scrapped => "Rebutée",
        CylinderEventType.Incident => "Incident",
        CylinderEventType.Unknown => "Événement",
        _ => Type.ToString(),
    };

    /// <summary>Label of the kind, followed by the outcome when there is one ("Contrôle optique — conforme").</summary>
    public string Title => Outcome switch
    {
        CampaignLineOutcome.Passed => $"{Label} — conforme",
        CampaignLineOutcome.Failed => $"{Label} — échec",
        _ => Label,
    };

    /// <summary>True when the event is a bad news: failed control, withdrawal, scrapping or incident.</summary>
    public bool IsAdverse =>
        Outcome is CampaignLineOutcome.Failed
        || Type is CylinderEventType.Withdrawn or CylinderEventType.Scrapped or CylinderEventType.Incident;
}
