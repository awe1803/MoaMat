using Microsoft.Extensions.Logging;
using MoaMat.Domain.Accounts;
using MoaMat.Domain.Common;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="IAccountRepository"/>.
/// </summary>
/// <remarks>
/// <para>The real arbitration happens in the database: the view filters on the
/// permission <c>compte.read</c>, the RLS policies on
/// <c>public.utilisateur_role</c> reject a forbidden role change, and
/// <c>public.set_compte_actif</c> raises <c>42501</c> for a forbidden
/// activation change. This adapter only turns a refusal into a message.</para>
/// <para>Both writes are naturally idempotent - assigning the role a user
/// already holds, or deactivating an already deactivated account, converges on
/// the same state and is safely replayable.</para>
/// </remarks>
internal sealed class SupabaseAccountRepository : IAccountRepository
{
    private const string ReadFailureMessage = "Lecture des comptes impossible.";
    private const string RoleRefusedMessage =
        "Changement de rôle refusé (droits insuffisants ou compte protégé).";
    private const string ActivationRefusedMessage =
        "Changement d'état du compte refusé (droits insuffisants ou compte protégé).";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseAccountRepository(global::Supabase.Client client, ILogger<SupabaseAccountRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserAccount>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetAccountsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client
                    .From<UserAccountRecord>()
                    .Order("email", Constants.Ordering.Ascending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<UserAccount>)[.. response.Models.Select(UserAccountMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AssignRoleAsync(
        Guid userId,
        AppRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (role == AppRole.None)
        {
            return Task.FromResult(OperationResult.Failure("Rôle inconnu : changement refusé."));
        }

        return _guard.WriteAsync(
            nameof(AssignRoleAsync),
            RoleRefusedMessage,
            () => _client
                .From<UserRoleRecord>()
                .Where(record => record.UserId == userId)
                .Set(record => record.Role, role.Code)
                .Update(cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> SetActivationAsync(
        Guid userId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(SetActivationAsync),
            ActivationRefusedMessage,
            () => _client.Rpc("set_compte_actif", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_user"] = userId,
                ["p_actif"] = isActive,
            }),
            cancellationToken);
}
