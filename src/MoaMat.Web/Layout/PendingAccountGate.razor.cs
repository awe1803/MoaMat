using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MoaMat.Domain.Accounts;
using MoaMat.Web.DependencyInjection;

namespace MoaMat.Web.Layout;

/// <summary>
/// Replaces every <see cref="AuthorizeAttribute"/> page with the pending-account
/// screen while the signed-in user holds no real role. Public pages (sign-in,
/// sign-up, sign-out, password recovery) stay reachable.
/// </summary>
/// <remarks>
/// The child content is kept at the same position in the render tree whatever
/// the authentication state, so a sign-in/sign-out happening while a page is
/// open (GoTrue signs the caller in during sign-up) never remounts that page.
/// </remarks>
public partial class PendingAccountGate : ComponentBase
{
    private bool _isBlocked;

    /// <summary>Route being rendered.</summary>
    [Parameter]
    [EditorRequired]
    public RouteData RouteData { get; set; } = default!;

    /// <summary>Content rendered when the user is not blocked.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationState is null
            || RouteData.PageType.GetCustomAttribute<AuthorizeAttribute>() is null)
        {
            _isBlocked = false;
            return;
        }

        var user = (await AuthenticationState).User;

        // Rank 0 covers both "en_attente" and a token without any role claim:
        // neither grants anything, so neither may see the application shell.
        _isBlocked = user.Identity?.IsAuthenticated == true
                     && !user.RoleOf().IsAtLeast(AppRole.Reader);
    }
}
