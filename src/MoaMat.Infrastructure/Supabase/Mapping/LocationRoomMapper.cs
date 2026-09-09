using MoaMat.Domain.Locations;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="LocationRoomRecord"/> to the domain <see cref="LocationRoom"/>.</summary>
internal static class LocationRoomMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of <c>public.lieu_local</c>.</param>
    public static LocationRoom ToDomain(LocationRoomRecord record) =>
        new(record.Id, record.SectionId, record.Libelle);
}
