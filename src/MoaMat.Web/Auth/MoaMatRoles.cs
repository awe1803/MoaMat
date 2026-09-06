namespace MoaMat.Web.Auth;

/// <summary>
/// Rôles applicatifs MoaMat, du moins au plus privilégié. La valeur est celle
/// portée par le claim JWT <c>app_metadata.role</c> et interprétée par les
/// policies RLS (<c>db/schema.sql</c>, <c>db/storage.sql</c>).
/// </summary>
public static class MoaMatRoles
{
    public const string Lecture = "lecture";
    public const string Gestion = "gestion";
    public const string Admin = "admin";
    public const string SuperAdmin = "super-admin";

    /// <summary>Politiques d'autorisation « au moins ce rôle ».</summary>
    public static class Policies
    {
        public const string LectureOrHigher = "role:lecture+";
        public const string GestionOrHigher = "role:gestion+";
        public const string AdminOrHigher = "role:admin+";
        public const string SuperAdmin = "role:super-admin";
    }

    /// <summary>Rang numérique (aligné sur <c>public.moamat_role_rank()</c> côté SQL).</summary>
    public static int Rank(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "super-admin" or "superadmin" => 4,
        "admin" => 3,
        "gestion" => 2,
        "lecture" => 1,
        _ => 0,
    };
}
