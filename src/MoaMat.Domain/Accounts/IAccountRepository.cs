using MoaMat.Domain.Common;

namespace MoaMat.Domain.Accounts;

/// <summary>
/// Port giving access to user accounts and their application role.
/// </summary>
/// <remarks>
/// Every implementation is expected to be <b>idempotent</b>: replaying the same
/// role assignment or the same activation change must converge on the same
/// state rather than fail or duplicate anything. Screens retry on user action,
/// and a flaky network can make a write land twice.
/// </remarks>
public interface IAccountRepository
{
    /// <summary>
    /// Accounts visible to the caller, ordered by e-mail. An unauthorised caller
    /// gets an empty list rather than an error: the filtering happens in the
    /// database (permission <c>compte.read</c>).
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<IReadOnlyList<UserAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Assigns <paramref name="role"/> to <paramref name="userId"/>.</summary>
    /// <param name="userId">Target account.</param>
    /// <param name="role">Role to assign; <see cref="AppRole.None"/> is rejected.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> AssignRoleAsync(Guid userId, AppRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or deactivates an account. Accounts are never deleted, only
    /// deactivated.
    /// </summary>
    /// <param name="userId">Target account.</param>
    /// <param name="isActive">True to reactivate, false to deactivate.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetActivationAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);
}
