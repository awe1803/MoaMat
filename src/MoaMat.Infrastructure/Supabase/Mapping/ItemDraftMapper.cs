using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps the domain <see cref="ItemDraft"/> to the write record <see cref="ItemRecord"/>.</summary>
internal static class ItemDraftMapper
{
    /// <summary>Converts one draft.</summary>
    /// <param name="draft">Item to persist.</param>
    public static ItemRecord ToRecord(ItemDraft draft) => new()
    {
        Id = draft.Id,
        CodeClub = draft.ClubCode,
        Famille = draft.FamilyCode,
        NumSerie = draft.SerialNumber,
        Marque = draft.Brand,
        Modele = draft.Model,
        DateAcquisition = SqlDateConverter.ToDateTime(draft.AcquiredOn),
        PrixEur = draft.PriceEur,
        StatutCode = draft.StatusCode,
        LieuContenantId = draft.ContainerId,
        Destination = draft.Destination,
        Remarque = draft.Remark,
        DateEcheance = SqlDateConverter.ToDateTime(draft.DueOn),
        Actif = draft.IsActive,
    };
}
