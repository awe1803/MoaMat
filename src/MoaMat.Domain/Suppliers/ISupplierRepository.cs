using MoaMat.Domain.Common;

namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Port giving access to suppliers (contact details, archiving), the catalogue
/// of services each one offers and the history of their prices
/// (<c>db/fournisseur.sql</c>, <c>db/fournisseur_prestation.sql</c>). Rights are
/// enforced by the database on the <c>fournisseur</c> permission domain: reading
/// for every role, creating and editing for managers, deleting for
/// administrators.
/// </summary>
/// <remarks>
/// A service is either a bottle control (optical, or hydraulic with an explicit
/// oil/water method — usable in a campaign) or a free service; its tariff is
/// optional. Saving a service or adding a tariff is not blindly retryable after
/// a transient failure: re-read the catalogue first (a retried creation is
/// refused as a duplicate label, a retried tariff as an already-priced date).
/// </remarks>
public interface ISupplierRepository
{
    /// <summary>All suppliers (archived included) with their contact details, ordered by name.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a supplier.</summary>
    /// <param name="draft">Validated contact details.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddSupplierAsync(SupplierDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Updates the contact details of a supplier.</summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="draft">Validated contact details.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> UpdateSupplierAsync(
        long supplierId,
        SupplierDraft draft,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives or reactivates a supplier; its history is kept either way. An
    /// archived supplier cannot be picked for a new campaign.
    /// </summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="isActive">The new state.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetSupplierActiveAsync(
        long supplierId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a supplier together with its services and price history.
    /// Reserved to administrators; refused by the database when a campaign
    /// references the supplier (archive it instead).
    /// </summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteSupplierAsync(long supplierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Services of one supplier with their tariffs (history included): active
    /// ones first, then by name.
    /// </summary>
    /// <param name="supplierId">Supplier whose catalogue is read.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<SupplierService>> GetServicesAsync(long supplierId, CancellationToken cancellationToken = default);

    /// <summary>Creates or modifies (and so also activates/deactivates) a service; returns its identifier.</summary>
    /// <param name="request">Validated service.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult<long>> SaveServiceAsync(
        SupplierServiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a service. Reserved to administrators; refused by the
    /// database while prices exist for it (the price history is never lost as a
    /// side effect) or once a campaign used it (deactivate it instead).
    /// </summary>
    /// <param name="serviceId">Service to delete.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteServiceAsync(long serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new price for a service. Prices are never modified in place: a
    /// change is a new row with a later effective date.
    /// </summary>
    /// <param name="draft">Validated price and effective date.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddTariffAsync(SupplierTariffDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a price entered by mistake. Reserved to administrators; estimated
    /// costs already stored on campaign lines are not recalculated.
    /// </summary>
    /// <param name="tariffId">Price identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteTariffAsync(long tariffId, CancellationToken cancellationToken = default);
}
