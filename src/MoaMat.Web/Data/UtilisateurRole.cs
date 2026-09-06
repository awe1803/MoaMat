using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Table <c>public.utilisateur_role</c> — une ligne par compte Supabase Auth.
/// Utilisée par <see cref="CompteService"/> pour attribuer un rôle depuis
/// l'écran <c>/comptes</c>. Le contrôle d'accès réel est porté par les policies
/// RLS <c>utilisateur_role_upd</c> / <c>utilisateur_role_del</c> : un changement
/// non autorisé (rôle élevé, compte « CA », sa propre ligne…) est refusé côté
/// base.
/// </summary>
[Table("utilisateur_role")]
public sealed class UtilisateurRole : BaseModel
{
    [PrimaryKey("user_id", false)]
    public Guid UserId { get; set; }

    [Column("role")]
    public string Role { get; set; } = "";
}
