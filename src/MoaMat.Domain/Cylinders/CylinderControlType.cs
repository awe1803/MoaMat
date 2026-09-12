namespace MoaMat.Domain.Cylinders;

/// <summary>
/// The two independent requalification controls a cylinder is subject to.
/// </summary>
/// <remarks>
/// The two counters never alternate on a hard-coded schedule: each one is
/// resolved on its own against <see cref="CylinderPeriodicityRule"/>, and a
/// cylinder profile may have no rule at all for one of them (e.g. a carbon
/// cylinder has no optical control — see <see cref="CylinderReferenceType"/>).
/// </remarks>
public enum CylinderControlType
{
    /// <summary>Visual inspection (<c>optique</c>).</summary>
    Optical = 0,

    /// <summary>Hydraulic requalification test (<c>hydraulique</c>).</summary>
    Hydraulic = 1,
}
