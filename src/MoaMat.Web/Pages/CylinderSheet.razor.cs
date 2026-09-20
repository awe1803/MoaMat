using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Inventory;
using MoaMat.Web.Presentation;

namespace MoaMat.Web.Pages;

/// <summary>
/// Fiche of one cylinder: characteristics, actions (record a requalification,
/// download the life sheet, report an incident) and the chronological timeline.
/// </summary>
/// <remarks>
/// The timeline merges two sources that both belong to the cylinder's life: the
/// events of <c>public.bouteille_evenement</c> (imported legacy history and
/// entries recorded here) and the status transitions of
/// <c>public.item_transition</c>.
/// </remarks>
public partial class CylinderSheet : ComponentBase, IAsyncDisposable
{
    private const string DownloadModulePath = "./js/file-download.js";

    private IReadOnlyList<CylinderEvent> _events = [];
    private IReadOnlyList<TimelineEntry> _timeline = [];
    private InventoryItem? _item;
    private CylinderDetails? _details;

    private bool _isBusy = true;
    private bool _isSaving;
    private string? _error;
    private string? _notice;

    private bool _showRequalification;
    private string _reqControl = nameof(CylinderControlType.Optical);
    private DateOnly _reqDate = Today;
    private string _reqOutcome = string.Empty;
    private string? _reqProvider;
    private decimal? _reqCost;
    private string? _reqCertificate;
    private string? _reqRemark;

    private bool _showIncident;
    private DateOnly _incidentDate = Today;
    private string? _incidentDescription;

    // Idempotency keys: generated when a form opens and kept across retries of a
    // failed save, renewed only once the save succeeded.
    private Guid _reqRequestId = Guid.NewGuid();
    private Guid _incidentRequestId = Guid.NewGuid();

    private int _loadSequence;

    private IJSObjectReference? _downloadModule;

    /// <summary>Technical identifier of the cylinder item, from the route.</summary>
    [Parameter]
    public long ItemId { get; set; }

    [Inject]
    private IInventoryRepository Items { get; set; } = default!;

    [Inject]
    private ICylinderRepository Cylinders { get; set; } = default!;

    [Inject]
    private ILifeSheetRenderer LifeSheets { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        _showRequalification = false;
        _showIncident = false;
        _notice = null;
        await LoadAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_downloadModule is not null)
        {
            try
            {
                await _downloadModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; nothing left to release.
            }
        }

        GC.SuppressFinalize(this);
    }

    private async Task LoadAsync()
    {
        var sequence = ++_loadSequence;
        _isBusy = true;
        _error = null;

        try
        {
            var item = await Items.GetItemAsync(ItemId);

            // Only cylinders have a fiche: an id of another family is treated
            // like an unknown one rather than rendered with empty cylinder data.
            if (sequence != _loadSequence)
            {
                return;
            }

            if (item is null || item.FamilyCode != ItemFamily.Cylinder.Code)
            {
                _item = null;
                return;
            }

            var details = Cylinders.GetDetailsAsync(ItemId);
            var events = Cylinders.GetEventsAsync(ItemId);
            var transitions = Items.GetStatusHistoryAsync(ItemId);
            await Task.WhenAll(details, events, transitions);

            // A faster navigation to another cylinder must not be overwritten
            // by the slower answer of the previous one.
            if (sequence != _loadSequence)
            {
                return;
            }

            _item = item;
            _details = await details;
            _events = await events;
            _timeline = BuildTimeline(_events, await transitions);
        }
        catch (DataAccessException exception)
        {
            if (sequence == _loadSequence)
            {
                _error = exception.Message;
                _item = null;
            }
        }
        finally
        {
            if (sequence == _loadSequence)
            {
                _isBusy = false;
            }
        }
    }

    private void ToggleRequalification()
    {
        _showRequalification = !_showRequalification;
        _reqRequestId = Guid.NewGuid();
        _showIncident = false;
        _error = null;
    }

    private void ToggleIncident()
    {
        _showIncident = !_showIncident;
        _incidentRequestId = Guid.NewGuid();
        _showRequalification = false;
        _error = null;
    }

    private async Task SaveRequalificationAsync()
    {
        _error = null;
        _notice = null;

        var created = RequalificationRequest.Create(
            ItemId,
            Enum.Parse<CylinderControlType>(_reqControl),
            _reqDate,
            _reqOutcome switch
            {
                "passed" => CampaignLineOutcome.Passed,
                "failed" => CampaignLineOutcome.Failed,
                _ => null,
            },
            _reqProvider,
            _reqCost,
            _reqCertificate,
            _reqRemark,
            Today,
            _reqRequestId);

        if (!created.Succeeded)
        {
            _error = created.Error;
            return;
        }

        var request = created.Value!;
        _isSaving = true;
        try
        {
            var result = await Cylinders.RecordRequalificationAsync(request);
            if (!result.Succeeded)
            {
                _error = result.Error;
                return;
            }

            _notice = request.Outcome == CampaignLineOutcome.Passed
                ? "Requalification enregistrée : l'échéance de la bouteille a été recalculée."
                : "Échec enregistré dans la chronologie. Le statut de la bouteille n'a pas changé.";
            ResetRequalificationForm();
            await LoadAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task SaveIncidentAsync()
    {
        _error = null;
        _notice = null;

        if (string.IsNullOrWhiteSpace(_incidentDescription))
        {
            _error = "Décrivez l'incident.";
            return;
        }

        if (_incidentDate > Today)
        {
            _error = "La date de l'incident ne peut pas être dans le futur.";
            return;
        }

        _isSaving = true;
        try
        {
            var result = await Cylinders.ReportIncidentAsync(ItemId, _incidentDate, _incidentDescription, _incidentRequestId);
            if (!result.Succeeded)
            {
                _error = result.Error;
                return;
            }

            _notice = "Incident consigné dans la chronologie.";
            _incidentDescription = null;
            _incidentRequestId = Guid.NewGuid();
            _incidentDate = Today;
            _showIncident = false;
            await LoadAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DownloadLifeSheetAsync()
    {
        if (_item is null)
        {
            return;
        }

        _error = null;
        _isSaving = true;
        try
        {
            var pdf = LifeSheets.Render(new CylinderLifeSheet(_item, _details, _events, Today));
            var fileName = $"fiche-de-vie-{SafeFileName(_item.ClubCode ?? $"bouteille-{_item.Id}")}.pdf";

            _downloadModule ??= await JS.InvokeAsync<IJSObjectReference>("import", DownloadModulePath);
            await _downloadModule.InvokeVoidAsync("downloadFile", fileName, "application/pdf", pdf);
        }
        catch (JSException)
        {
            _error = "Le téléchargement de la fiche de vie a échoué.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void ResetRequalificationForm()
    {
        _showRequalification = false;
        _reqControl = nameof(CylinderControlType.Optical);
        _reqDate = Today;
        _reqOutcome = string.Empty;
        _reqProvider = null;
        _reqCost = null;
        _reqCertificate = null;
        _reqRemark = null;
        _reqRequestId = Guid.NewGuid();
    }

    /// <summary>Family, serial number, brand/model and location, on one line under the club code.</summary>
    private string DescribeSubtitle()
    {
        if (_item is null)
        {
            return string.Empty;
        }

        var parts = new[]
        {
            _item.SerialNumber is null ? null : $"N° de série {_item.SerialNumber}",
            string.Join(' ', new[] { _item.Brand, _item.Model }.Where(part => !string.IsNullOrWhiteSpace(part))),
            _item.LocationPath,
        };

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>Label/value cells of the characteristics grid; the first six are those of the mock-up.</summary>
    private IEnumerable<(string Label, string Value)> Characteristics()
    {
        var item = _item!;

        return
        [
            ("Marque", Or(item.Brand)),
            ("Volume", _details?.VolumeLitres is { } volume ? $"{volume:0.##} L" : "—"),
            ("Pression de service", _details?.ServicePressureBar is { } bar ? $"{bar} bar" : "—"),
            ("Tare", _details?.TareKg is { } tare ? $"{tare:0.##} kg" : "—"),
            ("Type de gaz", Or(_details?.UsageLabel)),
            ("Destination", Or(item.Destination)),
            ("Matière", Or(_details?.MaterialLabel)),
            ("N° peint", Or(_details?.PaintedNumber)),
            ("Statut", Or(item.StatusLabel ?? item.StatusCode)),
            ("Prochaine échéance", FormatDate(item.DueOn)),
            ("Dernier contrôle optique", FormatDate(_details?.LastOpticalControlOn)),
            ("Dernier contrôle hydraulique", FormatDate(_details?.LastHydraulicControlOn)),
        ];
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatDate(DateOnly? date) =>
        date is { } value ? value.ToString("dd/MM/yyyy", AppCulture.French) : "date inconnue";

    /// <summary>Keeps letters, digits, dash and underscore: a club code is a file name only in spirit.</summary>
    private static string SafeFileName(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());

    /// <summary>
    /// Merges timeline events and status transitions, most recent first;
    /// undated legacy rows go last, in database order.
    /// </summary>
    private static List<TimelineEntry> BuildTimeline(
        IReadOnlyList<CylinderEvent> events,
        IReadOnlyList<ItemStatusTransition> transitions)
    {
        var entries = new List<TimelineEntry>(events.Count + transitions.Count);

        entries.AddRange(events.Select(entry => new TimelineEntry(
            entry.OccurredOn,
            entry.Title,
            DescribeEvent(entry),
            entry.IsAdverse ? "expire" : entry.Outcome is CampaignLineOutcome.Passed ? "valide" : "neutre")));

        entries.AddRange(transitions.Select(transition => new TimelineEntry(
            transition.EffectiveOn,
            $"Statut : {transition.PreviousStatusLabel ?? transition.PreviousStatusCode ?? "—"} → {transition.NewStatusLabel ?? transition.NewStatusCode}",
            DescribeTransition(transition),
            "neutre")));

        // Stable ordering: LINQ OrderBy keeps the source order for equal keys,
        // and events come before transitions in the source.
        return [.. entries
            .OrderBy(entry => entry.Date is null)
            .ThenByDescending(entry => entry.Date)];
    }

    private static string? DescribeEvent(CylinderEvent entry)
    {
        var parts = new[]
        {
            entry.Provider,
            entry.CertificateNumber is null ? null : $"certificat {entry.CertificateNumber}",
            entry.CostEur is { } cost ? $"{cost:0.00} €" : null,
            entry.NextDueOn is { } next ? $"échéance suivante {FormatDate(next)}" : null,
            entry.Remark,
        };

        var detail = string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return detail.Length == 0 ? null : detail;
    }

    private static string? DescribeTransition(ItemStatusTransition transition) =>
        string.IsNullOrWhiteSpace(transition.Reason) ? null : transition.Reason;

    private sealed record TimelineEntry(DateOnly? Date, string Title, string? Detail, string Tone);
}
