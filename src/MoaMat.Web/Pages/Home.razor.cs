using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Domain.Accounts;
using MoaMat.Domain.Common;
using MoaMat.Domain.Inventory;
using MoaMat.Web.Navigation;
using MoaMat.Web.Presentation;

namespace MoaMat.Web.Pages;

/// <summary>
/// Dashboard: what is out of validity today, the module tiles, and the closest
/// due dates — the entry screen of the MOANA mock-up.
/// </summary>
public partial class Home : ComponentBase
{
    private readonly List<AppModule> _modules = [];

    private DashboardSummary _summary = DashboardSummary.Empty;
    private IReadOnlyCollection<string> _families = [];
    private bool _isBusy = true;
    private string? _error;
    private int _pendingAccounts;
    private bool _canAdministerAccounts;

    [Inject]
    private IInventoryRepository Items { get; set; } = default!;

    [Inject]
    private IAccountRepository Accounts { get; set; } = default!;

    [Inject]
    private IAuthorizationService Authorization { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    /// <summary>Reference day for every due date shown on this screen.</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Inventory filtered on the items whose due date has passed.</summary>
    private static string OverdueHref => Inventory.DueFilterHref(Inventory.DueFilterOverdue);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadModulesAsync();
        await LoadInventoryAsync();
        await LoadPendingAccountsAsync();
    }

    /// <summary>Formats a count, or a dash while the inventory is still loading.</summary>
    /// <param name="value">Count to render.</param>
    private string Figure(int value) =>
        _isBusy ? "—" : value.ToString(AppCulture.French);

    /// <summary>Caption of a tile: the live count for a family, its pitch otherwise.</summary>
    /// <param name="module">Module the tile stands for.</param>
    private string DescribeModule(AppModule module)
    {
        if (module.FamilyCode is null || _isBusy)
        {
            return module.Subtitle;
        }

        var count = _summary.CountIn(module.FamilyCode);
        return count == 1 ? "1 item actif" : $"{count.ToString(AppCulture.French)} items actifs";
    }

    /// <summary>Brand, model and location of an item, on one line.</summary>
    /// <param name="item">Item being previewed.</param>
    private static string DescribeItem(InventoryItem item)
    {
        var parts = new[]
        {
            string.Join(' ', new[] { item.Brand, item.Model }.Where(part => !string.IsNullOrWhiteSpace(part))),
            item.LocationPath,
        };

        var description = string.Join(" — ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return description.Length == 0 ? ItemFamily.DisplayNameFor(item.FamilyCode) : description;
    }

    /// <summary>Short due date, or "échue" once the date has passed.</summary>
    /// <param name="dueOn">Due date of the item.</param>
    private static string FormatDue(DateOnly? dueOn) => dueOn switch
    {
        null => "—",
        { } date when date < Today => "échue",
        { } date => date.ToString("dd/MM/yy", AppCulture.French),
    };

    /// <summary>Deep link opening the inventory on the family of <paramref name="item"/>.</summary>
    /// <param name="item">Item the row stands for.</param>
    private static string InventoryHrefFor(InventoryItem item) =>
        AppModules.InventoryHref(item.FamilyCode);

    private async Task LoadModulesAsync()
    {
        if (AuthenticationState is null)
        {
            return;
        }

        var user = (await AuthenticationState).User;

        foreach (var module in AppModules.All)
        {
            if (await IsVisibleAsync(user, module))
            {
                _modules.Add(module);
            }
        }
    }

    private async Task<bool> IsVisibleAsync(ClaimsPrincipal user, AppModule module)
    {
        if (module.Policy is null)
        {
            return true;
        }

        var result = await Authorization.AuthorizeAsync(user, resource: null, module.Policy);
        return result.Succeeded;
    }

    /// <summary>
    /// Counts accounts awaiting activation, for administrators only - a plain
    /// member never sees who is waiting for validation.
    /// </summary>
    private async Task LoadPendingAccountsAsync()
    {
        if (AuthenticationState is null)
        {
            return;
        }

        var user = (await AuthenticationState).User;
        var result = await Authorization.AuthorizeAsync(user, resource: null, AuthorizationPolicyNames.AdministratorOrHigher);
        _canAdministerAccounts = result.Succeeded;

        if (!_canAdministerAccounts)
        {
            return;
        }

        try
        {
            var accounts = await Accounts.GetAccountsAsync();
            _pendingAccounts = accounts.Count(account => account.IsPending);
        }
        catch (DataAccessException)
        {
            // Silently ignored: the banner is a convenience, not a critical
            // feature, and the account screen itself already surfaces errors.
        }
    }

    private async Task LoadInventoryAsync()
    {
        try
        {
            var items = await Items.GetItemsAsync(new InventoryFilter
            {
                Activation = ActivationScope.ActiveOnly,
                MaxResults = InventoryFilter.MaxAllowedResults,
            });

            _summary = DashboardSummary.From(items, Today);
            _families = ItemFamily.All
                .Where(family => _summary.CountIn(family.Code) > 0)
                .Select(family => family.Code)
                .ToArray();
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
