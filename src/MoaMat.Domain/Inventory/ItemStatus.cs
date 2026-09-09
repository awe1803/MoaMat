namespace MoaMat.Domain.Inventory;

/// <summary>
/// Entry of the status catalogue (<c>public.ref_statut</c>).
/// </summary>
/// <param name="Code">Status code stored on the item.</param>
/// <param name="Label">User-facing label.</param>
/// <param name="IsTerminal">
/// True when leaving this status requires the <c>status.terminal.override</c>
/// permission. The rule is enforced in SQL and audited; the client only reflects it.
/// </param>
/// <param name="SortOrder">Display order in the catalogue.</param>
public sealed record ItemStatus(string Code, string Label, bool IsTerminal, int SortOrder);
