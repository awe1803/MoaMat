using MoaMat.Domain.Locations;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="LocationContainerRecord"/> to the domain <see cref="LocationContainer"/>.</summary>
internal static class LocationContainerMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of <c>public.lieu_contenant</c>.</param>
    public static LocationContainer ToDomain(LocationContainerRecord record) =>
        new(record.Id, record.LocalId, record.Libelle);
}
