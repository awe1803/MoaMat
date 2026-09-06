namespace MoaMat.Web;

public partial class RedirectToLogin
{
    protected override void OnInitialized()
    {
        var returnUrl = Navigation.ToBaseRelativePath(Navigation.Uri);
        var target = string.IsNullOrEmpty(returnUrl) || returnUrl.StartsWith("connexion", StringComparison.OrdinalIgnoreCase)
            ? "connexion"
            : $"connexion?returnUrl={Uri.EscapeDataString(returnUrl)}";

        Navigation.NavigateTo(target, forceLoad: false, replace: true);
    }
}
