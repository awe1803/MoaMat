namespace MoaMat.Domain.Accounts;

/// <summary>
/// A Supabase Auth account seen through the account administration screen:
/// identity, application role and activation state. Immutable - every change
/// goes through <see cref="IAccountRepository"/> and is arbitrated by the
/// database.
/// </summary>
/// <param name="UserId">Supabase Auth identifier.</param>
/// <param name="Email">Login address; may be absent for a provider-only account.</param>
/// <param name="Role">Current application role.</param>
/// <param name="IsDisabled">True when the account has been deactivated.</param>
/// <param name="CreatedAt">Account creation instant.</param>
/// <param name="LastSignInAt">Last successful sign-in, when known.</param>
public sealed record UserAccount(
    Guid UserId,
    string? Email,
    AppRole Role,
    bool IsDisabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt)
{
    /// <summary>
    /// True for a privileged account (<see cref="AppRole.Administrator"/> and
    /// above), which only a super-administrator may act upon.
    /// </summary>
    public bool IsElevated => Role.IsAtLeast(AppRole.Administrator);

    /// <summary>
    /// True for a board member account ("CA", <em>conseil d'administration</em>),
    /// materialised by the <see cref="AppRole.Reader"/> role. Deactivating one is
    /// reserved to a super-administrator (see <c>public.set_compte_actif</c>).
    /// </summary>
    public bool IsBoardMember => Role == AppRole.Reader;

    /// <summary>
    /// True for an account awaiting activation (<see cref="AppRole.Pending"/>):
    /// it just signed up and holds no permission until an administrator assigns
    /// it a real role. Distinct from <see cref="IsDisabled"/> - a pending account
    /// is not "deactivated", it was simply never activated yet.
    /// </summary>
    public bool IsPending => Role == AppRole.Pending;
}
