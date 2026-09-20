using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Common;

namespace MoaMat.Web.Pages;

/// <summary>
/// "Retour de campagne" screen: grouped pointing of the bottles that actually
/// came back, their certificate and actual cost, and the resulting list of
/// missing bottles.
/// </summary>
/// <remarks>
/// With no campaign selected, the screen lists every campaign currently
/// "envoyée" (awaiting a return) or already "retournée" (for the missing-
/// bottles report). Selecting one shows its lines for pointing.
/// </remarks>
public partial class ReturnCampaign : ComponentBase
{
    private readonly List<Campaign> _campaigns = [];
    private readonly Dictionary<long, bool> _received = [];
    private readonly Dictionary<long, string> _certificateNumbers = [];
    private readonly Dictionary<long, decimal?> _actualCosts = [];
    private readonly Dictionary<long, CampaignLineOutcome?> _outcomes = [];

    private Campaign? _campaign;
    private bool _isBusy = true;
    private bool _isSubmitting;
    private string? _error;
    private DateOnly _returnedOn = DateOnly.FromDateTime(DateTime.Today);

    [Parameter]
    [SupplyParameterFromQuery(Name = "id")]
    public long? CampaignId { get; set; }

    [Inject]
    private ICampaignRepository Campaigns { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <inheritdoc />
    /// <remarks>
    /// Loading happens here, not in <c>OnInitializedAsync</c>: the "Ouvrir"
    /// link on the campaign list navigates to this same route with a
    /// different <c>id</c> query value, so Blazor reuses this component
    /// instance and only re-runs the parameter-set lifecycle —
    /// <c>OnInitializedAsync</c> would never fire again and the screen would
    /// stay stuck on the list instead of showing the selected campaign's lines.
    /// </remarks>
    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _isBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            if (CampaignId is { } id)
            {
                _campaign = await Campaigns.GetCampaignAsync(id);
                _received.Clear();
                _certificateNumbers.Clear();
                _actualCosts.Clear();
                _outcomes.Clear();

                if (_campaign is not null)
                {
                    foreach (var line in _campaign.Lines)
                    {
                        _received[line.Id] = line.ReturnedOn is not null;
                        _certificateNumbers[line.Id] = line.CertificateNumber ?? string.Empty;

                        // Pre-filled ONLY from an actual cost already recorded
                        // (re-opening an already-pointed line for correction).
                        // Never from EstimatedCostEur: an estimate is not what
                        // the provider actually billed, and silently carrying
                        // it into the "real cost" field would let an unedited
                        // line submit as if its real cost had been confirmed.
                        _actualCosts[line.Id] = line.ActualCostEur;

                        // No default here either: line.Outcome is null until
                        // pointed, and stays null in this dictionary until the
                        // manager explicitly picks pass/fail — a condemned
                        // bottle must never be pointed "by omission" as having
                        // passed.
                        _outcomes[line.Id] = line.Outcome;
                    }
                }
            }
            else
            {
                var campaigns = await Campaigns.GetCampaignsAsync();
                _campaigns.Clear();
                _campaigns.AddRange(campaigns.Where(campaign => campaign.Status != CampaignStatus.Preparation));
            }
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

    private async Task RecordReturnAsync()
    {
        // Without this guard, a double-click sends the pointing twice; the
        // second call would either be an idempotent no-op (harmless) or, if
        // anything changed between the two clicks (e.g. a field edited just
        // as the first click fired), get refused as "already pointed with
        // different values" — a confusing error over a return that in fact
        // just succeeded.
        if (_isSubmitting)
        {
            return;
        }

        if (_campaign is not { } campaign)
        {
            return;
        }

        // Lines already pointed in an earlier session (line.ReturnedOn is not
        // null) are EXCLUDED here regardless of _received's state, even
        // though they show pre-checked for visibility: resubmitting them with
        // today's _returnedOn would very likely differ from the date they
        // were originally pointed on, and the database refuses a re-pointing
        // whose values differ from what is already recorded (db/campagne.sql)
        // — every later catch-up session for the stragglers would fail
        // outright the moment it does not happen on the exact same calendar
        // day as the first one. Only genuinely new lines are ever submitted.
        var receivedLines = campaign.Lines
            .Where(line => line.ReturnedOn is null && _received.GetValueOrDefault(line.Id))
            .ToArray();

        if (receivedLines.Length == 0)
        {
            _error = "Aucune bouteille pointée comme reçue.";
            return;
        }

        // A missing or negative cost is refused here rather than defaulted to
        // zero and sent anyway: the database enforces the same rule
        // (db/campagne.sql), but catching it before the round-trip tells the
        // manager exactly which bottle is still missing its cost.
        var missingCost = receivedLines.FirstOrDefault(line => _actualCosts.GetValueOrDefault(line.Id) is not (>= 0m));
        if (missingCost is not null)
        {
            _error = $"Coût réel manquant ou négatif pour {missingCost.ItemClubCode ?? $"la ligne {missingCost.Id}"}.";
            return;
        }

        // No default outcome: a bottle condemned at requalification must
        // never be pointed as if it had passed just because the manager
        // forgot to pick a result — see CampaignLineOutcome.
        var missingOutcome = receivedLines.FirstOrDefault(line => _outcomes.GetValueOrDefault(line.Id) is null);
        if (missingOutcome is not null)
        {
            _error = $"Résultat de requalification manquant pour {missingOutcome.ItemClubCode ?? $"la ligne {missingOutcome.Id}"}.";
            return;
        }

        var lines = receivedLines
            .Select(line => new CampaignReturnLine(
                line.Id,
                _returnedOn,
                _actualCosts[line.Id],
                _certificateNumbers.GetValueOrDefault(line.Id, string.Empty),
                _outcomes[line.Id]!.Value))
            .ToArray();

        _isSubmitting = true;

        try
        {
            var result = await Campaigns.RecordReturnAsync(campaign.Id, _returnedOn, lines);

            // ReloadAsync() clears _error as its first step, so the write's
            // refusal (if any) must be applied AFTER the reload, not before,
            // or it is wiped before the next render ever shows it.
            await ReloadAsync();

            if (!result.Succeeded)
            {
                _error = result.Error;
            }
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    /// <summary>Parses the outcome `&lt;select&gt;`'s raw value, or <c>null</c> for the unset placeholder option.</summary>
    /// <param name="value">Raw <c>change</c> event value.</param>
    private static CampaignLineOutcome? ParseOutcome(string? value) =>
        Enum.TryParse<CampaignLineOutcome>(value, out var outcome) ? outcome : null;

    /// <summary>Deep link opening this screen on one campaign's return.</summary>
    /// <param name="campaignId">Campaign to open.</param>
    private static string RouteFor(long campaignId) => $"campagnes/retour?id={campaignId}";
}
