using MoaMat.Web.Auth;
using Supabase.Postgrest;
using static Supabase.Postgrest.Constants;

namespace MoaMat.Web.Data;

/// <summary>Critères de filtrage de l'inventaire (tous optionnels).</summary>
public sealed class ItemFilter
{
    /// <summary><c>bouteille</c>, <c>detendeur</c>, … ou null = toutes familles.</summary>
    public string? Famille { get; set; }

    /// <summary>Code de <c>ref_statut</c>, ou null = tous statuts.</summary>
    public string? StatutCode { get; set; }

    /// <summary>Filtre par contenant (niveau fin de la hiérarchie de lieux).</summary>
    public long? LieuContenantId { get; set; }

    /// <summary>Ne garder que les items dont l'échéance est &lt;= à cette date.</summary>
    public DateTime? EcheanceAvant { get; set; }

    /// <summary>true = actifs (défaut), false = désactivés, null = les deux.</summary>
    public bool? Actif { get; set; } = true;

    /// <summary>Ne garder que les items au code club marqué ambigu.</summary>
    public bool CodesAmbigusUniquement { get; set; }
}

/// <summary>
/// Couche d'accès aux données de l'inventaire (modèle Item — <c>db/MODELE.md</c>).
/// Lecture via la vue <c>public.v_item</c> ; écriture / désactivation via la
/// table <c>public.item</c> ; lignes filles (<c>item_bouteille</c>, …) via les
/// helpers génériques.
///
/// <para><b>Ce service n'est pas la ligne de sécurité.</b> La sécurité effective
/// est portée par les policies RLS (<c>db/rls.sql</c>, permission <c>item.*</c>)
/// et la vue <c>security_invoker</c> : un appel non autorisé renvoie 0 ligne ou
/// lève une erreur, que ce service se contente de traduire.</para>
/// </summary>
public sealed class ItemService
{
    private readonly Supabase.Client _supabase;

    public ItemService(Supabase.Client supabase) => _supabase = supabase;

    // -- Lecture --------------------------------------------------------------

    /// <summary>Items correspondant à <paramref name="filter"/>, triés par code club.</summary>
    public async Task<IReadOnlyList<ItemRow>> GetItemsAsync(ItemFilter? filter = null)
    {
        filter ??= new ItemFilter();

        var query = _supabase.From<ItemRow>()
            .Order("code_club", Ordering.Ascending)
            .Order("id", Ordering.Ascending);

        if (!string.IsNullOrWhiteSpace(filter.Famille))
            query = query.Filter("famille", Operator.Equals, filter.Famille);

        if (!string.IsNullOrWhiteSpace(filter.StatutCode))
            query = query.Filter("statut_code", Operator.Equals, filter.StatutCode);

        if (filter.LieuContenantId is { } contenantId)
            query = query.Filter("lieu_contenant_id", Operator.Equals, contenantId);

        if (filter.EcheanceAvant is { } echeance)
            query = query.Filter("date_echeance", Operator.LessThanOrEqual, echeance.ToString("yyyy-MM-dd"));

        // Postgrest n'accepte pas un bool comme critère : PostgREST attend « is.true ».
        if (filter.Actif is { } actif)
            query = query.Filter("actif", Operator.Is, actif ? "true" : "false");

        if (filter.CodesAmbigusUniquement)
            query = query.Filter("code_club_ambigu", Operator.Is, "true");

        var response = await query.Get();
        return response.Models;
    }

    /// <summary>Un item de la vue de lecture, ou null.</summary>
    public async Task<ItemRow?> GetItemAsync(long id)
    {
        var response = await _supabase.From<ItemRow>()
            .Filter("id", Operator.Equals, id)
            .Get();
        return response.Models.FirstOrDefault();
    }

    /// <summary>Catalogue des statuts, trié pour l'affichage.</summary>
    public async Task<IReadOnlyList<RefStatut>> GetStatutsAsync()
    {
        var response = await _supabase.From<RefStatut>()
            .Order("ordre", Ordering.Ascending)
            .Get();
        return response.Models;
    }

    /// <summary>
    /// Ligne fille d'un item (<see cref="ItemBouteille"/>, <see cref="ItemDetendeur"/>, …),
    /// ou null si absente.
    /// </summary>
    public async Task<T?> GetChildAsync<T>(long itemId)
        where T : Supabase.Postgrest.Models.BaseModel, new()
    {
        var response = await _supabase.From<T>()
            .Filter("item_id", Operator.Equals, itemId)
            .Get();
        return response.Models.FirstOrDefault();
    }

    /// <summary>Valeurs Access non converties lors de la reprise (constats A11/A22).</summary>
    public async Task<IReadOnlyList<ItemReject>> GetRejectsAsync()
    {
        var response = await _supabase.From<ItemReject>()
            .Order("origine_table", Ordering.Ascending)
            .Order("origine_id", Ordering.Ascending)
            .Get();
        return response.Models;
    }

    // -- Écriture ----------------------------------------------------------

    /// <summary>Crée un item (tronc commun). Renvoie l'item persité (avec son id).</summary>
    public async Task<Item?> CreateAsync(Item item)
    {
        var response = await _supabase.From<Item>().Insert(item);
        return response.Models.FirstOrDefault();
    }

    /// <summary>Met à jour un item (identifié par <see cref="Item.Id"/>).</summary>
    public async Task<AuthResult> UpdateAsync(Item item)
    {
        try
        {
            await _supabase.From<Item>().Update(item);
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            return AuthResult.Fail("Enregistrement refusé (droits insuffisants ou données invalides).");
        }
    }

    /// <summary>Enregistre (upsert) une ligne fille de spécialisation.</summary>
    public async Task<AuthResult> UpsertChildAsync<T>(T child)
        where T : Supabase.Postgrest.Models.BaseModel, new()
    {
        try
        {
            await _supabase.From<T>().Upsert(child);
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            return AuthResult.Fail("Enregistrement du détail refusé (droits insuffisants ou données invalides).");
        }
    }

    /// <summary>
    /// Désactivation LOGIQUE (<c>actif = false</c>) ou réactivation. La couche
    /// métier ne supprime jamais physiquement un item.
    /// </summary>
    public async Task<AuthResult> SetActifAsync(long id, bool actif)
    {
        try
        {
            await _supabase.From<Item>()
                .Where(x => x.Id == id)
                .Set(x => x.Actif, actif)
                .Update();
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            return AuthResult.Fail(actif
                ? "Réactivation refusée (droits insuffisants)."
                : "Désactivation refusée (droits insuffisants).");
        }
    }

    /// <summary>
    /// Change le statut d'un item. Le retour depuis un statut terminal est
    /// arbitré côté base (permission <c>status.terminal.override</c>) : un refus
    /// est traduit ici en message.
    /// </summary>
    public async Task<AuthResult> SetStatutAsync(long id, string statutCode)
    {
        try
        {
            await _supabase.From<Item>()
                .Where(x => x.Id == id)
                .Set(x => x.StatutCode, statutCode)
                .Update();
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            return AuthResult.Fail("Changement de statut refusé (droits insuffisants ou statut terminal).");
        }
    }
}
