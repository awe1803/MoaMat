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

    [Inject]
    private IAccountRepository Accounts { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = default!;

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
