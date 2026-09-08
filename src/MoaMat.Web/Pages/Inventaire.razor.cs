using MoaMat.Web.Data;

namespace MoaMat.Web.Pages;

public partial class Inventaire
{
    private static readonly (string Code, string Label)[] Familles =
    {
        ("bouteille", "Bouteilles"),
        ("detendeur", "Détendeurs"),
        ("gilet", "Gilets"),
        ("petit_materiel", "Petit matériel"),
        ("materiel_didactique", "Matériel didactique"),
        ("piece_detachee", "Pièces détachées"),
    };

    private readonly List<ItemRow> _rows = new();
    private IReadOnlyList<RefStatut> _statuts = Array.Empty<RefStatut>();
    private IReadOnlyList<LieuContenantRow> _contenants = Array.Empty<LieuContenantRow>();

    private readonly ItemFilter _filter = new();

    // Champs liés (le binding <select>/<input> ne gère pas bien les nullables).
    private string _lieuContenantId = "";
    private DateTime? _echeanceAvant;
    private string _actif = "actifs";

    private bool _busy = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _statuts = await Items.GetStatutsAsync();
            _contenants = await Lieux.GetContenantRowsAsync();
        }
        catch (Exception ex)
        {
            _error = "Chargement des référentiels impossible : " + ex.Message;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        StateHasChanged();

        _filter.LieuContenantId = long.TryParse(_lieuContenantId, out var lc) ? lc : null;
        _filter.EcheanceAvant = _echeanceAvant;
        _filter.Actif = _actif switch
        {
            "actifs" => true,
            "inactifs" => false,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(_filter.Famille)) _filter.Famille = null;
        if (string.IsNullOrWhiteSpace(_filter.StatutCode)) _filter.StatutCode = null;

        try
        {
            var rows = await Items.GetItemsAsync(_filter);
            _rows.Clear();
            _rows.AddRange(rows);
        }
        catch (Exception ex)
        {
            _error = "Lecture de l'inventaire impossible : " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ToggleActifAsync(ItemRow it)
    {
        _error = null;
        var result = await Items.SetActifAsync(it.Id, actif: !it.Actif);
        if (!result.Succeeded)
        {
            _error = result.Error;
        }

        await ReloadAsync();
    }

    private static string FamilleLabel(string code) =>
        Array.Find(Familles, f => f.Code == code).Label is { Length: > 0 } l ? l : code;

    private static string Join(string? a, string? b)
    {
        var parts = new[] { a, b }.Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join(" ", parts);
        return joined.Length == 0 ? "—" : joined;
    }

    private static string AmbiguReason(ItemRow it)
    {
        var reasons = new List<string>();
        if (it.CodeClubDuplique == true) reasons.Add("code dupliqué");
        if (it.CodeClubNonStructurant == true) reasons.Add("code non structurant");
        return reasons.Count > 0 ? string.Join(", ", reasons) : "code ambigu";
    }
}
