-- =============================================================================
--  MoaMat — Supabase Storage : buckets + policies d'accès par rôle
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter dans l'éditeur SQL Supabase APRÈS db/schema.sql.
--  Ré-exécutable sans erreur (upsert des buckets, drop/create des policies).
--
--  Modèle de rôles (analyse fonctionnelle) — du moins au plus privilégié :
--      lecture (1)  <  gestion (2)  <  admin (3)  <  super-admin (4)
--
--  Le rôle applicatif est porté par le JWT de l'utilisateur, dans
--  « app_metadata.role ». app_metadata n'est modifiable que côté serveur
--  (dashboard Supabase, ou Management/Admin API avec la clé service_role) :
--  un utilisateur ne peut donc pas s'auto-promouvoir depuis le client WASM.
--  Pour attribuer un rôle :
--      Dashboard → Authentication → Users → (utilisateur) → App Metadata :
--          { "role": "gestion" }
--
--  Trois buckets, tous PRIVÉS (aucun accès anonyme, pas d'URL publique) :
--      materiel-photos              photos de matériel
--      certificats-requalification  certificats de requalification des bouteilles
--      factures                     factures d'achat / prestations
--
--  Accès :
--      | bucket                       | lecture (download) | écriture (upload/maj/suppr) |
--      |------------------------------|--------------------|-----------------------------|
--      | materiel-photos              | rôle >= lecture    | rôle >= gestion             |
--      | certificats-requalification  | rôle >= lecture    | rôle >= gestion             |
--      | factures                     | rôle >= admin      | rôle >= admin               |
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Fonctions utilitaires de rôle
-- -----------------------------------------------------------------------------

-- Rôle applicatif de l'appelant, lu dans le claim JWT app_metadata.role.
-- Renvoie NULL si absent (utilisateur sans rôle => aucun accès).
create or replace function public.moamat_role()
returns text
language sql
stable
as $$
    select nullif(lower(coalesce(auth.jwt() -> 'app_metadata' ->> 'role', '')), '');
$$;

-- Rang numérique d'un rôle (défaut : le rôle de l'appelant).
-- Rôle inconnu / NULL => 0 (aucun privilège).
create or replace function public.moamat_role_rank(p_role text default public.moamat_role())
returns integer
language sql
immutable
as $$
    select case lower(coalesce(p_role, ''))
        when 'super-admin' then 4
        when 'superadmin'  then 4
        when 'admin'       then 3
        when 'gestion'     then 2
        when 'lecture'     then 1
        else 0
    end;
$$;

comment on function public.moamat_role()      is 'Rôle applicatif MoaMat de l''appelant (claim JWT app_metadata.role).';
comment on function public.moamat_role_rank(text) is 'Rang numérique du rôle : lecture=1, gestion=2, admin=3, super-admin=4, inconnu=0.';

-- -----------------------------------------------------------------------------
--  2. Buckets (upsert)
-- -----------------------------------------------------------------------------

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values
    ('materiel-photos',             'materiel-photos',             false, 10485760,
        array['image/jpeg', 'image/png', 'image/webp', 'image/heic']),
    ('certificats-requalification', 'certificats-requalification', false, 20971520,
        array['application/pdf', 'image/jpeg', 'image/png', 'image/webp']),
    ('factures',                    'factures',                    false, 20971520,
        array['application/pdf', 'image/jpeg', 'image/png', 'image/webp'])
on conflict (id) do update set
    public             = excluded.public,
    file_size_limit    = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

-- -----------------------------------------------------------------------------
--  3. Policies sur storage.objects
--     (RLS est déjà activé par défaut sur storage.objects dans Supabase.)
--     Nommage : moamat_<bucket>_<action>. On repart de zéro à chaque exécution.
-- -----------------------------------------------------------------------------

-- materiel-photos + certificats-requalification -------------------------------

drop policy if exists moamat_materiel_certifs_select on storage.objects;
create policy moamat_materiel_certifs_select on storage.objects
    for select to authenticated
    using (
        bucket_id in ('materiel-photos', 'certificats-requalification')
        and public.moamat_role_rank() >= 1
    );

drop policy if exists moamat_materiel_certifs_insert on storage.objects;
create policy moamat_materiel_certifs_insert on storage.objects
    for insert to authenticated
    with check (
        bucket_id in ('materiel-photos', 'certificats-requalification')
        and public.moamat_role_rank() >= 2
    );

drop policy if exists moamat_materiel_certifs_update on storage.objects;
create policy moamat_materiel_certifs_update on storage.objects
    for update to authenticated
    using (
        bucket_id in ('materiel-photos', 'certificats-requalification')
        and public.moamat_role_rank() >= 2
    )
    with check (
        bucket_id in ('materiel-photos', 'certificats-requalification')
        and public.moamat_role_rank() >= 2
    );

drop policy if exists moamat_materiel_certifs_delete on storage.objects;
create policy moamat_materiel_certifs_delete on storage.objects
    for delete to authenticated
    using (
        bucket_id in ('materiel-photos', 'certificats-requalification')
        and public.moamat_role_rank() >= 2
    );

-- factures (données financières : admin et plus) -----------------------------

drop policy if exists moamat_factures_select on storage.objects;
create policy moamat_factures_select on storage.objects
    for select to authenticated
    using (bucket_id = 'factures' and public.moamat_role_rank() >= 3);

drop policy if exists moamat_factures_insert on storage.objects;
create policy moamat_factures_insert on storage.objects
    for insert to authenticated
    with check (bucket_id = 'factures' and public.moamat_role_rank() >= 3);

drop policy if exists moamat_factures_update on storage.objects;
create policy moamat_factures_update on storage.objects
    for update to authenticated
    using (bucket_id = 'factures' and public.moamat_role_rank() >= 3)
    with check (bucket_id = 'factures' and public.moamat_role_rank() >= 3);

drop policy if exists moamat_factures_delete on storage.objects;
create policy moamat_factures_delete on storage.objects
    for delete to authenticated
    using (bucket_id = 'factures' and public.moamat_role_rank() >= 3);

commit;
