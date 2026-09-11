namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// How a failed Supabase call should be reported to the user.
/// </summary>
/// <remarks>
/// The distinction that matters operationally is <em>refused</em> versus
/// <em>unreachable</em>: telling someone they lack rights when the network is
/// down sends them hunting for a permission problem that does not exist.
/// </remarks>
internal enum SupabaseFailureCategory
{
    /// <summary>The call failed for a reason that does not fit the other cases.</summary>
    Unexpected = 0,

    /// <summary>The database refused the operation (RLS, missing permission).</summary>
    Refused = 1,

    /// <summary>The write collided with an existing row or a referenced row.</summary>
    Conflict = 2,

    /// <summary>The request never got an answer, or the server failed.</summary>
    Unreachable = 3,
}
