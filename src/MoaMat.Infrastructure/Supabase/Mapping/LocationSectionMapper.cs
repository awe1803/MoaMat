using MoaMat.Domain.Locations;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="LocationSectionRecord"/> to the domain <see cref="LocationSection"/>.</summary>
internal static class LocationSectionMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of <c>public.lieu_section</c>.</param>
    public static LocationSection ToDomain(LocationSectionRecord record) =>
        new(record.Id, record.Libelle);
}
