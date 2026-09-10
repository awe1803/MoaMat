using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Web.Navigation;

namespace MoaMat.Web.Layout;

/// <summary>
/// Side navigation: the module catalogue grouped under its headings, filtered
/// by what the signed-in role may open.
/// </summary>
/// <remarks>
/// Visibility is resolved once, in code, rather than through one
/// <c>AuthorizeView</c> per entry: the group headings must disappear with their
/// last visible module, which a purely declarative markup cannot decide.
/// </remarks>
public partial class NavMenu : ComponentBase
{
    private readonly List<AppModule> _visible = [];

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Inject]
    private IAuthorizationService Authorization { get; set; } = default!;

    /// <summary>Headings that still carry at least one visible module.</summary>
    private IEnumerable<string> VisibleGroups =>
        AppModules.Groups.Where(group => VisibleModulesIn(group).Any());

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        _visible.Clear();

        if (AuthenticationState is null)
        {
            return;
        }

        var user = (await AuthenticationState).User;

        foreach (var module in AppModules.All)
        {
            if (await IsVisibleAsync(user, module))
            {
                _visible.Add(module);
            }
        }
    }

    private IEnumerable<AppModule> VisibleModulesIn(string group) =>
        _visible.Where(module => string.Equals(module.Group, group, StringComparison.Ordinal));

    private async Task<bool> IsVisibleAsync(ClaimsPrincipal user, AppModule module)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (module.Policy is null)
        {
            return true;
        }

        var result = await Authorization.AuthorizeAsync(user, resource: null, module.Policy);
        return result.Succeeded;
    }
}
