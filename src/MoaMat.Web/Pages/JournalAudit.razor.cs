using MoaMat.Web.Data;

namespace MoaMat.Web.Pages;

public partial class JournalAudit
{
    private readonly List<AuditLogEntry> _entries = new();
    private string _filter = "";
    private bool _busy = true;
    private string? _error;

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var rows = await AuditLog.GetRecentAsync(
                actionFilter: string.IsNullOrEmpty(_filter) ? null : _filter);
            _entries.Clear();
            _entries.AddRange(rows);
        }
        catch (Exception ex)
        {
            _error = "Lecture du journal impossible : " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static string Summarize(Dictionary<string, object>? data)
        => data is null || data.Count == 0
            ? "∅"
            : string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}"));
}
