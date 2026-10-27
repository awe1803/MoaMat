-- =============================================================================
--  MoaMat — Fournisseurs / prestataires : coordonnées, prestations proposées
--  (RR, hydraulique eau, hydraulique huile) et historique des tarifs.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/permissions.sql, db/rls.sql et db/item_bouteille.sql
--  (amorçage des tarifs Apragaz depuis public.ref_tarif_apragaz). Ré-exécutable.
--
--  Pourquoi de NOUVELLES tables et pas la table miroir public.fournisseur
--  (nom, specialite) : db/schema.sql la supprime et la recrée à chaque rejeu
--  (miroir Access), toute colonne ajoutée y serait perdue. Les permissions
--  `fournisseur.*` (db/permissions.sql) sont en revanche réutilisées telles
--  quelles.
--
--  Ce fichier pose :
--    1. public.ref_fournisseur — coordonnées d'un prestataire.
--    2. public.ref_fournisseur_prestation — prestations qu'il propose.
--    3. public.ref_fournisseur_tarif — historique daté des prix (un changement
--       de prix = une NOUVELLE ligne avec un date_effet plus récent ; jamais
--       d'UPDATE, même doctrine que public.ref_tarif_apragaz).
--    4. public.fournisseur_tarif_en_vigueur() — résolveur de prix.
--    5. Reprise UNIQUE des tarifs Apragaz depuis public.ref_tarif_apragaz
--       (seulement si Apragaz n'existe pas encore).
--    6. Tarification des campagnes de bouteilles : public.campagne_tarif_prestation()
--       et public.v_tarif_campagne lisent UNIQUEMENT les prix du fournisseur
--       « Apragaz » (aucun repli sur l'ancien référentiel). db/campagne.sql
--       appelle cette fonction ; ce fichier doit donc être exécuté avant
--       d'utiliser les campagnes.
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
    'Fournisseurs / prestataires du club (coordonnées). Distinct de la table miroir public.fournisseur (Access), recréée à chaque rejeu de db/schema.sql. actif=false : fournisseur archivé, conservé pour l''historique.';

create unique index if not exists ux_ref_fournisseur_nom
    on public.ref_fournisseur (lower(btrim(nom)));

drop trigger if exists ref_fournisseur_set_updated_at on public.ref_fournisseur;
create trigger ref_fournisseur_set_updated_at
    before update on public.ref_fournisseur
    for each row execute function public.tg_set_updated_at();

-- -----------------------------------------------------------------------------
--  2. Prestations proposées
-- -----------------------------------------------------------------------------

create table if not exists public.ref_fournisseur_prestation (
    fournisseur_id  bigint not null references public.ref_fournisseur (id) on delete cascade,
    type_prestation text not null check (type_prestation in ('rr', 'hydraulique_huile', 'hydraulique_eau')),
    primary key (fournisseur_id, type_prestation)
);

comment on table public.ref_fournisseur_prestation is
    'Prestations proposées par un fournisseur (mêmes codes que public.ref_tarif_apragaz.type_prestation).';

-- -----------------------------------------------------------------------------
--  3. Historique des tarifs
-- -----------------------------------------------------------------------------

create table if not exists public.ref_fournisseur_tarif (
    id              bigint generated always as identity primary key,
    fournisseur_id  bigint not null references public.ref_fournisseur (id) on delete cascade,
    type_prestation text not null,
    prix_eur        numeric(12,2) not null check (prix_eur >= 0),
    date_effet      date not null,
    cree_le         timestamptz not null default now(),
    cree_par        uuid default auth.uid(),
    unique (fournisseur_id, type_prestation, date_effet),
    -- Un prix n'existe que pour une prestation que le fournisseur propose.
    -- Sans ON DELETE CASCADE à dessein : retirer une prestation qui a des prix
    -- est refusé, l'historique ne disparaît jamais par effet de bord (seule la
    -- suppression du fournisseur lui-même, réservée à fournisseur.delete, l'emporte).
    foreign key (fournisseur_id, type_prestation)
        references public.ref_fournisseur_prestation (fournisseur_id, type_prestation)
);

comment on table public.ref_fournisseur_tarif is
    'Historique daté des prix par (fournisseur, prestation). Changer un prix = insérer une nouvelle ligne avec un date_effet plus récent (pas d''UPDATE). Retirer une prestation qui a des prix est refusé. La suppression d''une ligne (saisie erronée) est réservée à fournisseur.delete.';

create index if not exists ix_ref_fournisseur_tarif_lookup
    on public.ref_fournisseur_tarif (fournisseur_id, type_prestation, date_effet desc);

-- -----------------------------------------------------------------------------
--  4. Résolveur de prix
-- -----------------------------------------------------------------------------

create or replace function public.fournisseur_tarif_en_vigueur(
    p_fournisseur_id bigint,
    p_type_prestation text,
    p_a_la_date date default current_date)
returns numeric
language sql
stable
set search_path = ''
as $$
    select prix_eur
    from public.ref_fournisseur_tarif
    where fournisseur_id = p_fournisseur_id
      and type_prestation = p_type_prestation
      and date_effet <= p_a_la_date
    order by date_effet desc
    limit 1
$$;

comment on function public.fournisseur_tarif_en_vigueur(bigint, text, date) is
    'Prix (EUR) en vigueur à p_a_la_date pour (fournisseur, prestation). NULL si aucun tarif n''est encore effectif.';

-- -----------------------------------------------------------------------------
--  5. RLS — permissions du domaine `fournisseur` (db/permissions.sql) :
--     lecture : lecture+ ; création/modification : gestion+ ; suppression : admin+.
--     Les prestations et tarifs sont une modification du fournisseur
--     (fournisseur.update) ; seule la suppression d'un tarif exige
--     fournisseur.delete. Aucune policy UPDATE sur les tarifs.
-- -----------------------------------------------------------------------------

alter table public.ref_fournisseur            enable row level security;
alter table public.ref_fournisseur_prestation enable row level security;
alter table public.ref_fournisseur_tarif      enable row level security;

-- ref_fournisseur
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

-- ref_fournisseur_prestation
drop policy if exists ref_fournisseur_prestation_sel on public.ref_fournisseur_prestation;
create policy ref_fournisseur_prestation_sel on public.ref_fournisseur_prestation
    for select to authenticated using (public.has_permission('fournisseur.read'));

drop policy if exists ref_fournisseur_prestation_ins on public.ref_fournisseur_prestation;
create policy ref_fournisseur_prestation_ins on public.ref_fournisseur_prestation
    for insert to authenticated with check (public.has_permission('fournisseur.update'));

drop policy if exists ref_fournisseur_prestation_del on public.ref_fournisseur_prestation;
create policy ref_fournisseur_prestation_del on public.ref_fournisseur_prestation
    for delete to authenticated using (public.has_permission('fournisseur.update'));

-- ref_fournisseur_tarif
drop policy if exists ref_fournisseur_tarif_sel on public.ref_fournisseur_tarif;
create policy ref_fournisseur_tarif_sel on public.ref_fournisseur_tarif
    for select to authenticated using (public.has_permission('fournisseur.read'));

drop policy if exists ref_fournisseur_tarif_ins on public.ref_fournisseur_tarif;
create policy ref_fournisseur_tarif_ins on public.ref_fournisseur_tarif
    for insert to authenticated with check (public.has_permission('fournisseur.update'));

drop policy if exists ref_fournisseur_tarif_del on public.ref_fournisseur_tarif;
create policy ref_fournisseur_tarif_del on public.ref_fournisseur_tarif
    for delete to authenticated using (public.has_permission('fournisseur.delete'));

-- -----------------------------------------------------------------------------
--  6. Reprise UNIQUE des tarifs Apragaz — le fournisseur « Apragaz », ses
--     prestations et l'historique de public.ref_tarif_apragaz sont copiés
--     UNIQUEMENT quand Apragaz n'existe pas encore. Ensuite les prix ne vivent
--     que dans public.ref_fournisseur_tarif : un rejeu de ce fichier ne
--     réintroduit jamais une ligne de l'ancien référentiel (qui n'est plus lu
--     par l'application et peut être supprimé une fois la reprise faite).
-- -----------------------------------------------------------------------------

do $$
declare
    v_id bigint;
begin
    if exists (select 1 from public.ref_fournisseur where lower(btrim(nom)) = 'apragaz') then
        return;
    end if;

    insert into public.ref_fournisseur (nom) values ('Apragaz') returning id into v_id;

    insert into public.ref_fournisseur_prestation (fournisseur_id, type_prestation)
    select distinct v_id, type_prestation from public.ref_tarif_apragaz;

    insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
    select v_id, type_prestation, prix_eur, date_effet from public.ref_tarif_apragaz;
end $$;

-- -----------------------------------------------------------------------------
--  7. Tarification des campagnes de bouteilles — lit EXCLUSIVEMENT les prix du
--     fournisseur « Apragaz » (public.ref_fournisseur_tarif), maintenus depuis
--     la page /fournisseurs. Aucun repli sur l'ancien référentiel : sans prix
--     en vigueur, le résultat est NULL (coût estimé vide), jamais une valeur
--     périmée.
--       - public.campagne_tarif_prestation(type, date) : prix en vigueur ;
--       - public.v_tarif_campagne : historique des prix (estimation côté client).
--     public.ref_tarif_apragaz et public.bouteille_tarif_apragaz()
--     (db/item_bouteille.sql) ne sont plus utilisées par l'application.
-- -----------------------------------------------------------------------------

create or replace function public.campagne_tarif_prestation(
    p_type_prestation text, p_a_la_date date default current_date)
returns numeric
language sql
stable
set search_path = ''
as $$
    select t.prix_eur
    from public.ref_fournisseur_tarif t
    join public.ref_fournisseur f on f.id = t.fournisseur_id
    where lower(btrim(f.nom)) = 'apragaz'
      and t.type_prestation = p_type_prestation
      and t.date_effet <= p_a_la_date
    order by t.date_effet desc
    limit 1
$$;

comment on function public.campagne_tarif_prestation(text, date) is
    'Prix (EUR) en vigueur à p_a_la_date pour une prestation de campagne : prix du fournisseur « Apragaz » (ref_fournisseur_tarif). NULL si aucun prix n''est effectif.';

create or replace view public.v_tarif_campagne
with (security_invoker = true) as
    select t.id, t.type_prestation, t.prix_eur, t.date_effet
    from public.ref_fournisseur_tarif t
    join public.ref_fournisseur f on f.id = t.fournisseur_id
    where lower(btrim(f.nom)) = 'apragaz';

comment on view public.v_tarif_campagne is
    'Historique des prix du fournisseur « Apragaz » utilisé pour chiffrer les campagnes (type_prestation, prix_eur, date_effet). Lecture soumise aux droits de l''appelant (security_invoker).';

grant select on public.v_tarif_campagne to authenticated;

commit;
