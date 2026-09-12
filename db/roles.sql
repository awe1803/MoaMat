-- =============================================================================
--  MoaMat — Rôles applicatifs : table liée à auth.users, provisionnement par
--  trigger Postgres, fonctions de rôle et hook de jeton d'accès.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/schema.sql et db/initial_load.sql, AVANT db/permissions.sql,
--  db/rls.sql, db/audit.sql et db/storage.sql. Ré-exécutable sans erreur.
--
--  Modèle à 5 rôles (analyse fonctionnelle §2), du moins au plus privilégié :
--      en_attente (0)  <  lecture (1)  <  gestion (2)  <  admin (3)  <  super-admin (4)
--
--  Source de vérité : la table public.utilisateur_role (une ligne par
--  utilisateur Supabase Auth). Il n'y a AUCUNE synchronisation applicative
--  côté client :
--    * à la création d'un compte, le trigger on_auth_user_created insère
--      automatiquement une ligne au rôle « en_attente » (jamais un rôle élevé,
--      et jamais « lecture » directement) ;
--    * un compte « en_attente » n'a AUCUNE permission (aucune ligne dans
--      public.role_permission — db/permissions.sql) : il demande l'accès et
--      reste en attente tant qu'un admin / super-admin ne l'a pas activé et
--      affecté explicitement à un rôle (lecture / gestion / admin) ; les
--      policies RLS (db/rls.sql) ne lui laissent consulter que sa propre ligne
--      public.utilisateur_role, rien d'autre ;
--    * les policies RLS lisent le rôle via public.moamat_current_role(), qui
--      interroge cette table (et NON un claim que le client pourrait falsifier) ;
--    * public.custom_access_token_hook() recopie le rôle dans le JWT
--      (app_metadata.role) au moment de l'émission du jeton, pour l'affichage
--      côté client (Blazor lit ce claim pour piloter la navigation).
--
--  Le rôle « super-admin » est VACANT à l'initialisation. Il n'est jamais
--  attribué par défaut ni codé en dur. Il s'attribue :
--    * soit directement en base (requête SQL documentée ci-dessous),
--    * soit via l'Edge Function supabase/functions/nominate-super-admin.
--
--  Protection du super-admin (section 6 ci-dessous) : un compte super-admin ne
--  peut jamais être supprimé (ligne de rôle ou compte auth.users), une
--  rétrogradation qui ne laisserait plus aucun super-admin actif est refusée,
--  et seul un autre super-admin peut modifier la ligne d'un super-admin.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Type énuméré des rôles
-- -----------------------------------------------------------------------------

do $$
begin
    if not exists (select 1 from pg_type where typname = 'app_role') then
        create type public.app_role as enum ('en_attente', 'lecture', 'gestion', 'admin', 'super-admin');
    end if;
end $$;

-- Ajout idempotent pour une base déjà provisionnée avant l'introduction du
-- rôle « en_attente » (ADD VALUE ne peut pas être exécuté dans le bloc do $$
-- ci-dessus : PostgreSQL interdit un ALTER TYPE ... ADD VALUE dans une
-- transaction qui utiliserait ensuite la nouvelle valeur).
alter type public.app_role add value if not exists 'en_attente' before 'lecture';

-- -----------------------------------------------------------------------------
--  2. Table Utilisateurs / Rôles — liée à auth.users(id)
-- -----------------------------------------------------------------------------

create table if not exists public.utilisateur_role (
    user_id     uuid primary key references auth.users (id) on delete cascade,
    role        public.app_role not null default 'en_attente',
    assigned_by uuid references auth.users (id) on delete set null,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now()
);

comment on table  public.utilisateur_role        is 'Rôle applicatif MoaMat d''un utilisateur Supabase Auth. Alimentée par trigger à la création du compte ; jamais synchronisée depuis le client.';
comment on column public.utilisateur_role.role   is 'en_attente | lecture | gestion | admin | super-admin. Défaut = en_attente (aucune permission, compte en attente d''activation). « super-admin » jamais posé par défaut.';

-- Horodatage de modification
create or replace function public.tg_set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at := now();
    return new;
end $$;

drop trigger if exists set_updated_at on public.utilisateur_role;
create trigger set_updated_at
    before update on public.utilisateur_role
    for each row execute function public.tg_set_updated_at();

-- -----------------------------------------------------------------------------
--  3. Fonctions de rôle (lues par les policies RLS et par db/storage.sql)
-- -----------------------------------------------------------------------------

-- Rôle applicatif de l'appelant, lu dans public.utilisateur_role via auth.uid().
-- SECURITY DEFINER : la fonction doit voir la table même si l'appelant n'a pas
-- le droit de la lire directement. NULL => utilisateur sans rôle (aucun accès).
create or replace function public.moamat_current_role()
returns public.app_role
language sql
stable
security definer
set search_path = ''
as $$
    select ur.role
    from public.utilisateur_role ur
    where ur.user_id = (select auth.uid())
$$;

-- Variante texte — conservée pour compatibilité avec db/storage.sql.
create or replace function public.moamat_role()
returns text
language sql
stable
set search_path = ''
as $$
    select public.moamat_current_role()::text
$$;

-- Rang numérique d'un rôle (défaut : le rôle de l'appelant).
-- « en_attente », rôle inconnu ou NULL => 0 (aucun privilège).
create or replace function public.moamat_role_rank(p_role text default public.moamat_role())
returns integer
language sql
immutable
set search_path = ''
as $$
    select case lower(coalesce(p_role, ''))
        when 'super-admin' then 4
        when 'superadmin'  then 4
        when 'admin'       then 3
        when 'gestion'     then 2
        when 'lecture'     then 1
        when 'en_attente'  then 0
        else 0
    end
$$;

-- Raccourci « au moins admin » (utilisé par quelques policies).
create or replace function public.moamat_is_admin()
returns boolean
language sql
stable
set search_path = ''
as $$
    select public.moamat_role_rank(public.moamat_role()) >= 3
$$;

comment on function public.moamat_current_role()  is 'Rôle applicatif MoaMat de l''appelant, lu dans public.utilisateur_role (source de vérité). NULL si aucun rôle.';
comment on function public.moamat_role()          is 'public.moamat_current_role() sous forme texte (compat. db/storage.sql).';
comment on function public.moamat_role_rank(text) is 'Rang : en_attente=0, lecture=1, gestion=2, admin=3, super-admin=4, inconnu=0.';
comment on function public.moamat_is_admin()      is 'Vrai si l''appelant est admin ou super-admin.';

grant execute on function public.moamat_current_role()      to anon, authenticated;
grant execute on function public.moamat_role()              to anon, authenticated;
grant execute on function public.moamat_role_rank(text)     to anon, authenticated;
grant execute on function public.moamat_is_admin()          to anon, authenticated;

-- -----------------------------------------------------------------------------
--  4. Provisionnement automatique à la création d'un compte
--     Trigger Postgres sur auth.users — AUCUN code applicatif côté client.
-- -----------------------------------------------------------------------------

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    -- Rôle minimal par défaut : « en_attente ». Un utilisateur qui s'inscrit via
    -- Supabase Auth n'a JAMAIS d'accès direct à l'application ni un rôle élevé :
    -- il demande l'accès, et son compte reste sans permission tant qu'un
    -- admin / super-admin ne l'a pas explicitement activé et affecté à un rôle
    -- (lecture / gestion / admin). JAMAIS « super-admin » : ce siège reste
    -- vacant jusqu'à nomination explicite (SQL ou Edge Function).
    insert into public.utilisateur_role (user_id, role)
    values (new.id, 'en_attente')
    on conflict (user_id) do nothing;
    return new;
end $$;

comment on function public.handle_new_user() is 'Insère une ligne public.utilisateur_role au rôle « en_attente » (aucune permission) à chaque création de compte Supabase Auth.';

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function public.handle_new_user();

-- Rattrapage des comptes DÉJÀ EXISTANTS (idempotent). Contrairement au trigger
-- (qui pose désormais « en_attente » pour tout NOUVEAU compte), ce filet de
-- sécurité conserve « lecture » : il vise des comptes créés avant ce fichier,
-- déjà en usage, qui n'ont jamais eu de ligne de rôle ; les faire retomber en
-- « en_attente » à la prochaine exécution de ce script leur couperait
-- silencieusement l'accès en production.
insert into public.utilisateur_role (user_id, role)
select u.id, 'lecture'
from auth.users u
on conflict (user_id) do nothing;

-- -----------------------------------------------------------------------------
--  5. Hook de jeton d'accès — recopie le rôle dans le JWT (app_metadata.role)
--     À activer : Dashboard → Authentication → Hooks → « Custom Access Token ».
--     Le client Blazor lit app_metadata.role pour piloter la navigation ; il ne
--     s'agit que d'un miroir — l'autorité reste la table + les policies RLS.
-- -----------------------------------------------------------------------------

-- NB : pas de SECURITY DEFINER — la fonction s'exécute en tant que
-- supabase_auth_admin, à qui l'on accorde explicitement la lecture de la table
-- (grants plus bas), conformément au modèle officiel des Auth Hooks Supabase.
create or replace function public.custom_access_token_hook(event jsonb)
returns jsonb
language plpgsql
stable
set search_path = ''
as $$
declare
    v_claims jsonb;
    v_role   text;
begin
    select ur.role::text
    into v_role
    from public.utilisateur_role ur
    where ur.user_id = (event ->> 'user_id')::uuid;

    v_claims := coalesce(event -> 'claims', '{}'::jsonb);
    v_claims := jsonb_set(
        v_claims,
        '{app_metadata}',
        coalesce(v_claims -> 'app_metadata', '{}'::jsonb) || jsonb_build_object('role', v_role),
        true
    );

    return jsonb_set(event, '{claims}', v_claims);
end $$;

comment on function public.custom_access_token_hook(jsonb) is 'Auth hook Supabase : injecte utilisateur_role.role dans le claim app_metadata.role du JWT.';

grant execute on function public.custom_access_token_hook(jsonb) to supabase_auth_admin;
revoke execute on function public.custom_access_token_hook(jsonb) from authenticated, anon, public;
grant all on table public.utilisateur_role to supabase_auth_admin;

-- -----------------------------------------------------------------------------
--  6. Protection du super-admin au niveau base
--     Ces triggers sont une DEUXIÈME ligne de défense, au-dessus des policies
--     RLS de db/rls.sql : ils s'appliquent même à un appelant qui contourne la
--     RLS (postgres, service_role), à la manière du verrou append-only de
--     public.audit_log (db/audit.sql). Trois invariants :
--       a) une ligne de rôle « super-admin » ne peut JAMAIS être supprimée
--          (seule une rétrogradation via UPDATE est possible) ;
--       b) une rétrogradation ne peut jamais laisser zéro super-admin restant ;
--       c) côté PostgREST (rôle « authenticated »), seul un autre super-admin
--          peut modifier la ligne d'un super-admin — RLS impose déjà la même
--          règle via la permission « role.assign_admin » ; service_role /
--          postgres restent des opérateurs de confiance, comme pour la RLS.
-- -----------------------------------------------------------------------------

create or replace function public.tg_protect_super_admin_delete()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if old.role = 'super-admin' then
        raise exception 'Suppression interdite : le compte % est super-admin (rétrogradation obligatoire via UPDATE, jamais de suppression).', old.user_id
            using errcode = 'insufficient_privilege';
    end if;
    return old;
end $$;

comment on function public.tg_protect_super_admin_delete() is 'Bloque toute suppression de la ligne public.utilisateur_role d''un compte super-admin, y compris pour postgres / service_role.';

drop trigger if exists protect_super_admin_delete on public.utilisateur_role;
create trigger protect_super_admin_delete
    before delete on public.utilisateur_role
    for each row execute function public.tg_protect_super_admin_delete();

create or replace function public.tg_protect_super_admin_update()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_remaining integer;
begin
    if old.role = 'super-admin' and new.role <> 'super-admin' then
        -- Compte les AUTRES super-admin ACTIFS (compte désactivé exclu, cf.
        -- auth.users.banned_until) : l'exigence est qu'il reste au moins un
        -- super-admin actif après la modification, pas seulement une ligne au
        -- rôle super-admin qui pourrait être désactivée.
        select count(*) into v_remaining
        from public.utilisateur_role ur
        join auth.users u on u.id = ur.user_id
        where ur.role = 'super-admin'
          and ur.user_id <> old.user_id
          and (u.banned_until is null or u.banned_until <= now());

        if v_remaining = 0 then
            raise exception 'Rétrogradation refusée : % est le dernier super-admin actif (au moins un super-admin actif doit toujours exister).', old.user_id
                using errcode = 'insufficient_privilege';
        end if;
    end if;

    -- Défense en profondeur : côté PostgREST (rôle « authenticated »), seul un
    -- autre super-admin peut toucher la ligne d'un super-admin. service_role et
    -- postgres contournent ce contrôle, comme ils contournent la RLS.
    if old.role = 'super-admin'
       and current_setting('role', true) = 'authenticated'
       and public.moamat_current_role() <> 'super-admin' then
        raise exception 'Modification refusée : seul un autre super-admin peut modifier ce compte.'
            using errcode = 'insufficient_privilege';
    end if;

    return new;
end $$;

comment on function public.tg_protect_super_admin_update() is 'Empêche de rétrograder le dernier super-admin restant et, côté authenticated, restreint la modification d''un super-admin à un autre super-admin.';

drop trigger if exists protect_super_admin_update on public.utilisateur_role;
create trigger protect_super_admin_update
    before update on public.utilisateur_role
    for each row execute function public.tg_protect_super_admin_update();

-- Protection symétrique côté auth.users : empêche la suppression PHYSIQUE du
-- compte (pas seulement de sa ligne de rôle) tant qu'il est super-admin.
create or replace function public.tg_protect_super_admin_account_delete()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if exists (
        select 1 from public.utilisateur_role
        where user_id = old.id and role = 'super-admin'
    ) then
        raise exception 'Suppression de compte interdite : % est super-admin (rétrograder le rôle avant toute suppression).', old.id
            using errcode = 'insufficient_privilege';
    end if;
    return old;
end $$;

comment on function public.tg_protect_super_admin_account_delete() is 'Bloque la suppression du compte auth.users d''un super-admin.';

drop trigger if exists protect_super_admin_account_delete on auth.users;
create trigger protect_super_admin_account_delete
    before delete on auth.users
    for each row execute function public.tg_protect_super_admin_account_delete();

commit;

-- =============================================================================
--  NOMINATION DU SUPER-ADMIN (siège vacant à l'initialisation)
--  ---------------------------------------------------------------------------
--  À exécuter UNE FOIS, par la personne qui administre le projet Supabase,
--  en remplaçant l'adresse e-mail par celle du membre désigné par le CA.
--
--      insert into public.utilisateur_role (user_id, role)
--      select id, 'super-admin' from auth.users where email = 'a.definir@royalmoana.be'
--      on conflict (user_id) do update set role = 'super-admin', updated_at = now();
--
--  Ensuite, les nominations / transferts se font via l'Edge Function
--  supabase/functions/nominate-super-admin (réservée aux super-admin en place ;
--  tolère un premier appel par un admin tant que le siège est vacant).
-- =============================================================================
