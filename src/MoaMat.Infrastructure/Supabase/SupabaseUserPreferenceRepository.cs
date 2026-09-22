using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Preferences;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="IUserPreferenceRepository"/>.
/// </summary>
/// <remarks>
/// Writes go through the RPC of <c>db/preferences.sql</c>: the table has no
/// write policy, and the row is attached to <c>auth.uid()</c> by the database
/// rather than by anything the client sends.
/// </remarks>
internal sealed class SupabaseUserPreferenceRepository : IUserPreferenceRepository
{
    private const string ReadFailureMessage = "Lecture des préférences d'affichage impossible.";
    private const string WriteRefusedMessage = "Enregistrement de la préférence d'affichage impossible.";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseUserPreferenceRepository(
        global::Supabase.Client client,
        ILogger<SupabaseUserPreferenceRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<bool> IsInstallTipHiddenAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(IsInstallTipHiddenAsync),
            ReadFailureMessage,
            async () =>
            {
                // RLS only returns the signed-in user's row; an account that
                // never answered has none, which reads as "not hidden".
                var response = await _client.From<UserPreferenceRecord>()
                    .Select("invite_installation_masquee")
                    .Limit(1)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return response.Models is [{ InviteInstallationMasquee: true }, ..];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> SetInstallTipHiddenAsync(
        bool isHidden,
        CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(SetInstallTipHiddenAsync),
            WriteRefusedMessage,
            () => _client.Rpc("definir_invite_installation_masquee", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["p_masquee"] = isHidden,
            }),
            cancellationToken);
}
