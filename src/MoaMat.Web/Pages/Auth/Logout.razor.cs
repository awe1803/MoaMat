namespace MoaMat.Web.Pages.Auth;

public partial class Logout
{
    protected override async Task OnInitializedAsync()
    {
        await Auth.SignOutAsync();
        Navigation.NavigateTo("connexion", replace: true);
    }
}
