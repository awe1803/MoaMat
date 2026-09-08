using System.Security.Claims;
using MoaMat.Web.Auth;
using MoaMat.Web.Data;

namespace MoaMat.Web.Pages;

public partial class GestionComptes
{
    private static readonly string[] Roles = { "lecture", "gestion", "admin", "super-admin" };

    private readonly List<CompteRow> _rows = new();
    private bool _busy = true;
    private string? _error;
    private bool _isSuperAdmin;
    private Guid _currentUserId;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthState.GetAuthenticationStateAsync()).User;
        _isSuperAdmin = MoaMatRoles.Rank(user.FindFirst(ClaimTypes.Role)?.Value) >= 4;
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out _currentUserId);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var rows = await Comptes.GetComptesAsync();
            _rows.Clear();
            _rows.AddRange(rows);
        }
        catch (Exception ex)
        {
            _error = "Lecture des comptes impossible : " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static bool IsElevated(CompteRow c) => MoaMatRoles.Rank(c.Role) >= 3;

    private static bool IsCa(CompteRow c) =>
        string.Equals(c.Role, MoaMatRoles.Lecture, StringComparison.OrdinalIgnoreCase);

    // Un admin (non super-admin) ne peut pas viser un compte élevé ni le sien.
    // La promotion lecture -> gestion d'un compte « CA » reste permise par la RLS,
    // donc le <select> n'est pas verrouillé pour un compte CA.
    private bool RoleSelectDisabled(CompteRow c) =>
        c.UserId == _currentUserId || (!_isSuperAdmin && IsElevated(c));

    private bool OptionDisabled(string role) =>
        !_isSuperAdmin && MoaMatRoles.Rank(role) >= 3;

    // Désactivation : jamais soi-même ; pour un admin, jamais un compte CA
    // (lecture) ni un compte élevé (réservé au super-admin, cf. set_compte_actif).
    private bool ActifToggleDisabled(CompteRow c) =>
        c.UserId == _currentUserId || (!_isSuperAdmin && (IsElevated(c) || IsCa(c)));

    private async Task ChangeRoleAsync(CompteRow c, string? role)
    {
        if (string.IsNullOrWhiteSpace(role) || string.Equals(role, c.Role, StringComparison.Ordinal))
        {
            return;
        }

        _error = null;
        var result = await Comptes.SetRoleAsync(c.UserId, role);
        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        await ReloadAsync();
    }

    private async Task ToggleActifAsync(CompteRow c)
    {
        _error = null;
        var result = await Comptes.SetActifAsync(c.UserId, actif: c.Desactive);
        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        await ReloadAsync();
    }
}
