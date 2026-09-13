using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoaMat.Web.Notifications;

namespace MoaMat.Web.Components;

/// <summary>
/// Opt-in for the "new pending account" push notifications on the current
/// device.
/// </summary>
/// <remarks>
/// The host screen only renders this component for an account allowed to
/// approve sign-ups (<c>AccountAdministrationPolicy.CanApprovePendingAccounts</c>);
/// the database refuses a subscription from anybody else regardless.
/// </remarks>
public partial class PendingAccountNotificationToggle : ComponentBase
{
    private PushNotificationState? _state;
    private bool _isBusy;
    private string? _error;

    [Inject]
    private PushNotificationService Push { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _state = await Push.GetStateAsync();
        }
        catch (JSException)
        {
            // A browser that cannot even report its state is treated as unsupported.
            _state = PushNotificationState.Unsupported;
        }
    }

    private async Task EnableAsync()
    {
        // No await before this call: the permission prompt needs the click gesture.
        await RunAsync(Push.EnableAsync);
    }

    private Task DisableAsync() => RunAsync(Push.DisableAsync);

    private async Task RunAsync(Func<Task<Domain.Common.OperationResult>> action)
    {
        _isBusy = true;
        _error = null;

        try
        {
            var result = await action();
            if (!result.Succeeded)
            {
                _error = result.Error;
            }
        }
        catch (JSException)
        {
            _error = "Le navigateur a refusé l'abonnement aux notifications.";
        }

        try
        {
            _state = await Push.GetStateAsync();
        }
        catch (JSException)
        {
            _state = PushNotificationState.Unsupported;
        }
        finally
        {
            _isBusy = false;
        }
    }
}
