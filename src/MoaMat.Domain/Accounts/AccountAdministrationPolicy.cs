namespace MoaMat.Domain.Accounts;

/// <summary>
/// Decides what the signed-in administrator is allowed to do on other accounts.
/// This mirrors the rules enforced by the RLS policies on
/// <c>public.utilisateur_role</c> and by <c>public.set_compte_actif</c>.
/// </summary>
/// <remarks>
/// The rules live here, in one testable place, instead of being spread across
/// component markup. They drive <em>affordances</em> only: disabling a control
/// the database would reject anyway. This is <b>not</b> the security boundary -
/// bypassing it changes nothing, the database still refuses.
/// </remarks>
public sealed class AccountAdministrationPolicy
{
    private readonly Guid _actorId;
    private readonly AppRole _actorRole;

    /// <summary>Builds the policy for the currently signed-in user.</summary>
    /// <param name="actorId">Identifier of the signed-in user.</param>
    /// <param name="actorRole">Role of the signed-in user.</param>
    public AccountAdministrationPolicy(Guid actorId, AppRole actorRole)
    {
        ArgumentNullException.ThrowIfNull(actorRole);
        _actorId = actorId;
        _actorRole = actorRole;
    }

    /// <summary>True when the actor may open the account administration screen at all.</summary>
    public bool CanAdministerAccounts => _actorRole.IsAtLeast(AppRole.Administrator);

    private bool IsSuperAdministrator => _actorRole.IsAtLeast(AppRole.SuperAdministrator);

    /// <summary>
    /// True when the actor may change the role of <paramref name="account"/>.
    /// Nobody may change their own role, and only a super-administrator may
    /// touch a privileged account.
    /// </summary>
    /// <param name="account">Account being administered.</param>
    public bool CanChangeRoleOf(UserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return CanAdministerAccounts
            && account.UserId != _actorId
            && (IsSuperAdministrator || !account.IsElevated);
    }

    /// <summary>
    /// True when the actor may hand out <paramref name="role"/>. Granting a
    /// privileged role is reserved to a super-administrator.
    /// </summary>
    /// <param name="role">Role the actor wants to assign.</param>
    public bool CanAssignRole(AppRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return CanAdministerAccounts
            && (IsSuperAdministrator || !role.IsAtLeast(AppRole.Administrator));
    }

    /// <summary>
    /// True when the actor may activate or deactivate <paramref name="account"/>.
    /// Nobody may lock themselves out; an administrator may touch neither a
    /// privileged account nor a board member account.
    /// </summary>
    /// <param name="account">Account being administered.</param>
    public bool CanChangeActivationOf(UserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return CanAdministerAccounts
            && account.UserId != _actorId
            && (IsSuperAdministrator || (!account.IsElevated && !account.IsBoardMember));
    }
}
