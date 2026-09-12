using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="ItemStatusTransitionRecord"/> to the domain <see cref="ItemStatusTransition"/>.</summary>
internal static class ItemStatusTransitionMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of the view <c>public.v_item_transition</c>.</param>
    public static ItemStatusTransition ToDomain(ItemStatusTransitionRecord record) => new()
    {
        Id = record.Id,
        ItemId = record.ItemId,
        PreviousStatusCode = record.AncienStatut,
        PreviousStatusLabel = record.AncienStatutLibelle,
        NewStatusCode = record.NouveauStatut,
        NewStatusLabel = record.NouveauStatutLibelle,
        Reason = record.Motif,
        EffectiveOn = SqlDateConverter.ToDateOnly(record.DateEffet) ?? DateOnly.MinValue,
        Authority = record.AutoriteDecision,
        AttachmentUrl = record.PieceJointeUrl,
        DecidedById = record.DecidePar,
        DecidedByEmail = record.DecideParEmail,
        RecordedAt = record.CreeLe,
    };
}
