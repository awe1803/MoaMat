namespace MoaMat.Domain.Accounts;

/// <summary>
/// Names of the "at least this role" authorization policies registered in the
/// host. Kept as constants so markup and code cannot drift apart on a typo.
/// </summary>
public static class AuthorizationPolicyNames
{
    /// <summary>Any authenticated member (<see cref="AppRole.Reader"/> and above).</summary>
    public const string ReaderOrHigher = "role:lecture+";

    /// <summary><see cref="AppRole.Manager"/> and above.</summary>
    public const string ManagerOrHigher = "role:gestion+";

    /// <summary><see cref="AppRole.Administrator"/> and above.</summary>
    public const string AdministratorOrHigher = "role:admin+";

    /// <summary><see cref="AppRole.SuperAdministrator"/> only.</summary>
    public const string SuperAdministrator = "role:super-admin";
}
