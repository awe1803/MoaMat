using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="ItemRejectRecord"/> to the domain <see cref="ItemImportRejection"/>.</summary>
internal static class ItemImportRejectionMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of <c>public.item_reject</c>.</param>
    public static ItemImportRejection ToDomain(ItemRejectRecord record) => new(
        record.Id,
        record.OrigineTable,
        record.OrigineId,
        record.Colonne,
        record.ValeurBrute,
        record.Raison,
        record.CreeLe);
}
