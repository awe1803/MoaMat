using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;
using MoaMat.Domain.Regulators;
using MoaMat.Web.Presentation;

namespace MoaMat.Web.Pages;

/// <summary>
/// Fiche of one regulator: characteristics, the "new intervention" form (5
/// mandatory control points) and the chronological timeline.
/// </summary>
public partial class RegulatorSheet : ComponentBase
{
    private IReadOnlyList<RegulatorEvent> _events = [];
    private InventoryItem? _item;
    private RegulatorDetails? _details;

    private bool _isBusy = true;
    private bool _isSaving;
    private string? _error;
    private string? _notice;

    private bool _showIntervention;
    private DateOnly _interventionDate = Today;
    private string _ctrlGauge = string.Empty;
    private string _ctrlStages = string.Empty;
    private string _ctrlOctopus = string.Empty;
    private string _ctrlInflator = string.Empty;
    private string _ctrlHoseVisual = string.Empty;
    private string? _interventionProvider;
    private decimal? _interventionCost;
    private string? _interventionRemark;

    // Idempotency key: generated when the form opens and kept across retries
    // of a failed save, renewed only once the save succeeded.
    private Guid _interventionRequestId = Guid.NewGuid();

    private int _loadSequence;

    /// <summary>Technical identifier of the regulator item, from the route.</summary>
    [Parameter]
    public long ItemId { get; set; }

    [Inject]
    private IInventoryRepository Items { get; set; } = default!;

    [Inject]
    private IRegulatorRepository Regulators { get; set; } = default!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        _showIntervention = false;
        _notice = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var sequence = ++_loadSequence;
        _isBusy = true;
        _error = null;

        try
        {
            var item = await Items.GetItemAsync(ItemId);

            if (sequence != _loadSequence)
            {
                return;
            }

            // Only regulators have a fiche: an id of another family is treated
            // like an unknown one rather than rendered with empty regulator data.
            if (item is null || item.FamilyCode != ItemFamily.Regulator.Code)
            {
                _item = null;
                return;
            }

            var details = Regulators.GetDetailsAsync(ItemId);
            var events = Regulators.GetEventsAsync(ItemId);
            await Task.WhenAll(details, events);

            // A faster navigation to another regulator must not be overwritten
            // by the slower answer of the previous one.
            if (sequence != _loadSequence)
            {
                return;
            }

            _item = item;
            _details = await details;
            _events = await events;
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

    private void ToggleIntervention()
    {
        _showIntervention = !_showIntervention;
        _interventionRequestId = Guid.NewGuid();
        _interventionDate = Today;
        _ctrlGauge = string.Empty;
        _ctrlStages = string.Empty;
        _ctrlOctopus = string.Empty;
        _ctrlInflator = string.Empty;
        _ctrlHoseVisual = string.Empty;
        _interventionProvider = null;
        _interventionCost = null;
        _interventionRemark = null;
        _error = null;
    }

    private async Task SaveInterventionAsync()
    {
        _error = null;
        _notice = null;

        var created = RegulatorInterventionRequest.Create(
            ItemId,
            _interventionDate,
            ParseResult(_ctrlGauge),
            ParseResult(_ctrlStages),
            ParseResult(_ctrlOctopus),
            ParseResult(_ctrlInflator),
            ParseResult(_ctrlHoseVisual),
            _interventionProvider,
            _interventionCost,
            _interventionRemark,
            Today,
            _interventionRequestId);

        if (!created.Succeeded)
        {
            _error = created.Error;
            return;
        }

        _isSaving = true;
        try
        {
            var result = await Regulators.RecordInterventionAsync(created.Value!);
            if (!result.Succeeded)
            {
                _error = result.Error;
                return;
            }

            _notice = "Intervention enregistrée : l'échéance de révision a été recalculée.";
            _showIntervention = false;
            await LoadAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private static RegulatorControlResult? ParseResult(string value) =>
        Enum.TryParse<RegulatorControlResult>(value, out var result) ? result : null;

    /// <summary>Family, serial number, brand/model and location, on one line under the club code.</summary>
    private string DescribeSubtitle()
    {
        if (_item is null)
        {
            return string.Empty;
        }

        var parts = new[]
        {
            string.Join(' ', new[] { _item.Brand, _item.Model }.Where(part => !string.IsNullOrWhiteSpace(part))),
            _details?.ConnectionType,
            _item.LocationPath,
        };

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>Label/value cells of the characteristics grid.</summary>
    private IEnumerable<(string Label, string Value)> Characteristics()
    {
        var item = _item!;

        return
        [
            ("Localisation", Or(item.LocationPath)),
            ("Marque", Or(item.Brand)),
            ("Connexion", Or(_details?.ConnectionType)),
            ("1er étage", Or(_details?.FirstStageModel)),
            ("2e étage", Or(_details?.SecondStageModel)),
            ("Octopus", _details?.HasOctopus switch { true => Or(_details?.OctopusModel), false => "Absent", _ => "—" }),
            ("Manomètre", YesNo(_details?.HasGauge)),
            ("Inflateur", YesNo(_details?.HasInflator)),
            ("Statut", Or(item.StatusLabel ?? item.StatusCode)),
            ("Autorisé à la location", YesNo(_details?.IsAuthorizedForUse)),
            ("Réservé enfants", YesNo(_details?.IsReservedForChildren)),
            ("Prochaine échéance", FormatDate(item.DueOn)),
            ("Dernière intervention connue", FormatDate(_details?.LastInterventionOn)),
            ("Dernier contrôle (import Access)", FormatDate(_details?.LastKnownControlOn)),
        ];
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string YesNo(bool? value) => value switch { true => "Oui", false => "Non", _ => "—" };

    private static string FormatDate(DateOnly? date) =>
        date is { } value ? value.ToString("dd/MM/yyyy", AppCulture.French) : "date inconnue";

    private static string DescribeControlResult(RegulatorControlResult? result) => result switch
    {
        RegulatorControlResult.Ok => "OK",
        RegulatorControlResult.NotOk => "Non OK",
        RegulatorControlResult.NotApplicable => "Sans objet",
        RegulatorControlResult.NotControlled => "Non contrôlé (historique)",
        _ => "—",
    };

    private static string DescribeEvent(RegulatorEvent entry)
    {
        var parts = new[]
        {
            $"manomètre {DescribeControlResult(entry.ControlPoints.Gauge)}",
            $"détendeur {DescribeControlResult(entry.ControlPoints.Stages)}",
            $"octopus {DescribeControlResult(entry.ControlPoints.Octopus)}",
            $"inflateur {DescribeControlResult(entry.ControlPoints.Inflator)}",
            $"flexibles {DescribeControlResult(entry.ControlPoints.HoseVisual)}",
            entry.Provider,
            entry.CostEur is { } cost and > 0 ? $"{cost:0.00} €" : null,
            entry.Remark,
        };

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string EventTone(RegulatorEvent entry) =>
        entry.IsLegacyGap ? "neutre" : entry.ControlPoints.HasAnyFailure ? "expire" : "valide";
}
