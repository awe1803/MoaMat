-- =============================================================================
--  MoaMat — Fournisseurs / prestataires : coordonnées et archivage.
--  Les PRESTATIONS de chaque fournisseur et leurs tarifs datés sont dans
--  db/fournisseur_prestation.sql (catalogue par fournisseur).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/permissions.sql et db/rls.sql. Ré-exécutable.
--  Ordre : db/fournisseur.sql, puis db/fournisseur_prestation.sql, puis
--  db/campagne.sql.
--
--  Pourquoi une table dédiée et pas la table miroir public.fournisseur
--  (nom, specialite) : db/schema.sql la supprime et la recrée à chaque rejeu
--  (miroir Access), toute colonne ajoutée y serait perdue. Les permissions
--  `fournisseur.*` (db/permissions.sql) sont en revanche réutilisées telles
--  quelles.
--
--  Historique : une première version de ce fichier portait aussi trois
--  prestations figées (RR / hydraulique huile / hydraulique eau) et leurs prix
--  (ref_fournisseur_prestation, ref_fournisseur_tarif, campagne_tarif_prestation,
--  v_tarif_campagne). Ce modèle est REMPLACÉ par le catalogue libre de
--  db/fournisseur_prestation.sql : ses données y sont reprises, puis ces
--  anciennes tables sont supprimées par
--  db/migration_prestations_fournisseur.sql.
--
--  Ce fichier pose :
--    1. public.ref_fournisseur — coordonnées d'un prestataire (nom unique,
--       personne de contact, téléphone, email, site web, adresse) et archivage
--       (actif=false : conservé pour l'historique, non proposé aux campagnes).
--    2. Ses policies RLS : lecture lecture+, création/modification gestion+,
--       suppression admin+ (permissions du domaine `fournisseur`). La
--       suppression d'un fournisseur qui a un catalogue passe par
--       public.supprimer_fournisseur() (db/fournisseur_prestation.sql).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Fournisseurs
-- -----------------------------------------------------------------------------

create table if not exists public.ref_fournisseur (
    id               bigint generated always as identity primary key,
    nom              text not null check (btrim(nom) <> '' and char_length(nom) <= 120),
    adresse          text check (adresse is null or char_length(adresse) <= 300),
    telephone        text check (telephone is null or char_length(telephone) <= 40),
    email            text check (email is null or (char_length(email) <= 200 and email ~ '^[^@\s]+@[^@\s]+\.[^@\s]+$')),
    personne_contact text check (personne_contact is null or char_length(personne_contact) <= 120),
    site_web         text check (site_web is null or (char_length(site_web) <= 300 and site_web ~* '^https?://[^\s]+$')),
    actif            boolean not null default true,
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now()   -- tenu par public.tg_set_updated_at()
);

comment on table public.ref_fournisseur is
    'Fournisseurs / prestataires du club (coordonnées). Distinct de la table miroir public.fournisseur (Access), recréée à chaque rejeu de db/schema.sql. actif=false : fournisseur archivé, conservé pour l''historique. Leurs prestations et tarifs : public.fournisseur_prestation / fournisseur_prestation_tarif (db/fournisseur_prestation.sql).';

create unique index if not exists ux_ref_fournisseur_nom
    on public.ref_fournisseur (lower(btrim(nom)));

drop trigger if exists ref_fournisseur_set_updated_at on public.ref_fournisseur;
create trigger ref_fournisseur_set_updated_at
    before update on public.ref_fournisseur
    for each row execute function public.tg_set_updated_at();

-- -----------------------------------------------------------------------------
--  2. RLS — permissions du domaine `fournisseur` (db/permissions.sql) :
--     lecture : lecture+ ; création/modification : gestion+ ; suppression : admin+.
-- -----------------------------------------------------------------------------

alter table public.ref_fournisseur enable row level security;

drop policy if exists ref_fournisseur_sel on public.ref_fournisseur;
create policy ref_fournisseur_sel on public.ref_fournisseur
    for select to authenticated using (public.has_permission('fournisseur.read'));

drop policy if exists ref_fournisseur_ins on public.ref_fournisseur;
create policy ref_fournisseur_ins on public.ref_fournisseur
    for insert to authenticated with check (public.has_permission('fournisseur.create'));

drop policy if exists ref_fournisseur_upd on public.ref_fournisseur;
create policy ref_fournisseur_upd on public.ref_fournisseur
    for update to authenticated
    using (public.has_permission('fournisseur.update'))
    with check (public.has_permission('fournisseur.update'));

drop policy if exists ref_fournisseur_del on public.ref_fournisseur;
create policy ref_fournisseur_del on public.ref_fournisseur
    for delete to authenticated using (public.has_permission('fournisseur.delete'));

commit;
