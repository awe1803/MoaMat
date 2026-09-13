using MoaMat.Domain.Common;

namespace MoaMat.Domain.Notifications;

/// <summary>
/// Port storing the Web Push subscriptions of the accounts allowed to approve
/// sign-ups. The database refuses a subscription from any other account.
/// </summary>
/// <remarks>
/// Both operations are idempotent: saving the same browser twice converges on
/// one subscription, removing an unknown endpoint succeeds.
/// </remarks>
public interface IPushSubscriptionRepository
{
    /// <summary>Stores the subscription of the current browser for the signed-in user.</summary>
    /// <param name="subscription">Subscription returned by the browser.</param>
    /// <param name="userAgent">Browser description, kept to tell devices apart; may be null.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SaveAsync(
        PushSubscription subscription,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="endpoint"/> is stored for the signed-in user.
    /// False once the database has purged it (the account lost the permission
    /// <c>role.assign</c>) or when another account registered this browser.
    /// </summary>
    /// <param name="endpoint">Endpoint of the browser subscription.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    /// <exception cref="Common.DataAccessException">The subscriptions could not be read.</exception>
    Task<bool> IsStoredAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Removes the subscription of <paramref name="endpoint"/> if it belongs to the signed-in user.</summary>
    /// <param name="endpoint">Endpoint of the browser subscription.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> RemoveAsync(string endpoint, CancellationToken cancellationToken = default);
}
