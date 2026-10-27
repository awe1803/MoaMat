namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Bath used for a hydraulic requalification, mirroring
/// <c>public.fournisseur_prestation.methode_hydraulique</c>.
/// </summary>
/// <remarks>
/// Oil and water are two distinct services with their own tariffs on purpose:
/// the price gap between them (roughly 13 to 15 € depending on the year) is a
/// real fact of the supplier referential, never a data entry mistake to merge
/// away. Neither is ever picked automatically — the manager chooses one
/// explicitly, per bottle, before a campaign can be sent.
/// </remarks>
public enum HydraulicMethod
{
    /// <summary>Oil bath (<c>huile</c>).</summary>
    Oil,

    /// <summary>Water bath (<c>eau</c>).</summary>
    Water,
}
