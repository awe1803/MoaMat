using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Suppliers;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="ISupplierRepository"/>.
/// </summary>
/// <remarks>
/// Rights are enforced by RLS on the <c>fournisseur</c> permission domain, never
/// by this adapter: a refused write comes back as a failed
/// <see cref="OperationResult"/>. Prices are append-only; the only way to change
/// one is <see cref="AddTariffAsync"/> with a later effective date.
/// </remarks>
internal sealed class SupabaseSupplierRepository : ISupplierRepository
{
    private const string ReadFailureMessage = "Lecture des fournisseurs impossible.";
    private const string CreateRefusedMessage = "Création refusée (droits insuffisants ou nom déjà utilisé).";
    private const string UpdateRefusedMessage = "Modification refusée (droits insuffisants ou nom déjà utilisé).";
    private const string DeleteRefusedMessage = "Suppression refusée (droits insuffisants).";
    private const string TariffRefusedMessage =
        "Prix refusé (droits insuffisants, prestation non proposée ou prix déjà saisi à cette date).";

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
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var services = await _client
                    .From<SupplierServiceRecord>()
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                var codesBySupplier = services.Models
                    .GroupBy(record => record.FournisseurId)
                    .ToDictionary(group => group.Key, group => group.Select(record => record.TypePrestation).ToArray());

                return (IReadOnlyList<Supplier>)
                [
                    .. suppliers.Models.Select(record => SupplierMapper.ToDomain(
                        record,
                        codesBySupplier.GetValueOrDefault(record.Id) ?? [])),
                ];
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
            DeleteRefusedMessage,
            () => _client.From<SupplierRecord>()
                .Where(record => record.Id == supplierId)
                .Delete(cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddServiceAsync(
        long supplierId,
        CylinderServiceType service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        return _guard.WriteAsync(
            nameof(AddServiceAsync),
            UpdateRefusedMessage,
            () => _client.From<SupplierServiceRecord>()
                .Insert(
                    new SupplierServiceRecord { FournisseurId = supplierId, TypePrestation = service.Code },
                    cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> RemoveServiceAsync(
        long supplierId,
        CylinderServiceType service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        var code = service.Code;

        return _guard.WriteAsync(
            nameof(RemoveServiceAsync),
            "Retrait refusé (droits insuffisants ou des prix existent pour cette prestation).",
            () => _client.From<SupplierServiceRecord>()
                .Where(record => record.FournisseurId == supplierId && record.TypePrestation == code)
                .Delete(cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SupplierTariff>> GetTariffsAsync(
        long supplierId,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetTariffsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client
                    .From<SupplierTariffRecord>()
                    .Filter("fournisseur_id", Constants.Operator.Equals, supplierId)
                    .Order("date_effet", Constants.Ordering.Descending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<SupplierTariff>)
                [
                    .. response.Models.Select(SupplierMapper.ToDomain).OfType<SupplierTariff>(),
                ];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddTariffAsync(
        long supplierId,
        SupplierTariffDraft draft,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(AddTariffAsync),
            TariffRefusedMessage,
            () => _client.From<SupplierTariffRecord>()
                .Insert(SupplierMapper.ToRecord(supplierId, draft), cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteTariffAsync(long tariffId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteTariffAsync),
            DeleteRefusedMessage,
            () => _client.From<SupplierTariffRecord>()
                .Where(record => record.Id == tariffId)
                .Delete(cancellationToken: cancellationToken),
            cancellationToken);
}
