using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Audit;
using MoaMat.Domain.Common;

namespace MoaMat.Web.Pages;

/// <summary>
/// Audit trail screen. Read-only by construction: the trail is append-only and
/// written by the database, so there is nothing to edit here.
/// </summary>
public partial class AuditTrail : ComponentBase
{
    private readonly List<AuditEntry> _entries = [];

    private string _actionFilter = string.Empty;
    private bool _isBusy = true;
    private string? _error;

    [Inject]
    private IAuditLogRepository AuditLog { get; set; } = default!;

    /// <inheritdoc />
    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _isBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var entries = await AuditLog.GetRecentAsync(
                actionFilter: string.IsNullOrEmpty(_actionFilter) ? null : _actionFilter);

            _entries.Clear();
            _entries.AddRange(entries);
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _isBusy = false;
        }
    }
}
