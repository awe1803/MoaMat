using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="ItemStatusRecord"/> to the domain <see cref="ItemStatus"/>.</summary>
internal static class ItemStatusMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of <c>public.ref_statut</c>.</param>
    public static ItemStatus ToDomain(ItemStatusRecord record) =>
        new(record.Code, record.Libelle, record.EstTerminal, record.Ordre);
}
