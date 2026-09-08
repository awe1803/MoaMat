using MoaMat.Web.Auth;
using static Supabase.Postgrest.Constants;

namespace MoaMat.Web.Data;

/// <summary>
/// Couche d'accès à la hiérarchie de lieux Section → Local → Contenant
/// (<c>db/model_item.sql</c>). Domaine de permission <c>referentiel</c> :
/// lecture pour tous les rôles, écriture réservée à <c>admin</c>+ par la RLS.
///
/// <para>Aucune logique d'autorisation ne s'appuie sur la section (Q18.3) :
/// c'est un axe de rangement, pas un périmètre de droits.</para>
/// </summary>
public sealed class LieuService
{
    private readonly Supabase.Client _supabase;

    public LieuService(Supabase.Client supabase) => _supabase = supabase;

    // -- Lecture --------------------------------------------------------------

    public async Task<IReadOnlyList<LieuSection>> GetSectionsAsync()
    {
        var response = await _supabase.From<LieuSection>()
            .Order("libelle", Ordering.Ascending)
            .Get();
        return response.Models;
    }

    public async Task<IReadOnlyList<LieuLocal>> GetLocauxAsync(long? sectionId = null)
    {
        var query = _supabase.From<LieuLocal>().Order("libelle", Ordering.Ascending);
        if (sectionId is { } id)
            query = query.Filter("section_id", Operator.Equals, id);
        var response = await query.Get();
        return response.Models;
    }

    public async Task<IReadOnlyList<LieuContenant>> GetContenantsAsync(long? localId = null)
    {
        var query = _supabase.From<LieuContenant>().Order("libelle", Ordering.Ascending);
        if (localId is { } id)
            query = query.Filter("local_id", Operator.Equals, id);
        var response = await query.Get();
        return response.Models;
    }

    /// <summary>Contenants avec leur chemin complet — pour les listes déroulantes.</summary>
    public async Task<IReadOnlyList<LieuContenantRow>> GetContenantRowsAsync()
    {
        var response = await _supabase.From<LieuContenantRow>()
            .Order("chemin", Ordering.Ascending)
            .Get();
        return response.Models;
    }

    // -- Écriture (admin+ via RLS) -----------------------------------------

    public Task<AuthResult> AddSectionAsync(string libelle) =>
        InsertAsync(new LieuSection { Libelle = libelle.Trim() });

    public Task<AuthResult> AddLocalAsync(long sectionId, string libelle) =>
        InsertAsync(new LieuLocal { SectionId = sectionId, Libelle = libelle.Trim() });

    public Task<AuthResult> AddContenantAsync(long localId, string libelle) =>
        InsertAsync(new LieuContenant { LocalId = localId, Libelle = libelle.Trim() });

    public async Task<AuthResult> DeleteSectionAsync(long id)
    {
        try
        {
            await _supabase.From<LieuSection>().Where(x => x.Id == id).Delete();
            return AuthResult.Ok;
        }
        catch (Exception) { return DeleteRefused(); }
    }

    public async Task<AuthResult> DeleteLocalAsync(long id)
    {
        try
        {
            await _supabase.From<LieuLocal>().Where(x => x.Id == id).Delete();
            return AuthResult.Ok;
        }
        catch (Exception) { return DeleteRefused(); }
    }

    public async Task<AuthResult> DeleteContenantAsync(long id)
    {
        try
        {
            await _supabase.From<LieuContenant>().Where(x => x.Id == id).Delete();
            return AuthResult.Ok;
        }
        catch (Exception) { return DeleteRefused(); }
    }

    // -- Interne ------------------------------------------------------------

    private static AuthResult DeleteRefused() =>
        AuthResult.Fail("Suppression refusée (droits insuffisants ou lieu utilisé).");

    private async Task<AuthResult> InsertAsync<T>(T model)
        where T : Supabase.Postgrest.Models.BaseModel, new()
    {
        try
        {
            await _supabase.From<T>().Insert(model);
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            return AuthResult.Fail("Création refusée (droits insuffisants ou doublon).");
        }
    }
}
