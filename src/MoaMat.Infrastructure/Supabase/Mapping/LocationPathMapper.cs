using MoaMat.Domain.Locations;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="LocationPathRecord"/> to the domain <see cref="LocationPath"/>.</summary>
internal static class LocationPathMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of the view <c>public.v_lieu_contenant</c>.</param>
    public static LocationPath ToDomain(LocationPathRecord record) => new(
        record.ContenantId,
        record.Contenant,
        record.LocalId,
        record.Local,
        record.SectionId,
        record.Section,
        record.Chemin);
}
