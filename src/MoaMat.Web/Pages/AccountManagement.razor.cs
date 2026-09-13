using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Domain.Accounts;
using MoaMat.Domain.Common;
using MoaMat.Web.DependencyInjection;

namespace MoaMat.Web.Pages;

/// <summary>
/// Account administration screen: role assignment and activation.
/// </summary>
/// <remarks>
/// What the signed-in administrator may do is decided by
/// <see cref="AccountAdministrationPolicy"/>, not by this component. The screen
/// only renders the answer; the database refuses anything the policy would have
/// allowed by mistake. Deletion is irreversible, so the row itself carries a
/// two-click confirmation (<see cref="_pendingDeleteId"/>) instead of firing on
/// the first click.
/// </remarks>
public partial class AccountManagement : ComponentBase
{
    private const string RoutePath = "comptes";

    private const string RoleQueryParameter = "role";

    private readonly List<UserAccount> _accounts = [];

    private AccountAdministrationPolicy _policy = new(Guid.Empty, AppRole.None);
    private bool _isBusy = true;
    private string? _error;

    /// <summary>
    /// Account awaiting a second click before its deletion is actually sent -
    /// permanent deletion has no confirmation dialog in this codebase's style,
    /// so the row itself becomes the confirmation step.
    /// </summary>
    private Guid? _pendingDeleteId;

    /// <summary>
    /// Role code the list is restricted to, from the <c>role</c> query parameter.
    /// Kept in the URL so the dashboard banner can open the screen on the
    /// pending accounts only.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = RoleQueryParameter)]
    public string? Role { get; set; }

    [Inject]
    private IAccountRepository Accounts { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Roles offered as filter chips, pending first since it is the one needing action.</summary>
    private static IReadOnlyList<AppRole> FilterRoles { get; } = [AppRole.Pending, .. AppRole.Assignable];

    /// <summary>Role selected by the query string, or <see cref="AppRole.None"/> for every account.</summary>
    private AppRole SelectedRole => AppRole.FromCode(Role);

    /// <summary>Accounts matching the selected role chip.</summary>
    private IEnumerable<UserAccount> VisibleAccounts =>
        SelectedRole == AppRole.None ? _accounts : _accounts.Where(account => account.Role == SelectedRole);

    /// <summary>Link opening the account screen on one role.</summary>
    /// <param name="role">Role to filter on, or <c>null</c> for every account.</param>
    public static string RoleFilterHref(AppRole? role) =>
        role is null || role == AppRole.None
            ? RoutePath
            : $"{RoutePath}?{RoleQueryParameter}={Uri.EscapeDataString(role.Code)}";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthenticationState.GetAuthenticationStateAsync()).User;

        // An unparseable subject claim means "no known actor". Guid.Empty never
        // matches a real account, so the self-protection rule simply stops
        // firing; the database still refuses a self-directed change.
        var actorId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parsed)
            ? parsed
            : Guid.Empty;

        _policy = new AccountAdministrationPolicy(actorId, user.RoleOf());

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _isBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var accounts = await Accounts.GetAccountsAsync();
            _accounts.Clear();
            _accounts.AddRange(accounts);
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

    private async Task ChangeRoleAsync(UserAccount account, string? roleCode)
    {
        var role = AppRole.FromCode(roleCode);

        if (role == AppRole.None || role == account.Role)
        {
            return;
        }

        _error = null;
        var result = await Accounts.AssignRoleAsync(account.UserId, role);

        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        // Reload either way: on success to show the new state, on failure to
        // undo the optimistic value the select box is already displaying.
        await ReloadAsync();
    }

    private async Task ToggleActivationAsync(UserAccount account)
    {
        _error = null;
        var result = await Accounts.SetActivationAsync(account.UserId, isActive: account.IsDisabled);

        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        await ReloadAsync();
    }

    /// <summary>Number of loaded accounts holding one role.</summary>
    /// <param name="role">Role to count, or <see cref="AppRole.None"/> for every account.</param>
    private int CountFor(AppRole role) =>
        role == AppRole.None ? _accounts.Count : _accounts.Count(account => account.Role == role);

    /// <summary>Rewrites the current URL with the role filter changed.</summary>
    /// <param name="role">Role to filter on, or <see cref="AppRole.None"/> to drop the filter.</param>
    private void FilterBy(AppRole role) =>
        Navigation.NavigateTo(
            Navigation.GetUriWithQueryParameter(RoleQueryParameter, role == AppRole.None ? null : role.Code));

    private void RequestDelete(UserAccount account) => _pendingDeleteId = account.UserId;

    private void CancelDelete() => _pendingDeleteId = null;

    private async Task DeleteAccountAsync(UserAccount account)
    {
        _error = null;
        var result = await Accounts.DeleteAccountAsync(account.UserId);

        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        // Reset either way: on success the row is gone after reload; on
        // failure the confirmation state for a row that turned out to be
        // protected (e.g. promoted to super-admin by someone else in the
        // meantime) should not linger.
        _pendingDeleteId = null;
        await ReloadAsync();
    }
}
