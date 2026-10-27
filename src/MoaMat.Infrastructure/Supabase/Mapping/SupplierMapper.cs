using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Suppliers;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps supplier records to the domain model and back.</summary>
internal static class SupplierMapper
{
    /// <summary>Builds a supplier from its row and the service codes it offers.</summary>
    /// <param name="record">PostgREST row of <c>public.ref_fournisseur</c>.</param>
    /// <param name="serviceCodes">Codes of the rows of <c>ref_fournisseur_prestation</c> for this supplier.</param>
    /// <remarks>Unknown codes are dropped; the services are returned in display order.</remarks>
    public static Supplier ToDomain(SupplierRecord record, IEnumerable<string> serviceCodes)
    {
        var codes = serviceCodes.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<CylinderServiceType> services =
            [.. CylinderServiceType.All.Where(service => codes.Contains(service.Code))];

        return new Supplier(
            record.Id,
            record.Nom,
            record.Adresse,
            record.Telephone,
            record.Email,
            record.PersonneContact,
            record.SiteWeb,
            record.Actif,
            services);
    }

    /// <summary>Builds the row to insert for a new supplier.</summary>
    /// <param name="draft">Validated contact details.</param>
    public static SupplierRecord ToRecord(SupplierDraft draft) => new()
    {
        Nom = draft.Name,
        Adresse = draft.Address,
        Telephone = draft.Phone,
        Email = draft.Email,
        PersonneContact = draft.ContactPerson,
        SiteWeb = draft.WebsiteUrl,
    };

    /// <summary>
    /// Converts a price row, or <c>null</c> when its service code is unknown
    /// (a code added to the schema before the application knows it).
    /// </summary>
    /// <param name="record">PostgREST row of <c>public.ref_fournisseur_tarif</c>.</param>
    public static SupplierTariff? ToDomain(SupplierTariffRecord record) =>
        CylinderServiceType.FromCode(record.TypePrestation) is { } service
            ? new SupplierTariff(
                record.Id,
                record.FournisseurId,
                service,
                record.PrixEur,
                DateOnly.FromDateTime(record.DateEffet))
            : null;

    /// <summary>Builds the row to insert for a new price.</summary>
    /// <param name="supplierId">Owning supplier.</param>
    /// <param name="draft">Validated price and effective date.</param>
    public static SupplierTariffRecord ToRecord(long supplierId, SupplierTariffDraft draft) => new()
    {
        FournisseurId = supplierId,
        TypePrestation = draft.Service.Code,
        PrixEur = draft.PriceEur,
        DateEffet = SqlDateConverter.ToDateTime(draft.EffectiveOn)!.Value,
    };
}
