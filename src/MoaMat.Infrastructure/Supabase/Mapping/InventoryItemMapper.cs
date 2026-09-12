using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.Infrastructure.Supabase.Mapping;

/// <summary>Maps <see cref="ItemViewRecord"/> to the domain <see cref="InventoryItem"/>.</summary>
internal static class InventoryItemMapper
{
    /// <summary>Converts one record.</summary>
    /// <param name="record">PostgREST row of the view <c>public.v_item</c>.</param>
    public static InventoryItem ToDomain(ItemViewRecord record) => new()
    {
        Id = record.Id,
        ClubCode = record.CodeClub,
        HasAmbiguousClubCode = record.CodeClubAmbigu,
        HasDuplicateClubCode = record.CodeClubDuplique ?? false,
        HasNonStructuringClubCode = record.CodeClubNonStructurant ?? false,
        FamilyCode = record.Famille,
        SerialNumber = record.NumSerie,
        Brand = record.Marque,
        Model = record.Modele,
        AcquiredOn = SqlDateConverter.ToDateOnly(record.DateAcquisition),
        PriceEur = record.PrixEur,
        StatusCode = record.StatutCode,
        StatusLabel = record.StatutLibelle,
        IsStatusTerminal = record.StatutTerminal,
        IsAvailable = record.Disponible,
        ContainerId = record.LieuContenantId,
        LocationPath = record.LieuChemin,
        Destination = record.Destination,
        Remark = record.Remarque,
        DueOn = SqlDateConverter.ToDateOnly(record.DateEcheance),
        IsActive = record.Actif,
        CreatedAt = record.CreeLe,
        UpdatedAt = record.MajLe,
    };
}
