namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Which control counter of a bottle a supplier service feeds, mirroring
/// <c>public.fournisseur_prestation.controle_cylindre</c>
/// (<c>db/fournisseur_prestation.sql</c>). A service without a control is a
/// free service (no bottle counter attached), represented by a <c>null</c>
/// <see cref="SupplierService.Control"/>.
/// </summary>
public enum CylinderControl
{
    /// <summary>Optical (visual) control — feeds the optical counter.</summary>
    Optical,

    /// <summary>Hydraulic requalification — feeds the hydraulic counter; always carries a <see cref="HydraulicMethod"/>.</summary>
    Hydraulic,
}
