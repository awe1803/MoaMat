using System.Globalization;
using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Suppliers;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="ISupplierRepository"/>.
/// </summary>
/// <remarks>
/// Rights are enforced by the database on the <c>fournisseur</c> permission
/// domain, never by this adapter: a refused write comes back as a failed
/// <see cref="OperationResult"/>. Contact details (<c>public.ref_fournisseur</c>,
/// <c>db/fournisseur.sql</c>) are written directly under RLS; the service
/// catalogue and its tariffs (<c>db/fournisseur_prestation.sql</c>) have no
/// insert/update/delete policy and are written through <c>SECURITY DEFINER</c>
/// functions only. Prices are append-only; the only way to change one is
/// <see cref="AddTariffAsync"/> with a later effective date.
/// </remarks>
internal sealed class SupabaseSupplierRepository : ISupplierRepository
{
    private const string ReadFailureMessage = "Lecture des fournisseurs impossible.";
    private const string CreateRefusedMessage = "Création refusée (droits insuffisants ou nom déjà utilisé).";
    private const string UpdateRefusedMessage = "Modification refusée (droits insuffisants ou nom déjà utilisé).";

    private const string DeleteSupplierRefusedMessage =
        "Suppression du fournisseur refusée (droits insuffisants, ou fournisseur déjà utilisé dans une campagne : archivez-le à la place).";

    private const string ServiceRefusedMessage =
        "Enregistrement de la prestation refusé (droits insuffisants, libellé déjà utilisé chez ce fournisseur, ou type de contrôle figé car la prestation a déjà servi en campagne).";

    private const string DeleteServiceRefusedMessage =
        "Suppression de la prestation refusée (droits insuffisants, prestation déjà utilisée en campagne, ou des prix existent : supprimez-les d'abord ou désactivez la prestation).";

    private const string TariffRefusedMessage =
        "Prix refusé (droits insuffisants, montant négatif ou prix déjà saisi à cette date d'effet).";

    private const string DeleteTariffRefusedMessage = "Suppression du prix refusée (droits insuffisants).";

    /// <summary>
    /// Upper bounds of the lists — a browsing surface, not an export. A club has
    /// a handful of suppliers and a few services each; the bounds only protect
    /// against a runaway table.
    /// </summary>
    private const int MaxSuppliers = 500;

    private const int MaxServices = 500;

    private const int MaxTariffsPerService = 20;

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseSupplierRepository(global::Supabase.Client client, ILogger<SupabaseSupplierRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetSuppliersAsync),
            ReadFailureMessage,
            async () =>
            {
                var suppliers = await _client
                    .From<SupplierRecord>()
                    .Order("nom", Constants.Ordering.Ascending)
                    .Limit(MaxSuppliers)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<Supplier>)[.. suppliers.Models.Select(SupplierMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddSupplierAsync(
        SupplierDraft draft,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(AddSupplierAsync),
            CreateRefusedMessage,
            () => _client.From<SupplierRecord>()
                .Insert(SupplierMapper.ToRecord(draft), cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> UpdateSupplierAsync(
        long supplierId,
        SupplierDraft draft,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(UpdateSupplierAsync),
            UpdateRefusedMessage,
            () => _client.From<SupplierRecord>()
                .Where(record => record.Id == supplierId)
                .Set(record => record.Nom, draft.Name)
                .Set(record => record.Adresse!, draft.Address)
                .Set(record => record.Telephone!, draft.Phone)
                .Set(record => record.Email!, draft.Email)
                .Set(record => record.PersonneContact!, draft.ContactPerson)
                .Set(record => record.SiteWeb!, draft.WebsiteUrl)
                .Update(cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> SetSupplierActiveAsync(
        long supplierId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(SetSupplierActiveAsync),
            UpdateRefusedMessage,
            () => _client.From<SupplierRecord>()
                .Where(record => record.Id == supplierId)
                .Set(record => record.Actif, isActive)
                .Update(cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteSupplierAsync(long supplierId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteSupplierAsync),
            DeleteSupplierRefusedMessage,
            () => _client.Rpc("supprimer_fournisseur", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_id"] = supplierId,
            }),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SupplierService>> GetServicesAsync(
        long supplierId,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetServicesAsync),
            ReadFailureMessage,
            async () =>
            {
                var services = await _client.From<SupplierServiceRecord>()
                    .Filter("fournisseur_id", Constants.Operator.Equals, supplierId)
                    .Order("libelle", Constants.Ordering.Ascending)
                    .Limit(MaxServices)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var serviceIds = services.Models.Select(service => service.Id).ToArray();

                // Filtered to exactly the services just fetched; ordered and
                // bounded like every other list read here.
                var tariffs = serviceIds.Length == 0
                    ? new List<SupplierServiceTariffRecord>()
                    : (await _client.From<SupplierServiceTariffRecord>()
                        .Filter("prestation_id", Constants.Operator.In, serviceIds)
                        .Order("date_effet", Constants.Ordering.Ascending)
                        .Limit(MaxServices * MaxTariffsPerService)
                        .Get(cancellationToken)
                        .ConfigureAwait(false)).Models;

                var tariffsByService = tariffs
                    .Select(SupplierMapper.ToDomain)
                    .GroupBy(rule => rule.ServiceId)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<ServicePriceRule>)[.. group]);

                return (IReadOnlyList<SupplierService>)
                [
                    .. services.Models
                        .Select(record => SupplierMapper.ToDomain(
                            record,
                            tariffsByService.GetValueOrDefault(record.Id, [])))
                        .OrderByDescending(service => service.IsActive)
                        .ThenBy(service => service.Name, StringComparer.CurrentCultureIgnoreCase),
                ];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<long>> SaveServiceAsync(
        SupplierServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _guard.WriteValueAsync(
            nameof(SaveServiceAsync),
            ServiceRefusedMessage,
            () => _client.Rpc<long>(
                "enregistrer_prestation_fournisseur",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["p_id"] = request.ServiceId,
                    ["p_fournisseur_id"] = request.SupplierId,
                    ["p_libelle"] = request.Name,
                    ["p_controle_cylindre"] = SupplierMapper.ToCode(request.Control),
                    ["p_methode_hydraulique"] = SupplierMapper.ToCode(request.Method),
                    ["p_actif"] = request.IsActive,
                    ["p_commentaire"] = request.Comment,
                }),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> DeleteServiceAsync(long serviceId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteServiceAsync),
            DeleteServiceRefusedMessage,
            () => _client.Rpc("supprimer_prestation_fournisseur", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_id"] = serviceId,
            }),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddTariffAsync(
        SupplierTariffDraft draft,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(AddTariffAsync),
            TariffRefusedMessage,
            () => _client.Rpc("definir_tarif_prestation", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_prestation_id"] = draft.ServiceId,
                ["p_prix_eur"] = draft.PriceEur,
                ["p_date_effet"] = draft.EffectiveOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteTariffAsync(long tariffId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteTariffAsync),
            DeleteTariffRefusedMessage,
            () => _client.Rpc("supprimer_tarif_prestation", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_tarif_id"] = tariffId,
            }),
            cancellationToken);
}
