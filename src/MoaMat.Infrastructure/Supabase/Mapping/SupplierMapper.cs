using MoaMat.Domain.Common;
using MoaMat.Domain.Suppliers;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>
/// Maps <see cref="SupplierRecord"/>, <see cref="SupplierServiceRecord"/> and
/// <see cref="SupplierServiceTariffRecord"/> to their domain equivalents and
/// back, including the control / bath enums and the codes stored in the
/// database.
/// </summary>
internal static class SupplierMapper
{
    /// <summary>Converts one supplier row.</summary>
    /// <param name="record">PostgREST row of <c>public.ref_fournisseur</c>.</param>
    public static Supplier ToDomain(SupplierRecord record) => new(
        record.Id,
        record.Nom,
        record.Adresse,
        record.Telephone,
        record.Email,
        record.PersonneContact,
        record.SiteWeb,
        record.Actif);

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

    /// <summary>Converts one service row plus its already-fetched tariffs.</summary>
    /// <param name="record">PostgREST row of <c>public.fournisseur_prestation</c>.</param>
    /// <param name="tariffs">Tariffs of this service, already mapped.</param>
    public static SupplierService ToDomain(SupplierServiceRecord record, IReadOnlyList<ServicePriceRule> tariffs) => new()
    {
        Id = record.Id,
        SupplierId = record.FournisseurId,
        Name = record.Libelle,
        Control = ToControl(record.ControleCylindre),
        Method = ToMethod(record.MethodeHydraulique),
        IsActive = record.Actif,
        Comment = record.Commentaire,
        Tariffs = tariffs,
    };

    /// <summary>Converts one tariff row.</summary>
    /// <param name="record">PostgREST row of <c>public.fournisseur_prestation_tarif</c>.</param>
    public static ServicePriceRule ToDomain(SupplierServiceTariffRecord record)
    {
        if (SqlDateConverter.ToDateOnly(record.DateEffet) is not { } effectiveOn)
        {
            // date_effet is "not null" in the schema, so this can only mean the
            // row and the app have drifted out of sync.
            throw new DataAccessException(
                $"Tarif incohérent : date d'effet manquante pour la prestation {record.PrestationId} (tarif {record.Id}).");
        }

        return new ServicePriceRule(record.PrestationId, record.PrixEur, effectiveOn, record.Id);
    }

    /// <summary>
    /// <c>controle_cylindre</c> is nullable (free service), so <c>null</c> is
    /// legitimate — only a non-null, out-of-domain code is a real drift.
    /// </summary>
    /// <param name="code">Raw database code.</param>
    public static CylinderControl? ToControl(string? code) => code switch
    {
        null => null,
        "optique" => CylinderControl.Optical,
        "hydraulique" => CylinderControl.Hydraulic,
        _ => throw new DataAccessException($"Type de contrôle de prestation inconnu : « {code} »."),
    };

    /// <summary>
    /// <c>methode_hydraulique</c> is nullable (non-hydraulic service) — only a
    /// non-null, out-of-domain code is a real drift.
    /// </summary>
    /// <param name="code">Raw database code.</param>
    public static HydraulicMethod? ToMethod(string? code) => code switch
    {
        null => null,
        "huile" => HydraulicMethod.Oil,
        "eau" => HydraulicMethod.Water,
        _ => throw new DataAccessException($"Méthode hydraulique inconnue : « {code} »."),
    };

    /// <summary>Database code of a control; <c>null</c> for a free service.</summary>
    /// <param name="control">Domain value to encode.</param>
    public static string? ToCode(CylinderControl? control) => control switch
    {
        null => null,
        CylinderControl.Optical => "optique",
        CylinderControl.Hydraulic => "hydraulique",
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, "Type de contrôle inconnu."),
    };

    /// <summary>Database code of a bath; <c>null</c> when none applies.</summary>
    /// <param name="method">Domain value to encode.</param>
    public static string? ToCode(HydraulicMethod? method) => method switch
    {
        null => null,
        HydraulicMethod.Oil => "huile",
        HydraulicMethod.Water => "eau",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Méthode hydraulique inconnue."),
    };
}
