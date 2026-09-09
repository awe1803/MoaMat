using Microsoft.Extensions.Logging;
using MoaMat.Domain.Common;
using MoaMat.Domain.Locations;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;
using Supabase.Postgrest;
using Supabase.Postgrest.Models;

namespace MoaMat.Infrastructure.Supabase;

/// <summary>
/// Supabase adapter for <see cref="ILocationRepository"/>.
/// </summary>
/// <remarks>
/// Reading is open to every role; writing is reserved to administrators by RLS.
/// Deleting a node still referenced by a child or by an item raises a foreign
/// key violation, which is surfaced as a refusal rather than a crash.
/// </remarks>
internal sealed class SupabaseLocationRepository : ILocationRepository
{
    private const string ReadFailureMessage = "Lecture de la hiérarchie de lieux impossible.";
    private const string CreateRefusedMessage = "Création refusée (droits insuffisants ou doublon).";
    private const string DeleteRefusedMessage = "Suppression refusée (droits insuffisants ou lieu utilisé).";

    private readonly global::Supabase.Client _client;
    private readonly SupabaseCallGuard _guard;

    /// <summary>Creates the adapter.</summary>
    /// <param name="client">Configured Supabase client.</param>
    /// <param name="logger">Logger receiving untranslated provider errors.</param>
    public SupabaseLocationRepository(global::Supabase.Client client, ILogger<SupabaseLocationRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _guard = new SupabaseCallGuard(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LocationSection>> GetSectionsAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetSectionsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client
                    .From<LocationSectionRecord>()
                    .Order("libelle", Constants.Ordering.Ascending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<LocationSection>)[.. response.Models.Select(LocationSectionMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<LocationRoom>> GetRoomsAsync(
        long? sectionId = null,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetRoomsAsync),
            ReadFailureMessage,
            async () =>
            {
                var query = _client.From<LocationRoomRecord>().Order("libelle", Constants.Ordering.Ascending);

                if (sectionId is { } id)
                {
                    query = query.Filter("section_id", Constants.Operator.Equals, id);
                }

                var response = await query.Get(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<LocationRoom>)[.. response.Models.Select(LocationRoomMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<LocationContainer>> GetContainersAsync(
        long? roomId = null,
        CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetContainersAsync),
            ReadFailureMessage,
            async () =>
            {
                var query = _client.From<LocationContainerRecord>().Order("libelle", Constants.Ordering.Ascending);

                if (roomId is { } id)
                {
                    query = query.Filter("local_id", Constants.Operator.Equals, id);
                }

                var response = await query.Get(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<LocationContainer>)[.. response.Models.Select(LocationContainerMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<LocationPath>> GetContainerPathsAsync(CancellationToken cancellationToken = default) =>
        _guard.ReadAsync(
            nameof(GetContainerPathsAsync),
            ReadFailureMessage,
            async () =>
            {
                var response = await _client
                    .From<LocationPathRecord>()
                    .Order("chemin", Constants.Ordering.Ascending)
                    .Get(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<LocationPath>)[.. response.Models.Select(LocationPathMapper.ToDomain)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddSectionAsync(
        LocationLabel label,
        CancellationToken cancellationToken = default) =>
        InsertAsync(new LocationSectionRecord { Libelle = label.Value }, nameof(AddSectionAsync), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddRoomAsync(
        long sectionId,
        LocationLabel label,
        CancellationToken cancellationToken = default) =>
        InsertAsync(
            new LocationRoomRecord { SectionId = sectionId, Libelle = label.Value },
            nameof(AddRoomAsync),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> AddContainerAsync(
        long roomId,
        LocationLabel label,
        CancellationToken cancellationToken = default) =>
        InsertAsync(
            new LocationContainerRecord { LocalId = roomId, Libelle = label.Value },
            nameof(AddContainerAsync),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteSectionAsync(long sectionId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteSectionAsync),
            DeleteRefusedMessage,
            () => _client.From<LocationSectionRecord>()
                .Where(record => record.Id == sectionId)
                .Delete(cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteRoomAsync(long roomId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteRoomAsync),
            DeleteRefusedMessage,
            () => _client.From<LocationRoomRecord>()
                .Where(record => record.Id == roomId)
                .Delete(cancellationToken: cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteContainerAsync(long containerId, CancellationToken cancellationToken = default) =>
        _guard.WriteAsync(
            nameof(DeleteContainerAsync),
            DeleteRefusedMessage,
            () => _client.From<LocationContainerRecord>()
                .Where(record => record.Id == containerId)
                .Delete(cancellationToken: cancellationToken),
            cancellationToken);

    private Task<OperationResult> InsertAsync<TRecord>(
        TRecord record,
        string operationName,
        CancellationToken cancellationToken)
        where TRecord : BaseModel, new() =>
        _guard.WriteAsync(
            operationName,
            CreateRefusedMessage,
            () => _client.From<TRecord>().Insert(record, cancellationToken: cancellationToken),
            cancellationToken);
}
