using MoaMat.Web.Data;

namespace MoaMat.Web.Pages;

public partial class GestionLieux
{
    private IReadOnlyList<LieuSection> _sections = Array.Empty<LieuSection>();
    private IReadOnlyList<LieuLocal> _locaux = Array.Empty<LieuLocal>();
    private IReadOnlyList<LieuContenant> _contenants = Array.Empty<LieuContenant>();

    private long? _sectionId;
    private long? _localId;

    private string _newSection = "";
    private string _newLocal = "";
    private string _newContenant = "";

    private bool _busy;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadSectionsAsync();

    private async Task LoadSectionsAsync()
    {
        _busy = true;
        try
        {
            _sections = await Lieux.GetSectionsAsync();
        }
        catch (Exception ex)
        {
            _error = "Lecture des sections impossible : " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SelectSectionAsync(long id)
    {
        _sectionId = id;
        _localId = null;
        _contenants = Array.Empty<LieuContenant>();
        _locaux = await Safe(() => Lieux.GetLocauxAsync(id), Array.Empty<LieuLocal>());
    }

    private async Task SelectLocalAsync(long id)
    {
        _localId = id;
        _contenants = await Safe(() => Lieux.GetContenantsAsync(id), Array.Empty<LieuContenant>());
    }

    private async Task AddSectionAsync()
    {
        await Run(() => Lieux.AddSectionAsync(_newSection));
        _newSection = "";
        await LoadSectionsAsync();
    }

    private async Task AddLocalAsync()
    {
        if (_sectionId is not { } sid) return;
        await Run(() => Lieux.AddLocalAsync(sid, _newLocal));
        _newLocal = "";
        _locaux = await Safe(() => Lieux.GetLocauxAsync(sid), _locaux);
    }

    private async Task AddContenantAsync()
    {
        if (_localId is not { } lid) return;
        await Run(() => Lieux.AddContenantAsync(lid, _newContenant));
        _newContenant = "";
        _contenants = await Safe(() => Lieux.GetContenantsAsync(lid), _contenants);
    }

    private async Task DeleteSectionAsync(long id)
    {
        await Run(() => Lieux.DeleteSectionAsync(id));
        if (_sectionId == id) { _sectionId = null; _localId = null; _locaux = Array.Empty<LieuLocal>(); _contenants = Array.Empty<LieuContenant>(); }
        await LoadSectionsAsync();
    }

    private async Task DeleteLocalAsync(long id)
    {
        await Run(() => Lieux.DeleteLocalAsync(id));
        if (_localId == id) { _localId = null; _contenants = Array.Empty<LieuContenant>(); }
        if (_sectionId is { } sid) _locaux = await Safe(() => Lieux.GetLocauxAsync(sid), _locaux);
    }

    private async Task DeleteContenantAsync(long id)
    {
        await Run(() => Lieux.DeleteContenantAsync(id));
        if (_localId is { } lid) _contenants = await Safe(() => Lieux.GetContenantsAsync(lid), _contenants);
    }

    private async Task Run(Func<Task<MoaMat.Web.Auth.AuthResult>> action)
    {
        _busy = true;
        _error = null;
        StateHasChanged();
        try
        {
            var result = await action();
            if (!result.Succeeded) _error = result.Error;
        }
        catch (Exception ex)
        {
            _error = "Opération impossible : " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<T> Safe<T>(Func<Task<T>> load, T fallback)
    {
        try
        {
            return await load();
        }
        catch (Exception ex)
        {
            _error = "Lecture impossible : " + ex.Message;
            return fallback;
        }
    }
}
