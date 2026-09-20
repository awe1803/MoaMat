namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Kind of entry on a cylinder's timeline, mirroring
/// <c>public.bouteille_evenement.type</c> (<c>db/bouteille_evenement.sql</c>).
/// </summary>
public enum CylinderEventType
{
    /// <summary>Commissioning of the cylinder (<c>mise_en_service</c>).</summary>
    Commissioning = 0,

    /// <summary>Optical inspection (<c>controle_optique</c>).</summary>
    OpticalControl = 1,

    /// <summary>Hydraulic test (<c>controle_hydraulique</c>).</summary>
    HydraulicControl = 2,

    /// <summary>Withdrawn from service after a defect (<c>ecartee</c>).</summary>
    Withdrawn = 3,

    /// <summary>Scrapped (<c>rebut</c>).</summary>
    Scrapped = 4,

    /// <summary>Incident reported on the cylinder (<c>incident</c>).</summary>
    Incident = 5,

    /// <summary>A type this version of the application does not know (added in the database later).</summary>
    Unknown = 99,
}
