-- =============================================================================
--  MoaMat — Gestion des comptes utilisateurs : vue de liste + désactivation.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/roles.sql, db/permissions.sql, db/rls.sql et db/audit.sql.
--  AVANT db/storage.sql. Ré-exécutable sans erreur.
--
--  Sert l'écran /comptes (analyse fonctionnelle — gestion des comptes) :
--    * public.compte_utilisateur  — vue de liste (e-mail + rôle + état
--      d'activation). Réservée à la permission « compte.read » (admin +
--      super-admin) : un appelant non habilité obtient 0 ligne, exactement
--      comme public.audit_log.
--    * public.set_compte_actif()  — active / désactive un compte via la colonne
--      native auth.users.banned_until. Il n'existe AUCUNE suppression physique
--      d'un compte (traçabilité et intégrité des données historiques).
--
--  Règle métier : un compte « CA » = un compte au rôle « lecture ». Un admin ne
--  peut PAS désactiver un compte « lecture » (ni un compte « admin » /
--  « super-admin ») : seul un super-admin le peut (permission
--  « role.assign_admin »). L'auto-désactivation est refusée.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Vue de liste des comptes
--     Propriétaire « postgres » : la vue lit auth.users sans être soumise à la
--     RLS de ce schéma. Le filtrage d'accès est porté par la clause WHERE
--     (public.has_permission('compte.read')). SECURITY INVOKER laissé au défaut
--     (false) pour cette raison.
-- -----------------------------------------------------------------------------

-- drop explicite : « create or replace view » refuse tout changement de la
-- liste des colonnes ; on garantit ainsi la ré-exécutabilité si la vue évolue.
drop view if exists public.compte_utilisateur;

create view public.compte_utilisateur as
    select
        u.id                                                    as user_id,
        u.email                                                 as email,
        ur.role                                                 as role,
        (u.banned_until is not null and u.banned_until > now()) as desactive,
        u.created_at                                            as created_at,
        u.last_sign_in_at                                       as last_sign_in_at
    from auth.users u
    left join public.utilisateur_role ur on ur.user_id = u.id
    where public.has_permission('compte.read');

comment on view public.compte_utilisateur is
    'Liste des comptes (e-mail, rôle, état d''activation) pour l''écran /comptes. Réservée à la permission « compte.read » : 0 ligne pour un appelant non habilité.';

grant select on public.compte_utilisateur to authenticated;

-- -----------------------------------------------------------------------------
--  2. Activation / désactivation d'un compte
--     Bascule auth.users.banned_until. SECURITY DEFINER : la fonction doit
--     pouvoir écrire dans auth.users. Toutes les vérifications de droit sont
--     refaites ici (la fonction n'est pas protégée par une RLS).
-- -----------------------------------------------------------------------------

create or replace function public.set_compte_actif(p_user uuid, p_actif boolean)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_role public.app_role;
begin
    if not public.has_permission('compte.disable') then
        raise exception 'Droit insuffisant : permission « compte.disable » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_user = (select auth.uid()) then
        raise exception 'Auto-désactivation interdite.'
            using errcode = 'insufficient_privilege';
    end if;

    select ur.role into v_role
    from public.utilisateur_role ur
    where ur.user_id = p_user;

    -- Compte « CA » (rôle lecture), compte élevé (admin / super-admin) ou compte
    -- sans rôle : réservé au super-admin (permission « role.assign_admin »).
    if (v_role is null or v_role in ('lecture', 'admin', 'super-admin'))
       and not public.has_permission('role.assign_admin') then
        raise exception 'Compte protégé : seul un super-admin peut modifier l''activation de ce compte.'
            using errcode = 'insufficient_privilege';
    end if;

    update auth.users
    set banned_until = case when p_actif then null else 'infinity'::timestamptz end,
        updated_at   = now()
    where id = p_user;

    if not found then
        raise exception 'Compte introuvable : %.', p_user
            using errcode = 'no_data_found';
    end if;

    perform public.audit_write(
        case when p_actif then 'compte.enabled' else 'compte.disabled' end,
        'auth.users',
        p_user::text,
        jsonb_build_object('desactive', not p_actif),
        jsonb_build_object('desactive', p_actif),
        '{}'::jsonb
    );
end $$;

comment on function public.set_compte_actif(uuid, boolean) is
    'Active (p_actif = true) ou désactive (false) un compte via auth.users.banned_until. Exige « compte.disable » ; un compte lecture/admin/super-admin (ou sans rôle) exige « role.assign_admin ». Auto-désactivation refusée. Journalisé (compte.enabled / compte.disabled). Aucune suppression physique de compte.';

revoke execute on function public.set_compte_actif(uuid, boolean) from public;
grant execute on function public.set_compte_actif(uuid, boolean) to authenticated;

commit;
