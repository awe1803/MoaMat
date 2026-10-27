using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;

namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Port giving access to suppliers, the services they offer and the history of
/// their prices (<c>db/fournisseur.sql</c>). Rights are enforced by RLS on the
/// <c>fournisseur</c> permission domain: reading for every role, creating and
/// editing for managers, deleting for administrators.
/// </summary>
public interface ISupplierRepository
{
    /// <summary>All suppliers with their offered services, ordered by name.</summary>
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

    /// <summary>Archives or reactivates a supplier; its history is kept either way.</summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="isActive">The new state.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetSupplierActiveAsync(
        long supplierId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a supplier together with its services and price history.
    /// Reserved to administrators.
    /// </summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteSupplierAsync(long supplierId, CancellationToken cancellationToken = default);

    /// <summary>Declares that a supplier offers a service.</summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="service">Service offered.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddServiceAsync(
        long supplierId,
        CylinderServiceType service,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a service from a supplier. Refused while prices exist for it: the
    /// price history is never lost as a side effect.
    /// </summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="service">Service no longer offered.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> RemoveServiceAsync(
        long supplierId,
        CylinderServiceType service,
        CancellationToken cancellationToken = default);

    /// <summary>Price history of a supplier, most recent effective date first.</summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<SupplierTariff>> GetTariffsAsync(
        long supplierId,
        CancellationToken cancellationToken = default);

    /// <summary>Records a new price for a service the supplier offers.</summary>
    /// <param name="supplierId">Supplier identifier.</param>
    /// <param name="draft">Validated price and effective date.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AddTariffAsync(
        long supplierId,
        SupplierTariffDraft draft,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a price entered by mistake. Reserved to administrators.</summary>
    /// <param name="tariffId">Price identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> DeleteTariffAsync(long tariffId, CancellationToken cancellationToken = default);
}
