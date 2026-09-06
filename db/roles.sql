-- =============================================================================
--  MoaMat — Rôles applicatifs : table liée à auth.users, provisionnement par
--  trigger Postgres, fonctions de rôle et hook de jeton d'accès.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/schema.sql et db/initial_load.sql, AVANT db/permissions.sql,
--  db/rls.sql, db/audit.sql et db/storage.sql. Ré-exécutable sans erreur.
--
--  Modèle à 4 rôles (analyse fonctionnelle §2), du moins au plus privilégié :
--      lecture (1)  <  gestion (2)  <  admin (3)  <  super-admin (4)
--
--  Source de vérité : la table public.utilisateur_role (une ligne par
--  utilisateur Supabase Auth). Il n'y a AUCUNE synchronisation applicative
--  côté client :
--    * à la création d'un compte, le trigger on_auth_user_created insère
--      automatiquement une ligne au rôle « lecture » (jamais un rôle élevé) ;
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
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Type énuméré des rôles
-- -----------------------------------------------------------------------------

do $$
begin
    if not exists (select 1 from pg_type where typname = 'app_role') then
        create type public.app_role as enum ('lecture', 'gestion', 'admin', 'super-admin');
    end if;
end $$;

-- -----------------------------------------------------------------------------
--  2. Table Utilisateurs / Rôles — liée à auth.users(id)
-- -----------------------------------------------------------------------------

create table if not exists public.utilisateur_role (
    user_id     uuid primary key references auth.users (id) on delete cascade,
    role        public.app_role not null default 'lecture',
    assigned_by uuid references auth.users (id) on delete set null,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now()
);

comment on table  public.utilisateur_role        is 'Rôle applicatif MoaMat d''un utilisateur Supabase Auth. Alimentée par trigger à la création du compte ; jamais synchronisée depuis le client.';
comment on column public.utilisateur_role.role   is 'lecture | gestion | admin | super-admin. Défaut = lecture. « super-admin » jamais posé par défaut.';

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
-- Rôle inconnu / NULL => 0 (aucun privilège).
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
comment on function public.moamat_role_rank(text) is 'Rang : lecture=1, gestion=2, admin=3, super-admin=4, inconnu=0.';
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
    -- Rôle minimal par défaut. JAMAIS « super-admin » : ce siège reste vacant
    -- jusqu'à nomination explicite (SQL ou Edge Function).
    insert into public.utilisateur_role (user_id, role)
    values (new.id, 'lecture')
    on conflict (user_id) do nothing;
    return new;
end $$;

comment on function public.handle_new_user() is 'Insère une ligne public.utilisateur_role au rôle « lecture » à chaque création de compte Supabase Auth.';

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function public.handle_new_user();

-- Rattrapage des comptes déjà existants (idempotent).
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
