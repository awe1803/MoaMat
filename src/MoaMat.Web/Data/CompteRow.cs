using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace MoaMat.Web.Data;

/// <summary>
/// Ligne de la vue <c>public.compte_utilisateur</c> : un compte Supabase Auth
/// avec son rôle applicatif et son état d'activation. La vue est réservée à la
/// permission <c>compte.read</c> (rôles <c>admin</c> / <c>super-admin</c>) : un
/// appel effectué par un utilisateur non habilité renvoie simplement 0 ligne.
/// Lecture seule — les changements passent par <see cref="CompteService"/>.
/// </summary>
[Table("compte_utilisateur")]
public sealed class CompteRow : BaseModel
{
    [PrimaryKey("user_id", false)]
    public Guid UserId { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("desactive")]
    public bool Desactive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("last_sign_in_at")]
    public DateTimeOffset? LastSignInAt { get; set; }
}
