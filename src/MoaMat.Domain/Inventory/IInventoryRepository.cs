using MoaMat.Domain.Common;

namespace MoaMat.Domain.Inventory;

/// <summary>
/// Port giving access to the inventory (Item model, see <c>db/MODELE.md</c>).
/// </summary>
/// <remarks>
/// <para>Reads go through the <c>security_invoker</c> view <c>public.v_item</c>,
/// so an unauthorised caller gets an empty list rather than an error. Writes go
/// through <c>public.item</c> and are arbitrated by the RLS policies of
/// <c>db/rls.sql</c>.</para>
/// <para>Every write is expected to be <b>idempotent</b>: replaying the same
/// status change or the same deactivation converges on the same state. Items
/// are never physically deleted, only deactivated, so a replayed delete cannot
/// destroy history.</para>
/// </remarks>
public interface IInventoryRepository
{
    /// <summary>Items matching <paramref name="filter"/>, ordered by club code.</summary>
    /// <param name="filter">Query criteria; page size is bounded by the filter.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<InventoryItem>> GetItemsAsync(
        InventoryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>One item, or <c>null</c> when it does not exist or is not visible.</summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<InventoryItem?> GetItemAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>Status catalogue, in display order.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<ItemStatus>> GetStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>Legacy values the migration could not convert.</summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<ItemImportRejection>> GetImportRejectionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Creates an item and returns the identifier the database generated.</summary>
    /// <param name="draft">Item to create.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult<long>> CreateItemAsync(ItemDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing item, identified by <see cref="ItemDraft.Id"/>.</summary>
    /// <param name="draft">Item to update.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> UpdateItemAsync(ItemDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates an item logically, or reactivates it. The inventory never
    /// deletes: an item that left the club stays visible in history.
    /// </summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="isActive">True to reactivate, false to deactivate.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetItemActivationAsync(
        long itemId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an item status. The database rejects the change outright when
    /// <paramref name="transition"/> is missing a reason or an effective date,
    /// when the target status is <c>perdu</c>/<c>vole</c> with no attachment,
    /// or when it enters a terminal status with no deciding authority. Leaving
    /// a terminal status is further arbitrated by the database (permission
    /// <c>status.terminal.override</c>) and is audited either way.
    /// </summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="transition">Target status plus the mandatory reason for the change.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetItemStatusAsync(
        long itemId,
        StatusTransitionRequest transition,
        CancellationToken cancellationToken = default);

    /// <summary>Decision history of an item's status changes, most recent first.</summary>
    /// <param name="itemId">Item identifier.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<ItemStatusTransition>> GetStatusHistoryAsync(
        long itemId,
        CancellationToken cancellationToken = default);
}
