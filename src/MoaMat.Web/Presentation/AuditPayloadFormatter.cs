using System.Globalization;

namespace MoaMat.Web.Presentation;

/// <summary>
/// Renders the free-form "before" / "after" payloads of an audit entry as a
/// single compact line.
/// </summary>
/// <remarks>
/// The payloads are JSON written by database triggers, so their shape is not
/// known at compile time. Formatting stays deliberately dumb: keys and values
/// are shown as they were recorded, because the point of an audit trail is to
/// show what happened, not an interpretation of it.
/// </remarks>
internal static class AuditPayloadFormatter
{
    /// <summary>Placeholder shown for an absent or empty payload.</summary>
    private const string Empty = "∅";

    /// <summary>Formats one payload as <c>key=value, key=value</c>.</summary>
    /// <param name="payload">Payload recorded by the trigger; may be null.</param>
    public static string Format(IReadOnlyDictionary<string, object>? payload) =>
        payload is null || payload.Count == 0
            ? Empty
            : string.Join(
                ", ",
                payload.Select(entry =>
                    string.Create(CultureInfo.InvariantCulture, $"{entry.Key}={entry.Value}")));
}
