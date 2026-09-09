using MoaMat.Domain.Accounts;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="UserAccountRecord"/> to the domain <see cref="UserAccount"/>.</summary>
internal static class UserAccountMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row.</param>
    public static UserAccount ToDomain(UserAccountRecord record) => new(
        record.UserId,
        record.Email,
        AppRole.FromCode(record.Role),
        record.Desactive,
        record.CreatedAt,
        record.LastSignInAt);
}
