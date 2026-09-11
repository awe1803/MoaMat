using System.Globalization;

namespace MoaMat.Domain.Accounts;

/// <summary>
/// Application role, as a value object ordered from the least to the most
/// privileged. <see cref="Code"/> is the exact value stored in
/// <c>public.utilisateur_role</c> and mirrored into the JWT claim
/// <c>app_metadata.role</c>; it is deliberately kept in French so that it stays
/// byte-identical to the SQL side (<c>db/roles.sql</c>).
/// </summary>
/// <remarks>
/// Ranking here drives navigation and screen affordances only. The enforcing
/// authority is the set of RLS policies in <c>db/rls.sql</c>: a client that
/// lies about its role still gets nothing back.
/// </remarks>
public sealed record AppRole
{
    private AppRole(string code, int rank)
    {
        Code = code;
        Rank = rank;
    }

    /// <summary>No role at all - an unauthenticated or unknown principal.</summary>
    public static AppRole None { get; } = new(string.Empty, 0);

    /// <summary>Read-only access (<c>lecture</c>).</summary>
    public static AppRole Reader { get; } = new("lecture", 1);

    /// <summary>Day-to-day gear management (<c>gestion</c>).</summary>
    public static AppRole Manager { get; } = new("gestion", 2);

    /// <summary>Administration of accounts and reference data (<c>admin</c>).</summary>
    public static AppRole Administrator { get; } = new("admin", 3);

    /// <summary>Highest privilege, single seat (<c>super-admin</c>).</summary>
    public static AppRole SuperAdministrator { get; } = new("super-admin", 4);

    /// <summary>Every assignable role, ordered from least to most privileged.</summary>
    public static IReadOnlyList<AppRole> Assignable { get; } =
        [Reader, Manager, Administrator, SuperAdministrator];

    /// <summary>Value stored in the database and in the JWT claim.</summary>
    public string Code { get; }

    /// <summary>Privilege rank, aligned with the SQL function <c>public.moamat_role_rank()</c>.</summary>
    public int Rank { get; }

    /// <summary>
    /// Parses a role code. Unknown, empty or malformed input yields
    /// <see cref="None"/> - never an exception, because the value comes from an
    /// external token the client does not control.
    /// </summary>
    /// <param name="code">Raw role code read from the JWT or the database.</param>
    public static AppRole FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return None;
        }

        return code.Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "super-admin" or "superadmin" => SuperAdministrator,
            "admin" => Administrator,
            "gestion" => Manager,
            "lecture" => Reader,
            _ => None,
        };
    }

    /// <summary>True when this role is at least as privileged as <paramref name="other"/>.</summary>
    /// <param name="other">Role to compare against.</param>
    public bool IsAtLeast(AppRole other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Rank >= other.Rank;
    }

    /// <inheritdoc />
    public override string ToString() => Code;
}
