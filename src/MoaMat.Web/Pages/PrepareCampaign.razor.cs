using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Inventory;

namespace MoaMat.Web.Pages;

/// <summary>
/// "Préparer une campagne" screen: select bottles nearing their due date,
/// estimate cost, then send the bordereau once every line has its service
/// type set.
/// </summary>
/// <remarks>
/// Two phases share this one screen, distinguished by <see cref="CampaignId"/>:
/// with no campaign yet, the manager picks bottles from the due-soon list;
/// once <see cref="ICampaignRepository.CreateCampaignAsync"/> has returned an
/// id (carried in the query string so a reload does not lose the campaign),
/// the screen shows its lines for service-type entry and, once complete, the
/// bordereau fields.
/// </remarks>
public partial class PrepareCampaign : ComponentBase
{
    private const string DefaultProvider = "Apragaz";

    private readonly List<InventoryItem> _dueSoonItems = [];
    private readonly HashSet<long> _selectedItemIds = [];
    private readonly List<CylinderPricingRule> _pricingRules = [];
    private readonly List<Campaign> _resumableCampaigns = [];

    private Campaign? _campaign;
    private bool _isBusy = true;
    private bool _isSubmitting;
    private string? _error;
    private string _provider = DefaultProvider;
    private string _bordereauNumber = string.Empty;
    private DateOnly _sentOn = DateOnly.FromDateTime(DateTime.Today);

    [Parameter]
    [SupplyParameterFromQuery(Name = "id")]
    public long? CampaignId { get; set; }

    [Inject]
    private ICampaignRepository Campaigns { get; set; } = default!;

    [Inject]
    private IInventoryRepository Inventory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>True once a campaign has been created and is being completed for sending.</summary>
    private bool HasCampaign => _campaign is not null;

    /// <summary>Every line still missing its billed service — sending is blocked while this is non-empty.</summary>
    private IEnumerable<CampaignLine> LinesMissingService => _campaign?.LinesMissingService ?? [];

    /// <summary>Sum of every line's estimated cost, once all are known.</summary>
    private decimal? EstimatedTotal => _campaign?.EstimatedTotalEur;

    /// <inheritdoc />
    /// <remarks>
    /// Loading happens here, not in <c>OnInitializedAsync</c>: after
    /// <see cref="CreateCampaignAsync"/> navigates to this same route with a
    /// different <c>id</c> query value, Blazor reuses this component instance
    /// (same route, no full page load) and only re-runs the parameter-set
    /// lifecycle — <c>OnInitializedAsync</c> would never fire again, leaving
    /// the screen stuck on the bottle-selection view instead of showing the
    /// campaign that was just created.
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
                _pricingRules.Clear();
                _pricingRules.AddRange(await Campaigns.GetPricingRulesAsync());
            }
            else
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var items = await Inventory.GetItemsAsync(new InventoryFilter
                {
                    FamilyCode = ItemFamily.Cylinder.Code,
                    DueOnOrBefore = today.AddDays(InventoryItem.DueSoonHorizonInDays),
                });

                _dueSoonItems.Clear();
                _dueSoonItems.AddRange(items.OrderBy(item => item.DueOn));

                // A campaign created by CreateCampaignAsync but never reached
                // by the subsequent navigation (closed tab, lost network
                // right after the RPC committed) has no other way back: its
                // bottles are locked out of every new campaign by the
                // uniqueness trigger (db/campagne.sql), yet nothing before
                // this list ever showed a still-"preparation" campaign
                // anywhere. Without it, that campaign — and its bottles —
                // would be effectively unreachable through the UI forever.
                var campaigns = await Campaigns.GetCampaignsAsync();
                _resumableCampaigns.Clear();
                _resumableCampaigns.AddRange(campaigns.Where(campaign => campaign.Status == CampaignStatus.Preparation));
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

    private void ToggleSelection(long itemId, bool selected)
    {
        if (selected)
        {
            _selectedItemIds.Add(itemId);
        }
        else
        {
            _selectedItemIds.Remove(itemId);
        }
    }

    private async Task CreateCampaignAsync()
    {
        // Guards against a double-click firing two concurrent creations for
        // the same selection — the database's uniqueness trigger also closes
        // this race (see db/campagne.sql), but refusing the second click here
        // avoids a confusing "already engaged" error for what the manager
        // experiences as a single click.
        if (_isSubmitting)
        {
            return;
        }

        if (_selectedItemIds.Count == 0)
        {
            _error = "Sélectionnez au moins une bouteille.";
            return;
        }

        _isSubmitting = true;
        _error = null;

        try
        {
            var result = await Campaigns.CreateCampaignAsync(_provider, [.. _selectedItemIds]);

            if (!result.Succeeded)
            {
                _error = result.Error;
                return;
            }

            Navigation.NavigateTo(RouteFor(result.Value));
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    private async Task SetLineServiceTypeAsync(CampaignLine line, string? serviceCode)
    {
        if (_isSubmitting)
        {
            return;
        }

        var serviceType = CylinderServiceType.FromCode(serviceCode);
        if (serviceType is null)
        {
            return;
        }

        _isSubmitting = true;

        try
        {
            var result = await Campaigns.SetLineServiceTypeAsync(line.Id, serviceType);

            // ReloadAsync() clears _error as its very first step (it is also
            // the "fresh load" path, where an earlier message must not
            // linger) — so a write's refusal has to be applied AFTER the
            // reload, not before, or it is wiped before the next render ever
            // shows it.
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

    private async Task SendCampaignAsync()
    {
        // Without this guard, a double-click on "Envoyer" sends the first
        // click, then shows the second click's refusal (the campaign is no
        // longer "preparation") on top of an already-sent campaign — a
        // confusing error about an action that in fact just succeeded.
        if (_isSubmitting || _campaign is not { } campaign)
        {
            return;
        }

        _isSubmitting = true;

        try
        {
            var result = await Campaigns.SendCampaignAsync(campaign.Id, _bordereauNumber, _sentOn);
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

    /// <summary>Price resolved for one service at today's date — for the per-service picker labels.</summary>
    /// <param name="serviceType">Service to price.</param>
    private decimal? PriceFor(CylinderServiceType serviceType) =>
        CylinderPricingEngine.ResolvePrice(serviceType, _pricingRules, DateOnly.FromDateTime(DateTime.Today));

    /// <summary>Deep link opening this screen on an existing campaign.</summary>
    /// <param name="campaignId">Campaign to open.</param>
    private static string RouteFor(long campaignId) => $"campagnes/preparer?id={campaignId}";
}
