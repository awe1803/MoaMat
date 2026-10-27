-- =============================================================================
--  MoaMat — Catalogue de prestations par fournisseur (remplace le référentiel
--  tarifaire Apragaz figé : public.ref_tarif_apragaz + type_prestation codé en
--  dur 'rr' / 'hydraulique_huile' / 'hydraulique_eau').
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/permissions.sql, db/audit.sql, db/item_bouteille.sql et
--  db/fournisseur.sql (public.ref_fournisseur : coordonnées, archivage), et
--  AVANT db/campagne.sql. Ré-exécutable sans erreur.
--
--  Les fournisseurs eux-mêmes (nom, contact, téléphone, email, site web,
--  adresse, archivage) restent dans public.ref_fournisseur (db/fournisseur.sql,
--  écran /fournisseurs) : ce fichier n'y touche pas, il y rattache le
--  catalogue de PRESTATIONS.
--
--  Ancien modèle : l'ancien catalogue à 3 prestations figées de
--  db/fournisseur.sql (ref_fournisseur_prestation / ref_fournisseur_tarif /
--  campagne_tarif_prestation) et l'ancien public.ref_tarif_apragaz sont
--  REPRIS ICI (section 10) puis supprimés par
--  db/migration_prestations_fournisseur.sql.
--
--  Base EXISTANTE (campagnes déjà saisies avec l'ancien modèle) : exécuter
--  ensuite db/migration_prestations_fournisseur.sql, PUIS (ré-)exécuter
--  db/campagne.sql. Installation neuve : ce fichier puis db/campagne.sql
--  suffisent (la migration est alors un no-op).
--
--  Modèle :
--    - public.fournisseur_prestation : une prestation d'un fournisseur. Soit
--      une prestation de contrôle de bouteille (controle_cylindre renseigné :
--      'optique' alimente le compteur optique, 'hydraulique' le compteur
--      hydraulique), soit une prestation libre (controle_cylindre NULL),
--      utilisable pour tout autre service rendu par le fournisseur.
--    - methode_hydraulique ('huile' | 'eau') : OBLIGATOIRE pour une
--      prestation hydraulique, interdite sinon. Huile et eau restent deux
--      prestations DISTINCTES, chacune avec ses tarifs : l'écart de prix
--      (de l'ordre de 13 à 15 € selon les années) est une donnée réelle du
--      référentiel fournisseur, jamais une erreur de saisie à fusionner ni à
--      moyenner. Aucune méthode n'est jamais choisie par défaut (voir
--      public.definir_prestation_campagne_ligne dans db/campagne.sql).
--    - public.fournisseur_prestation_tarif : tarifs OPTIONNELS et DATÉS,
--      append-only (même principe que l'ancien ref_tarif_apragaz) — « modifier »
--      un tarif, c'est en insérer un nouveau avec un date_effet plus récent.
--      Une prestation sans aucune ligne ici n'a simplement pas de tarif.
--
--  Toutes les écritures passent par des fonctions SECURITY DEFINER qui
--  vérifient les permissions fournisseur.* : aucune policy insert/update/
--  delete (même principe que db/campagne.sql).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Prestations d'un fournisseur
-- -----------------------------------------------------------------------------

create table if not exists public.fournisseur_prestation (
    id                  bigint generated always as identity primary key,
    fournisseur_id      bigint not null references public.ref_fournisseur (id),
    -- Code historique ('rr', 'hydraulique_huile', 'hydraulique_eau') conservé
    -- pour les prestations migrées : traçabilité uniquement, jamais saisi par
    -- l'application.
    code                text,
    libelle             text not null check (btrim(libelle) <> ''),
    controle_cylindre   text check (controle_cylindre in ('optique', 'hydraulique')),
    methode_hydraulique text check (methode_hydraulique in ('huile', 'eau')),
    actif               boolean not null default true,
    commentaire         text,
    cree_le             timestamptz not null default now(),
    cree_par            uuid default auth.uid(),
    -- Huile / eau : obligatoire dès que le contrôle est hydraulique, interdit
    -- sinon (jamais de méthode orpheline, jamais d'hydraulique sans méthode).
    -- coalesce : une prestation libre (controle_cylindre NULL) donnerait sinon
    -- NULL = false -> NULL, que CHECK accepte — une méthode orpheline passerait.
    constraint fournisseur_prestation_methode_coherente
        check (coalesce(controle_cylindre = 'hydraulique', false) = (methode_hydraulique is not null))
);

comment on table public.fournisseur_prestation is
    'Catalogue des prestations d''un fournisseur. controle_cylindre renseigné = prestation de contrôle de bouteille (utilisable en campagne) ; NULL = prestation libre. methode_hydraulique (huile/eau) obligatoire si hydraulique. Écritures via public.enregistrer_prestation_fournisseur / supprimer_prestation_fournisseur.';

create unique index if not exists ux_fournisseur_prestation_libelle
    on public.fournisseur_prestation (fournisseur_id, lower(btrim(libelle)));

create unique index if not exists ux_fournisseur_prestation_code
    on public.fournisseur_prestation (fournisseur_id, code)
    where code is not null;

create index if not exists ix_fournisseur_prestation_fournisseur
    on public.fournisseur_prestation (fournisseur_id);

-- -----------------------------------------------------------------------------
--  2. Tarifs datés (optionnels), append-only
-- -----------------------------------------------------------------------------

create table if not exists public.fournisseur_prestation_tarif (
    id            bigint generated always as identity primary key,
    prestation_id bigint not null references public.fournisseur_prestation (id),
    prix_eur      numeric(12,2) not null check (prix_eur >= 0),
    date_effet    date not null,
    cree_le       timestamptz not null default now(),
    cree_par      uuid default auth.uid(),
    unique (prestation_id, date_effet)
);

comment on table public.fournisseur_prestation_tarif is
    'Tarif daté (EUR) d''une prestation de fournisseur. Optionnel : une prestation sans ligne n''a pas de tarif. Append-only (public.definir_tarif_prestation insère une nouvelle ligne datée) ; seule la suppression de la prestation elle-même, via public.supprimer_prestation_fournisseur, purge ses tarifs.';

create index if not exists ix_fournisseur_prestation_tarif_lookup
    on public.fournisseur_prestation_tarif (prestation_id, date_effet desc);

-- Garde append-only : jamais d'UPDATE ; DELETE refusé sauf depuis
-- public.supprimer_prestation_fournisseur (qui pose le drapeau de session
-- ci-dessous, local à sa transaction). Même esprit que
-- public.tg_ref_bouteille_append_only (db/item_bouteille.sql) mais avec cette
-- unique brèche, nécessaire pour supprimer une prestation jamais utilisée.
create or replace function public.tg_fournisseur_prestation_tarif_garde()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if tg_op = 'DELETE'
       and coalesce(current_setting('moamat.suppression_prestation', true), '') = 'on' then
        return old;
    end if;

    raise exception '% est append-only : % interdit. Insérez un nouveau tarif daté plutôt que de modifier l''historique.', tg_table_name, tg_op
        using errcode = 'insufficient_privilege';
end $$;

drop trigger if exists fournisseur_prestation_tarif_no_update on public.fournisseur_prestation_tarif;
create trigger fournisseur_prestation_tarif_no_update
    before update on public.fournisseur_prestation_tarif
    for each row execute function public.tg_fournisseur_prestation_tarif_garde();

drop trigger if exists fournisseur_prestation_tarif_no_delete on public.fournisseur_prestation_tarif;
create trigger fournisseur_prestation_tarif_no_delete
    before delete on public.fournisseur_prestation_tarif
    for each row execute function public.tg_fournisseur_prestation_tarif_garde();

-- -----------------------------------------------------------------------------
--  3. RLS — lecture seule pour les clients
-- -----------------------------------------------------------------------------

alter table public.fournisseur_prestation       enable row level security;
alter table public.fournisseur_prestation_tarif enable row level security;

drop policy if exists fournisseur_prestation_sel on public.fournisseur_prestation;
create policy fournisseur_prestation_sel on public.fournisseur_prestation
    for select to authenticated
    using (public.has_permission('fournisseur.read'));

drop policy if exists fournisseur_prestation_tarif_sel on public.fournisseur_prestation_tarif;
create policy fournisseur_prestation_tarif_sel on public.fournisseur_prestation_tarif
    for select to authenticated
    using (public.has_permission('fournisseur.read'));

grant select on public.fournisseur_prestation       to authenticated;
grant select on public.fournisseur_prestation_tarif to authenticated;

-- -----------------------------------------------------------------------------
--  4. Tarif en vigueur — remplace public.bouteille_tarif_apragaz()
--     (même règle : dernière ligne dont date_effet <= date demandée).
-- -----------------------------------------------------------------------------

create or replace function public.prestation_tarif(
    p_prestation_id bigint, p_a_la_date date default current_date)
returns numeric
language sql
stable
set search_path = ''
as $$
    select prix_eur
    from public.fournisseur_prestation_tarif
    where prestation_id = p_prestation_id
      and date_effet <= p_a_la_date
    order by date_effet desc
    limit 1
$$;

comment on function public.prestation_tarif(bigint, date) is
    'Tarif (EUR) en vigueur à p_a_la_date pour une prestation de fournisseur ; NULL si aucun tarif n''est encore en vigueur (prestation sans tarif).';

revoke execute on function public.prestation_tarif(bigint, date) from public;
grant execute on function public.prestation_tarif(bigint, date) to authenticated;

-- -----------------------------------------------------------------------------
--  5. Résolution d'un fournisseur par son nom (usage interne : campagnes et
--     migration). Insensible à la casse et aux espaces (le nom est unique dans
--     public.ref_fournisseur, db/fournisseur.sql).
-- -----------------------------------------------------------------------------

create or replace function public.resoudre_fournisseur(p_nom text)
returns bigint
language sql
stable
set search_path = ''
as $$
    select id
    from public.ref_fournisseur
    where lower(btrim(nom)) = lower(btrim(p_nom))
    order by id
    limit 1
$$;

comment on function public.resoudre_fournisseur(text) is
    'Id du fournisseur dont le nom (casse/espaces normalisés) correspond, ou NULL. Usage interne (campagnes, migration).';

revoke execute on function public.resoudre_fournisseur(text) from public;
grant execute on function public.resoudre_fournisseur(text) to authenticated;

-- -----------------------------------------------------------------------------
--  7. Prestation — ajout / modification / (dés)activation
-- -----------------------------------------------------------------------------

create or replace function public.enregistrer_prestation_fournisseur(
    p_id                  bigint,
    p_fournisseur_id      bigint,
    p_libelle             text,
    p_controle_cylindre   text,
    p_methode_hydraulique text,
    p_actif               boolean,
    p_commentaire         text)
returns bigint
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_id        bigint := p_id;
    v_libelle   text   := nullif(btrim(coalesce(p_libelle, '')), '');
    v_controle  text   := nullif(btrim(coalesce(p_controle_cylindre, '')), '');
    v_methode   text   := nullif(btrim(coalesce(p_methode_hydraulique, '')), '');
    v_actif     boolean := coalesce(p_actif, true);
    v_commentaire text := nullif(btrim(coalesce(p_commentaire, '')), '');
    v_existant  public.fournisseur_prestation%rowtype;
begin
    if v_id is null then
        if not public.has_permission('fournisseur.create') then
            raise exception 'Droit insuffisant : permission « fournisseur.create » requise.'
                using errcode = 'insufficient_privilege';
        end if;
    elsif not public.has_permission('fournisseur.update') then
        raise exception 'Droit insuffisant : permission « fournisseur.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if v_libelle is null then
        raise exception 'Libellé de la prestation obligatoire.' using errcode = 'check_violation';
    end if;

    if v_controle is not null and v_controle not in ('optique', 'hydraulique') then
        raise exception 'Contrôle de bouteille invalide : % (attendu optique / hydraulique / vide).', v_controle
            using errcode = 'check_violation';
    end if;

    -- Huile / eau : obligatoire pour l'hydraulique, interdit sinon. Jamais de
    -- valeur par défaut — le choix est toujours explicite.
    if v_controle = 'hydraulique' and (v_methode is null or v_methode not in ('huile', 'eau')) then
        raise exception 'Une prestation hydraulique exige la méthode « huile » ou « eau ».'
            using errcode = 'check_violation';
    end if;

    if v_controle is distinct from 'hydraulique' and v_methode is not null then
        raise exception 'La méthode huile/eau ne s''applique qu''à une prestation hydraulique.'
            using errcode = 'check_violation';
    end if;

    if v_id is null then
        if p_fournisseur_id is null or not exists (select 1 from public.ref_fournisseur where id = p_fournisseur_id) then
            raise exception 'Fournisseur introuvable : %.', p_fournisseur_id using errcode = 'no_data_found';
        end if;

        insert into public.fournisseur_prestation
            (fournisseur_id, libelle, controle_cylindre, methode_hydraulique, actif, commentaire)
        values
            (p_fournisseur_id, v_libelle, v_controle, v_methode, v_actif, v_commentaire)
        returning id into v_id;

        perform public.audit_write('fournisseur_prestation.created', 'fournisseur_prestation', v_id::text, null,
            jsonb_build_object('fournisseur_id', p_fournisseur_id, 'libelle', v_libelle,
                'controle_cylindre', v_controle, 'methode_hydraulique', v_methode, 'actif', v_actif),
            '{}'::jsonb);
    else
        select * into v_existant from public.fournisseur_prestation where id = v_id for update;

        if not found then
            raise exception 'Prestation introuvable : %.', v_id using errcode = 'no_data_found';
        end if;

        -- Une prestation déjà utilisée dans une campagne ne peut pas changer de
        -- nature : sinon une ligne déjà envoyée en « hydraulique / huile »
        -- deviendrait rétroactivement « optique » ou « eau ».
        if (v_existant.controle_cylindre is distinct from v_controle
            or v_existant.methode_hydraulique is distinct from v_methode)
           and exists (select 1 from public.campagne_ligne where prestation_id = v_id) then
            raise exception 'Prestation déjà utilisée dans une campagne : son type de contrôle et sa méthode ne peuvent plus changer (désactivez-la et créez-en une nouvelle).'
                using errcode = 'check_violation';
        end if;

        update public.fournisseur_prestation
        set libelle = v_libelle,
            controle_cylindre = v_controle,
            methode_hydraulique = v_methode,
            actif = v_actif,
            commentaire = v_commentaire
        where id = v_id;

        perform public.audit_write('fournisseur_prestation.updated', 'fournisseur_prestation', v_id::text,
            jsonb_build_object('libelle', v_existant.libelle, 'controle_cylindre', v_existant.controle_cylindre,
                'methode_hydraulique', v_existant.methode_hydraulique, 'actif', v_existant.actif),
            jsonb_build_object('libelle', v_libelle, 'controle_cylindre', v_controle,
                'methode_hydraulique', v_methode, 'actif', v_actif),
            '{}'::jsonb);
    end if;

    return v_id;
end $$;

comment on function public.enregistrer_prestation_fournisseur(bigint, bigint, text, text, text, boolean, text) is
    'Crée (p_id NULL, "fournisseur.create") ou modifie ("fournisseur.update") une prestation de fournisseur ; p_fournisseur_id n''est lu qu''à la création. methode_hydraulique (huile/eau) obligatoire si contrôle hydraulique, interdite sinon. Type de contrôle/méthode figés dès que la prestation est utilisée en campagne. Journalisé.';

revoke execute on function public.enregistrer_prestation_fournisseur(bigint, bigint, text, text, text, boolean, text) from public;
grant execute on function public.enregistrer_prestation_fournisseur(bigint, bigint, text, text, text, boolean, text) to authenticated;

-- -----------------------------------------------------------------------------
--  8. Tarif — nouvelle ligne datée (jamais de modification en place)
-- -----------------------------------------------------------------------------

create or replace function public.definir_tarif_prestation(
    p_prestation_id bigint, p_prix_eur numeric, p_date_effet date)
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
    if not public.has_permission('fournisseur.update') then
        raise exception 'Droit insuffisant : permission « fournisseur.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_prix_eur is null or p_prix_eur < 0 then
        raise exception 'Tarif manquant ou négatif.' using errcode = 'check_violation';
    end if;

    if p_date_effet is null then
        raise exception 'Date d''effet obligatoire.' using errcode = 'check_violation';
    end if;

    if not exists (select 1 from public.fournisseur_prestation where id = p_prestation_id) then
        raise exception 'Prestation introuvable : %.', p_prestation_id using errcode = 'no_data_found';
    end if;

    if exists (select 1 from public.fournisseur_prestation_tarif
               where prestation_id = p_prestation_id and date_effet = p_date_effet) then
        raise exception 'Un tarif existe déjà à la date d''effet % : saisissez une nouvelle date d''effet (l''historique n''est jamais modifié).', p_date_effet
            using errcode = 'unique_violation';
    end if;

    insert into public.fournisseur_prestation_tarif (prestation_id, prix_eur, date_effet)
    values (p_prestation_id, round(p_prix_eur, 2), p_date_effet);

    perform public.audit_write('fournisseur_prestation.tarif_defini', 'fournisseur_prestation', p_prestation_id::text, null,
        jsonb_build_object('prix_eur', round(p_prix_eur, 2), 'date_effet', p_date_effet), '{}'::jsonb);
end $$;

comment on function public.definir_tarif_prestation(bigint, numeric, date) is
    'Ajoute un tarif daté à une prestation (append-only : une date d''effet déjà tarifiée est refusée). Exige "fournisseur.update". Journalisé.';

revoke execute on function public.definir_tarif_prestation(bigint, numeric, date) from public;
grant execute on function public.definir_tarif_prestation(bigint, numeric, date) to authenticated;

-- -----------------------------------------------------------------------------
--  9. Suppression d'une prestation — jamais si elle a servi en campagne, ni
--     tant que des tarifs existent (l'historique des prix ne disparaît jamais
--     par effet de bord : supprimer d'abord un tarif saisi par erreur via
--     public.supprimer_tarif_prestation, ou désactiver la prestation).
-- -----------------------------------------------------------------------------

create or replace function public.supprimer_prestation_fournisseur(p_id bigint)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_existant public.fournisseur_prestation%rowtype;
begin
    if not public.has_permission('fournisseur.delete') then
        raise exception 'Droit insuffisant : permission « fournisseur.delete » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    select * into v_existant from public.fournisseur_prestation where id = p_id for update;

    if not found then
        raise exception 'Prestation introuvable : %.', p_id using errcode = 'no_data_found';
    end if;

    if exists (select 1 from public.campagne_ligne where prestation_id = p_id) then
        raise exception 'Prestation utilisée dans une campagne : suppression refusée (désactivez-la à la place).'
            using errcode = 'check_violation';
    end if;

    if exists (select 1 from public.fournisseur_prestation_tarif where prestation_id = p_id) then
        raise exception 'Des tarifs existent pour cette prestation : suppression refusée (supprimez d''abord les tarifs saisis par erreur, ou désactivez la prestation).'
            using errcode = 'check_violation';
    end if;

    delete from public.fournisseur_prestation where id = p_id;

    perform public.audit_write('fournisseur_prestation.deleted', 'fournisseur_prestation', p_id::text,
        jsonb_build_object('fournisseur_id', v_existant.fournisseur_id, 'libelle', v_existant.libelle,
            'controle_cylindre', v_existant.controle_cylindre, 'methode_hydraulique', v_existant.methode_hydraulique),
        null, '{}'::jsonb);
end $$;

comment on function public.supprimer_prestation_fournisseur(bigint) is
    'Supprime une prestation SANS tarif. Exige "fournisseur.delete". Refusée si la prestation est référencée par une ligne de campagne (la désactiver à la place) ou si des tarifs existent (historique protégé). Journalisé.';

revoke execute on function public.supprimer_prestation_fournisseur(bigint) from public;
grant execute on function public.supprimer_prestation_fournisseur(bigint) to authenticated;

-- -----------------------------------------------------------------------------
--  9ter. Suppression d'un tarif saisi par erreur — réservée à
--        fournisseur.delete (admin+), comme dans l'ancien écran Fournisseurs.
--        Les coûts estimés déjà enregistrés sur des lignes de campagne ne sont
--        jamais recalculés.
-- -----------------------------------------------------------------------------

create or replace function public.supprimer_tarif_prestation(p_tarif_id bigint)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_existant public.fournisseur_prestation_tarif%rowtype;
begin
    if not public.has_permission('fournisseur.delete') then
        raise exception 'Droit insuffisant : permission « fournisseur.delete » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    select * into v_existant from public.fournisseur_prestation_tarif where id = p_tarif_id for update;

    if not found then
        raise exception 'Tarif introuvable : %.', p_tarif_id using errcode = 'no_data_found';
    end if;

    perform set_config('moamat.suppression_prestation', 'on', true);
    delete from public.fournisseur_prestation_tarif where id = p_tarif_id;
    perform set_config('moamat.suppression_prestation', 'off', true);

    perform public.audit_write('fournisseur_prestation.tarif_supprime', 'fournisseur_prestation',
        v_existant.prestation_id::text,
        jsonb_build_object('prix_eur', v_existant.prix_eur, 'date_effet', v_existant.date_effet), null, '{}'::jsonb);
end $$;

comment on function public.supprimer_tarif_prestation(bigint) is
    'Supprime un tarif saisi par erreur. Exige "fournisseur.delete". Journalisé. Aucun coût estimé existant n''est recalculé.';

revoke execute on function public.supprimer_tarif_prestation(bigint) from public;
grant execute on function public.supprimer_tarif_prestation(bigint) to authenticated;

-- -----------------------------------------------------------------------------
--  9quater. Suppression d'un fournisseur — jamais s'il a servi en campagne.
--        Ses prestations (alors forcément inutilisées) et leurs tarifs sont
--        supprimés avec lui (comportement de l'ancien écran Fournisseurs).
-- -----------------------------------------------------------------------------

create or replace function public.supprimer_fournisseur(p_id bigint)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_existant public.ref_fournisseur%rowtype;
begin
    if not public.has_permission('fournisseur.delete') then
        raise exception 'Droit insuffisant : permission « fournisseur.delete » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    select * into v_existant from public.ref_fournisseur where id = p_id for update;

    if not found then
        raise exception 'Fournisseur introuvable : %.', p_id using errcode = 'no_data_found';
    end if;

    if exists (select 1 from public.campagne where fournisseur_id = p_id) then
        raise exception 'Fournisseur utilisé dans une campagne : suppression refusée (archivez-le à la place).'
            using errcode = 'check_violation';
    end if;

    perform set_config('moamat.suppression_prestation', 'on', true);
    delete from public.fournisseur_prestation_tarif
    where prestation_id in (select id from public.fournisseur_prestation where fournisseur_id = p_id);
    perform set_config('moamat.suppression_prestation', 'off', true);

    delete from public.fournisseur_prestation where fournisseur_id = p_id;
    delete from public.ref_fournisseur where id = p_id;

    perform public.audit_write('fournisseur.deleted', 'ref_fournisseur', p_id::text,
        jsonb_build_object('nom', v_existant.nom), null, '{}'::jsonb);
end $$;

comment on function public.supprimer_fournisseur(bigint) is
    'Supprime un fournisseur avec ses prestations et tarifs. Exige "fournisseur.delete". Refusé si une campagne le référence (l''archiver à la place). Journalisé.';

revoke execute on function public.supprimer_fournisseur(bigint) from public;
grant execute on function public.supprimer_fournisseur(bigint) to authenticated;

-- -----------------------------------------------------------------------------
--  10. Amorçage Apragaz et REPRISE de l'ancien modèle — idempotent.
--
--      Les 3 prestations de contrôle de bouteille d'Apragaz (RR, hydraulique
--      huile, hydraulique eau) sont toujours présentes. Les tarifs viennent,
--      par ordre de priorité, de :
--        a. l'ancien catalogue de db/fournisseur.sql (ref_fournisseur_tarif :
--           TOUS les fournisseurs, tarifs saisis depuis l'écran inclus) ;
--        b. à défaut, public.ref_tarif_apragaz (Apragaz seul) ;
--        c. à défaut (installation neuve), les valeurs 2023-2026 ci-dessous,
--           reprises verbatim de ref_tarif_requalification.
--      Jamais d'écrasement : « on conflict do nothing ». db/migration_prestations_fournisseur.sql
--      vérifie ensuite que tout a été repris à l'identique avant de supprimer
--      les anciennes tables.
-- -----------------------------------------------------------------------------

insert into public.ref_fournisseur (nom)
select 'Apragaz'
where public.resoudre_fournisseur('Apragaz') is null;

insert into public.fournisseur_prestation
    (fournisseur_id, code, libelle, controle_cylindre, methode_hydraulique)
select public.resoudre_fournisseur('Apragaz'), v.code, v.libelle, v.controle, v.methode
from (values
    ('rr',                'RR (contrôle optique)', 'optique',      null::text),
    ('hydraulique_huile', 'Hydraulique (huile)',   'hydraulique', 'huile'),
    ('hydraulique_eau',   'Hydraulique (eau)',     'hydraulique', 'eau')
) as v(code, libelle, controle, methode)
where not exists (
    select 1 from public.fournisseur_prestation p
    where p.fournisseur_id = public.resoudre_fournisseur('Apragaz') and p.code = v.code);

do $$
begin
    if to_regclass('public.ref_fournisseur_tarif') is not null
       and to_regclass('public.ref_fournisseur_prestation') is not null then
        -- a. Ancien catalogue : prestations proposées par chaque fournisseur…
        insert into public.fournisseur_prestation
            (fournisseur_id, code, libelle, controle_cylindre, methode_hydraulique)
        select o.fournisseur_id, o.type_prestation, m.libelle, m.controle, m.methode
        from public.ref_fournisseur_prestation o
        join (values
            ('rr',                'RR (contrôle optique)', 'optique',      null::text),
            ('hydraulique_huile', 'Hydraulique (huile)',   'hydraulique', 'huile'),
            ('hydraulique_eau',   'Hydraulique (eau)',     'hydraulique', 'eau')
        ) as m(code, libelle, controle, methode) on m.code = o.type_prestation
        where not exists (
            select 1 from public.fournisseur_prestation p
            where p.fournisseur_id = o.fournisseur_id and p.code = o.type_prestation);

        -- … puis leurs tarifs datés.
        insert into public.fournisseur_prestation_tarif (prestation_id, prix_eur, date_effet)
        select p.id, t.prix_eur, t.date_effet
        from public.ref_fournisseur_tarif t
        join public.fournisseur_prestation p
          on p.fournisseur_id = t.fournisseur_id and p.code = t.type_prestation
        on conflict (prestation_id, date_effet) do nothing;
    elsif to_regclass('public.ref_tarif_apragaz') is not null then
        -- b. Ancien référentiel Apragaz.
        insert into public.fournisseur_prestation_tarif (prestation_id, prix_eur, date_effet)
        select p.id, r.prix_eur, r.date_effet
        from public.ref_tarif_apragaz r
        join public.fournisseur_prestation p
          on p.fournisseur_id = public.resoudre_fournisseur('Apragaz') and p.code = r.type_prestation
        on conflict (prestation_id, date_effet) do nothing;
    else
        -- c. Installation neuve.
        insert into public.fournisseur_prestation_tarif (prestation_id, prix_eur, date_effet)
        select p.id, t.prix, t.date_effet
        from (values
            ('rr',                 19.90, date '2023-01-01'),
            ('hydraulique_eau',    45.92, date '2023-01-01'),
            ('hydraulique_huile',  32.61, date '2023-01-01'),
            ('rr',                 20.50, date '2024-01-01'),
            ('hydraulique_eau',    47.30, date '2024-01-01'),
            ('hydraulique_huile',  33.59, date '2024-01-01'),
            ('rr',                 21.32, date '2025-01-01'),
            ('hydraulique_eau',    49.19, date '2025-01-01'),
            ('hydraulique_huile',  34.93, date '2025-01-01'),
            ('rr',                 21.32, date '2026-01-01'),
            ('hydraulique_eau',    50.45, date '2026-01-01'),
            ('hydraulique_huile',  35.82, date '2026-01-01')
        ) as t(code, prix, date_effet)
        join public.fournisseur_prestation p
          on p.fournisseur_id = public.resoudre_fournisseur('Apragaz') and p.code = t.code
        on conflict (prestation_id, date_effet) do nothing;
    end if;
end $$;

commit;
