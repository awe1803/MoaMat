namespace MoaMat.Domain.Inventory;

/// <summary>
/// One legacy Access value that the migration script could not convert
/// (<c>public.item_reject</c>). The raw value is kept verbatim so a human can
/// decide what it meant, rather than the migration guessing.
/// </summary>
/// <param name="Id">Technical identifier of the rejection row.</param>
/// <param name="SourceTable">Access mirror table the value came from.</param>
/// <param name="SourceId">Row identifier in that table, when known.</param>
/// <param name="ColumnName">Column the value came from.</param>
/// <param name="RawValue">The value, exactly as it was in the source.</param>
/// <param name="Reason">Why the conversion was refused.</param>
/// <param name="RecordedAt">When the rejection was recorded.</param>
public sealed record ItemImportRejection(
    long Id,
    string SourceTable,
    long? SourceId,
    string ColumnName,
    string RawValue,
    string Reason,
    DateTimeOffset RecordedAt);
