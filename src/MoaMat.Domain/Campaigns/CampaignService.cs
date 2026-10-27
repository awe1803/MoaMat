using MoaMat.Domain.Suppliers;

namespace MoaMat.Domain.Campaigns;

/// <summary>
/// The supplier service billed on a <see cref="CampaignLine"/>, as read from
/// <c>public.v_campagne_ligne</c> — enough to display it and to know which
/// bottle counter it feeds, without loading the whole supplier catalogue.
/// </summary>
/// <param name="Id">Identifier of the <c>public.fournisseur_prestation</c>.</param>
/// <param name="Name">Label of the service.</param>
/// <param name="Control">Bottle counter fed by this service.</param>
/// <param name="Method">Oil or water bath, for a hydraulic service.</param>
public sealed record CampaignService(long Id, string Name, CylinderControl? Control, HydraulicMethod? Method)
{
    /// <summary>Label of the control and, for hydraulic, the bath (e.g. "Hydraulique · huile").</summary>
    public string ControlLabel => SupplierService.ControlLabelFor(Control, Method);
}
