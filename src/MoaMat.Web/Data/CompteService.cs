using MoaMat.Web.Auth;
using Supabase.Postgrest;

namespace MoaMat.Web.Data;

/// <summary>
/// Écran <c>/comptes</c> : liste des comptes (vue <c>public.compte_utilisateur</c>),
/// attribution de rôle (table <c>public.utilisateur_role</c>, sous RLS) et
/// activation / désactivation (RPC <c>public.set_compte_actif</c>). Aucun compte
/// n'est jamais supprimé.
///
/// <para>Le contrôle d'accès réel est côté base : la vue filtre sur la
/// permission <c>compte.read</c>, les policies RLS et la fonction
/// <c>set_compte_actif</c> refusent toute action interdite (rôle élevé, compte
/// « CA », auto-désactivation…). Ce service se contente de traduire un refus en
/// message.</para>
/// </summary>
public sealed class CompteService
{
    private readonly Supabase.Client _supabase;

    public CompteService(Supabase.Client supabase) => _supabase = supabase;

    /// <summary>Tous les comptes visibles par l'appelant, triés par e-mail.</summary>
    public async Task<IReadOnlyList<CompteRow>> GetComptesAsync()
    {
        var response = await _supabase
            .From<CompteRow>()
            .Order("email", Constants.Ordering.Ascending)
            .Get();
        return response.Models;
    }

    /// <summary>Attribue <paramref name="role"/> au compte <paramref name="userId"/>.</summary>
    public async Task<AuthResult> SetRoleAsync(Guid userId, string role)
    {
        try
        {
            await _supabase
                .From<UtilisateurRole>()
                .Where(x => x.UserId == userId)
                .Set(x => x.Role, role)
                .Update();
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            // Refus RLS (rôle élevé, compte « CA », sa propre ligne…) ou aléa réseau.
            return AuthResult.Fail("Changement de rôle refusé (droits insuffisants ou compte protégé).");
        }
    }

    /// <summary>Active (<paramref name="actif"/> = true) ou désactive un compte.</summary>
    public async Task<AuthResult> SetActifAsync(Guid userId, bool actif)
    {
        try
        {
            await _supabase.Rpc("set_compte_actif", new Dictionary<string, object>
            {
                ["p_user"] = userId,
                ["p_actif"] = actif,
            });
            return AuthResult.Ok;
        }
        catch (Exception)
        {
            // La fonction set_compte_actif lève 42501 si l'action est interdite
            // (compte « CA » / élevé, auto-désactivation, permission manquante).
            return AuthResult.Fail(actif
                ? "Réactivation refusée (droits insuffisants ou compte protégé)."
                : "Désactivation refusée (droits insuffisants ou compte protégé).");
        }
    }
}
