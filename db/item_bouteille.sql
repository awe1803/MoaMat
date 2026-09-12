-- =============================================================================
--  MoaMat — Moteur métier « Bouteilles » : classification, référentiels
--  administrables (périodicité, tarif Apragaz), moteur d'échéances à deux
--  compteurs indépendants, bascule automatique en « Hors validité ».
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql, db/transform_item.sql, db/rls.sql,
--  db/audit.sql et db/item_etat.sql (a besoin de public.item, public.ref_statut,
--  public.tg_item_valider_transition_statut et de public.has_permission).
--  Ré-exécutable sans erreur.
--
--  Fusion des tickets « modèle bouteille » + « référentiel réglementaire » +
--  « référentiel tarifaire » + « moteur d'échéances ». Les valeurs initiales
--  ne sont pas inventées : le miroir Access contient déjà les 6 profils
--  réglementaires (public.ref_regle_requalification, 6 lignes : Tampons,
--  Deco O², O² Secourisme, Plongée Carbonne, Plongée ALU, Plongée ACIER) et
--  les tarifs Apragaz 2023-2026 (public.ref_tarif_requalification). Ce fichier
--  pose la couche normalisée et administrable par-dessus, sur le même principe
--  que db/model_item.sql pour l'item générique — le miroir ne change pas.
--
--  Ce fichier pose :
--    1. Colonnes de classification + compteurs sur public.item_bouteille.
--    2. public.bouteille_type_referentiel() — résolveur (famille, matière)
--       -> l'un des 6 codes réglementaires.
--    3. public.ref_periodicite_bouteille — référentiel réglementaire
--       administrable et DATÉ (append-only, comme public.item_transition).
--    4. public.ref_tarif_apragaz — référentiel tarifaire, même mécanique.
--    5. public.bouteille_echeance() — moteur : un compteur = une échéance,
--       aucune alternance codée en dur entre optique et hydraulique.
--    6. public.v_item_bouteille — vue de lecture (échéances calculées).
--    7. Synchronisation de public.item.date_echeance depuis l'échéance la
--       plus proche des deux compteurs.
--    8. public.item_bouteille_appliquer_hors_validite() — bascule
--       automatique (R2.3), planifiée via pg_cron si disponible.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Classification + compteurs — colonnes ajoutées à public.item_bouteille.
--     Nullable : pas de contrainte NOT NULL rétroactive sur les lignes déjà
--     reprises depuis le miroir (db/transform_item.sql ne les renseigne pas).
--     « matiere » n'est pertinente que pour famille = 'plongee' (les trois
--     autres familles ont leur propre périodicité, indépendante de la
--     matière — cf. §3) ; non contrainte techniquement pour l'instant, comme
--     le rappelle db/MODELE.md §3 pour « famille » sur public.item.
-- -----------------------------------------------------------------------------

alter table public.item_bouteille
    add column if not exists famille text
        check (famille is null or famille in ('plongee', 'deco_o2', 'o2_secourisme', 'bloc_tampon')),
    add column if not exists matiere text
        check (matiere is null or matiere in ('acier', 'alu', 'carbone')),
    add column if not exists etat_robinetterie text,
    add column if not exists date_dernier_controle_optique date,
    add column if not exists date_dernier_controle_hydraulique date;

comment on column public.item_bouteille.famille is
    'Famille d''usage : plongee / deco_o2 / o2_secourisme / bloc_tampon. Champ simple, pas d''entité séparée pour la robinetterie (Q2.1).';
comment on column public.item_bouteille.matiere is
    'acier / alu / carbone — pertinent seulement pour famille = ''plongee'' (cf. public.bouteille_type_referentiel).';
comment on column public.item_bouteille.etat_robinetterie is
    'État de la robinetterie en texte libre — volontairement PAS une entité séparée (Q2.1).';
comment on column public.item_bouteille.date_dernier_controle_optique is
    'Date du dernier contrôle optique (compteur indépendant du contrôle hydraulique).';
comment on column public.item_bouteille.date_dernier_controle_hydraulique is
    'Date du dernier contrôle hydraulique (compteur indépendant du contrôle optique).';

-- -----------------------------------------------------------------------------
--  2. Résolveur de type réglementaire
--     Les 6 profils testés (critère d'acceptation) correspondent exactement
--     aux 6 lignes de public.ref_regle_requalification : Tampons, Deco O²,
--     O² Secourisme, Plongée Carbonne, Plongée ALU, Plongée ACIER. « plongee »
--     se décline par matière (acier/alu/carbone) car chacune a sa propre
--     périodicité ; les trois autres familles ont une périodicité unique,
--     indépendante de la matière.
-- -----------------------------------------------------------------------------

create or replace function public.bouteille_type_referentiel(p_famille text, p_matiere text)
returns text
language sql
immutable
set search_path = ''
as $$
    select case
        when p_famille = 'plongee' then 'plongee_' || p_matiere
        when p_famille in ('deco_o2', 'o2_secourisme', 'bloc_tampon') then p_famille
        else null
    end
$$;

comment on function public.bouteille_type_referentiel(text, text) is
    'Résout (famille, matière) -> l''un des 6 codes réglementaires (plongee_acier/plongee_alu/plongee_carbone/deco_o2/o2_secourisme/bloc_tampon). NULL si famille ou matière manquante/inconnue.';

-- -----------------------------------------------------------------------------
--  3. Référentiel réglementaire — périodicité par (type bouteille × type de
--     contrôle), ADMINISTRABLE et DATÉ. Append-only comme public.item_transition
--     (db/item_etat.sql) : « modifier » une valeur, c'est insérer une nouvelle
--     ligne avec un date_effet plus récent — rien n'est jamais écrasé, tout
--     l'historique des valeurs reste consultable.
--     Absence de ligne pour un couple (type, contrôle) = ce contrôle ne
--     s'applique pas à ce type (ex. carbone n'a pas de ligne optique) : le
--     moteur (§5) ne code AUCUNE alternance en dur entre les deux contrôles,
--     il se contente d'interroger ce référentiel pour chaque compteur.
-- -----------------------------------------------------------------------------

create table if not exists public.ref_periodicite_bouteille (
    id               bigint generated always as identity primary key,
    type_bouteille   text not null
                     check (type_bouteille in ('plongee_acier', 'plongee_alu', 'plongee_carbone',
                                                'deco_o2', 'o2_secourisme', 'bloc_tampon')),
    type_controle    text not null check (type_controle in ('optique', 'hydraulique')),
    periodicite_mois integer not null check (periodicite_mois > 0),
    date_effet       date not null default current_date,
    cree_le          timestamptz not null default now(),
    cree_par         uuid default auth.uid(),
    unique (type_bouteille, type_controle, date_effet)
);

comment on table public.ref_periodicite_bouteille is
    'Référentiel réglementaire administrable et daté : périodicité (mois) par (type bouteille, type de contrôle). Append-only — voir public.tg_ref_bouteille_append_only.';

create index if not exists ix_ref_periodicite_bouteille_lookup
    on public.ref_periodicite_bouteille (type_bouteille, type_controle, date_effet desc);

-- Valeurs initiales — reprises de public.ref_regle_requalification (6 lignes),
-- converties en mois. « JAMAIS » dans le miroir = aucune ligne ici.
insert into public.ref_periodicite_bouteille (type_bouteille, type_controle, periodicite_mois, date_effet) values
    ('plongee_acier',   'optique',     30,  date '2020-01-01'),
    ('plongee_acier',   'hydraulique', 60,  date '2020-01-01'),
    ('plongee_alu',     'optique',     30,  date '2020-01-01'),
    ('plongee_alu',     'hydraulique', 60,  date '2020-01-01'),
    ('plongee_carbone', 'hydraulique', 36,  date '2020-01-01'),
    ('deco_o2',         'optique',     30,  date '2020-01-01'),
    ('deco_o2',         'hydraulique', 60,  date '2020-01-01'),
    ('o2_secourisme',   'optique',     60,  date '2020-01-01'),
    ('bloc_tampon',     'hydraulique', 120, date '2020-01-01')
on conflict (type_bouteille, type_controle, date_effet) do nothing;

-- Consultation pratique de la valeur EN VIGUEUR à une date donnée (celle du
-- dernier contrôle par défaut de bouteille_echeance() ci-dessous — pas
-- forcément la date du jour, cohérent avec l'historisation).
-- Résolution "en vigueur" fondée UNIQUEMENT sur date_effet, pas sur l'ordre
-- d'insertion : un admin peut donc insérer une ligne d'édition d'historique
-- avec un date_effet passé (append-only ≠ forward-dating uniquement) sans
-- perturber la valeur retenue pour les contrôles à venir.
create or replace function public.bouteille_periodicite_mois(
    p_type_bouteille text, p_type_controle text, p_a_la_date date default current_date)
returns integer
language sql
stable
set search_path = ''
as $$
    select periodicite_mois
    from public.ref_periodicite_bouteille
    where type_bouteille = p_type_bouteille
      and type_controle = p_type_controle
      and date_effet <= p_a_la_date
    order by date_effet desc
    limit 1
$$;

comment on function public.bouteille_periodicite_mois(text, text, date) is
    'Périodicité (mois) en vigueur à p_a_la_date pour (type_bouteille, type_controle). NULL si ce contrôle ne s''applique pas à ce type.';

-- -----------------------------------------------------------------------------
--  4. Référentiel tarifaire Apragaz — même mécanique d'historisation.
--     Repris de public.ref_tarif_requalification (RR / hydraulique huile /
--     hydraulique eau, 2023-2026). Huile et eau restent deux lignes DISTINCTES
--     à dessein : l'écart entre les deux (de l'ordre de 13 à 15 € selon les
--     années, cf. les valeurs ci-dessous) est une donnée réelle du référentiel
--     Apragaz, pas une erreur de saisie à corriger — ne jamais les fusionner
--     ni les moyenner.
-- -----------------------------------------------------------------------------

create table if not exists public.ref_tarif_apragaz (
    id              bigint generated always as identity primary key,
    type_prestation text not null check (type_prestation in ('rr', 'hydraulique_huile', 'hydraulique_eau')),
    prix_eur        numeric(12,2) not null check (prix_eur >= 0),
    date_effet      date not null,
    cree_le         timestamptz not null default now(),
    cree_par        uuid default auth.uid(),
    unique (type_prestation, date_effet)
);

comment on table public.ref_tarif_apragaz is
    'Référentiel tarifaire Apragaz administrable et daté : RR (répreuve rétinienne) / hydraulique huile / hydraulique eau. Huile et eau distincts à dessein (écart constaté ~13-15 €, cf. commentaire du fichier). Append-only.';

create index if not exists ix_ref_tarif_apragaz_lookup
    on public.ref_tarif_apragaz (type_prestation, date_effet desc);

-- Valeurs initiales — reprises verbatim de public.ref_tarif_requalification
-- (id 1-12), une ligne par (prestation, année), date_effet = 1er janvier de
-- l'année du tarif.
insert into public.ref_tarif_apragaz (type_prestation, prix_eur, date_effet) values
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
on conflict (type_prestation, date_effet) do nothing;

create or replace function public.bouteille_tarif_apragaz(
    p_type_prestation text, p_a_la_date date default current_date)
returns numeric
language sql
stable
set search_path = ''
as $$
    select prix_eur
    from public.ref_tarif_apragaz
    where type_prestation = p_type_prestation
      and date_effet <= p_a_la_date
    order by date_effet desc
    limit 1
$$;

comment on function public.bouteille_tarif_apragaz(text, date) is
    'Tarif Apragaz (EUR) en vigueur à p_a_la_date pour type_prestation (rr / hydraulique_huile / hydraulique_eau).';

-- -----------------------------------------------------------------------------
--  4bis. Historisation — append-only (même garde que public.item_transition,
--        db/item_etat.sql) : ni UPDATE ni DELETE, y compris pour un rôle
--        admin — « modifier » une valeur, c'est en INSÉRER une nouvelle avec
--        un date_effet plus récent.
-- -----------------------------------------------------------------------------

create or replace function public.tg_ref_bouteille_append_only()
returns trigger
language plpgsql
as $$
begin
    raise exception '% est append-only : % interdit. Insérez une nouvelle ligne datée plutôt que de modifier l''historique.', tg_table_name, tg_op
        using errcode = 'insufficient_privilege';
end $$;

drop trigger if exists ref_periodicite_bouteille_no_update on public.ref_periodicite_bouteille;
create trigger ref_periodicite_bouteille_no_update
    before update on public.ref_periodicite_bouteille
    for each row execute function public.tg_ref_bouteille_append_only();

drop trigger if exists ref_periodicite_bouteille_no_delete on public.ref_periodicite_bouteille;
create trigger ref_periodicite_bouteille_no_delete
    before delete on public.ref_periodicite_bouteille
    for each row execute function public.tg_ref_bouteille_append_only();

drop trigger if exists ref_tarif_apragaz_no_update on public.ref_tarif_apragaz;
create trigger ref_tarif_apragaz_no_update
    before update on public.ref_tarif_apragaz
    for each row execute function public.tg_ref_bouteille_append_only();

drop trigger if exists ref_tarif_apragaz_no_delete on public.ref_tarif_apragaz;
create trigger ref_tarif_apragaz_no_delete
    before delete on public.ref_tarif_apragaz
    for each row execute function public.tg_ref_bouteille_append_only();

alter table public.ref_periodicite_bouteille enable row level security;
alter table public.ref_tarif_apragaz         enable row level security;

drop policy if exists moamat_dev_all on public.ref_periodicite_bouteille;
drop policy if exists moamat_dev_all on public.ref_tarif_apragaz;

-- Lecture : tout authentifié (referentiel.read, comme ref_statut / lieu_*).
-- Écriture : INSERT seul (referentiel.create, admin+) — pas d'UPDATE/DELETE :
-- l'historisation est garantie par les triggers ci-dessus, pas seulement par
-- l'absence de policy.
drop policy if exists ref_periodicite_bouteille_sel on public.ref_periodicite_bouteille;
create policy ref_periodicite_bouteille_sel on public.ref_periodicite_bouteille
    for select to authenticated
    using (public.has_permission('referentiel.read'));

drop policy if exists ref_periodicite_bouteille_ins on public.ref_periodicite_bouteille;
create policy ref_periodicite_bouteille_ins on public.ref_periodicite_bouteille
    for insert to authenticated
    with check (public.has_permission('referentiel.create'));

drop policy if exists ref_tarif_apragaz_sel on public.ref_tarif_apragaz;
create policy ref_tarif_apragaz_sel on public.ref_tarif_apragaz
    for select to authenticated
    using (public.has_permission('referentiel.read'));

drop policy if exists ref_tarif_apragaz_ins on public.ref_tarif_apragaz;
create policy ref_tarif_apragaz_ins on public.ref_tarif_apragaz
    for insert to authenticated
    with check (public.has_permission('referentiel.create'));

-- -----------------------------------------------------------------------------
--  5. Moteur de calcul — un compteur = une échéance. Aucune alternance codée
--     en dur entre optique et hydraulique : chaque appel interroge le
--     référentiel pour SON type de contrôle uniquement. La périodicité
--     appliquée est celle en vigueur à la date du dernier contrôle (et non
--     celle du jour), cohérent avec l'historisation du référentiel.
-- -----------------------------------------------------------------------------

create or replace function public.bouteille_echeance(
    p_type_bouteille text, p_type_controle text, p_dernier_controle date)
returns date
language sql
stable
set search_path = ''
as $$
    -- Concaténation textuelle (opérateur || strict) plutôt que make_interval() :
    -- une périodicité NULL (contrôle non applicable) doit donner une échéance
    -- NULL, jamais lever d'erreur ni retomber sur une valeur par défaut.
    select (p_dernier_controle
         + (public.bouteille_periodicite_mois(p_type_bouteille, p_type_controle, p_dernier_controle) || ' months')::interval)::date
$$;

comment on function public.bouteille_echeance(text, text, date) is
    'Échéance d''UN compteur = dernier contrôle + périodicité en vigueur à cette date. NULL si p_dernier_controle est NULL ou si ce contrôle ne s''applique pas à ce type (aucune alternance codée en dur entre optique/hydraulique).';

-- -----------------------------------------------------------------------------
--  6. Vue de lecture — public.v_item_bouteille (security_invoker, même
--     famille que public.v_item, db/model_item.sql §8).
-- -----------------------------------------------------------------------------

drop view if exists public.v_item_bouteille;
create view public.v_item_bouteille
    with (security_invoker = true) as
    select
        b.item_id,
        b.famille,
        b.matiere,
        c.type_referentiel,
        b.etat_robinetterie,
        b.date_dernier_controle_optique,
        c.echeance_optique,
        b.date_dernier_controle_hydraulique,
        c.echeance_hydraulique,
        case
            when c.echeance_optique is null then c.echeance_hydraulique
            when c.echeance_hydraulique is null then c.echeance_optique
            else least(c.echeance_optique, c.echeance_hydraulique)
        end as echeance_min
    from public.item_bouteille b
    cross join lateral (
        select
            public.bouteille_type_referentiel(b.famille, b.matiere) as type_referentiel,
            public.bouteille_echeance(
                public.bouteille_type_referentiel(b.famille, b.matiere), 'optique', b.date_dernier_controle_optique
            ) as echeance_optique,
            public.bouteille_echeance(
                public.bouteille_type_referentiel(b.famille, b.matiere), 'hydraulique', b.date_dernier_controle_hydraulique
            ) as echeance_hydraulique
    ) c;

comment on view public.v_item_bouteille is
    'Lecture calculée des échéances bouteille : deux compteurs indépendants (optique/hydraulique) + echeance_min (le plus proche des deux, NULL uniquement si les deux le sont). security_invoker : RLS de item_bouteille appliquée.';

grant select on public.v_item_bouteille to authenticated;

-- -----------------------------------------------------------------------------
--  7. Synchronisation de public.item.date_echeance depuis echeance_min.
--     Ne touche jamais statut_code : ne déclenche donc pas
--     public.tg_item_valider_transition_statut (db/item_etat.sql). SECURITY
--     DEFINER : ce champ dérivé n'est plus une saisie utilisateur, il n'y a
--     donc pas lieu d'exiger la permission item.update pour le maintenir.
-- -----------------------------------------------------------------------------

create or replace function public.tg_item_bouteille_sync_echeance()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_echeance_min date;
begin
    select v.echeance_min into v_echeance_min from public.v_item_bouteille v where v.item_id = new.item_id;

    update public.item
    set date_echeance = v_echeance_min
    where id = new.item_id
      and date_echeance is distinct from v_echeance_min;

    return null;
end $$;

comment on function public.tg_item_bouteille_sync_echeance() is
    'Recopie public.v_item_bouteille.echeance_min dans public.item.date_echeance à chaque changement de classification ou de compteur de contrôle.';

drop trigger if exists item_bouteille_sync_echeance on public.item_bouteille;
create trigger item_bouteille_sync_echeance
    after insert or update of famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique
    on public.item_bouteille
    for each row execute function public.tg_item_bouteille_sync_echeance();

-- -----------------------------------------------------------------------------
--  8. Bascule automatique en « Hors validité » (R2.3) — sans action manuelle.
--     Le statut hors_validite existe déjà dans public.ref_statut
--     (db/model_item.sql §1, non terminal). L'UPDATE ci-dessous passe par le
--     trigger existant public.tg_item_valider_transition_statut
--     (db/item_etat.sql) : motif et date d'effet fournis inline, aucune
--     autorité décisionnaire requise (statut non terminal) — la transition
--     est donc validée ET journalisée (item_transition + audit_log) par le
--     mécanisme existant, sans dupliquer sa logique. SECURITY DEFINER :
--     déclenchée par un job planifié, sans session utilisateur authentifiée.
--
--     Candidats volontairement restreints à en_stock / en_attente_controle /
--     prete : une bouteille déjà en_maintenance ou en_controle est prise en
--     charge par un workflow actif — la bascule automatique ne l'interrompt
--     pas et n'écrase pas ce statut, même si son ancienne échéance est
--     dépassée (l'opérateur sait déjà qu'elle est indisponible, pour la
--     bonne raison affichée).
-- -----------------------------------------------------------------------------

create or replace function public.item_bouteille_appliquer_hors_validite(p_a_la_date date default current_date)
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_count integer;
begin
    with candidats as (
        select i.id,
            case
                when b.echeance_optique is not null and b.echeance_optique < p_a_la_date
                 and (b.echeance_hydraulique is null or b.echeance_hydraulique >= p_a_la_date)
                    then 'optique'
                when b.echeance_hydraulique is not null and b.echeance_hydraulique < p_a_la_date
                 and (b.echeance_optique is null or b.echeance_optique >= p_a_la_date)
                    then 'hydraulique'
                else 'optique et hydraulique'
            end as controle_en_cause
        from public.item i
        join public.v_item_bouteille b on b.item_id = i.id
        where i.actif
          and i.statut_code in ('en_stock', 'en_attente_controle', 'prete')
          and b.echeance_min is not null
          and b.echeance_min < p_a_la_date
    )
    update public.item i
    set statut_code = 'hors_validite',
        statut_motif = format('Échéance de contrôle %s dépassée (bascule automatique)', c.controle_en_cause),
        statut_date_effet = p_a_la_date
    from candidats c
    where i.id = c.id;

    get diagnostics v_count = row_count;
    return v_count;
end $$;

comment on function public.item_bouteille_appliquer_hors_validite(date) is
    'Bascule automatiquement en hors_validite toute bouteille active dont echeance_min est dépassée, SAUF en_maintenance/en_controle (workflow actif non interrompu) et déjà terminale (R2.3). Retourne le nombre de bouteilles basculées. Planifiée par pg_cron (voir DO ci-dessous) ; peut aussi être appelée directement (tests, rattrapage manuel).';

-- SECURITY DEFINER + contourne intégralement la policy RLS item_upd (aucune
-- vérification de item.update) : PostgreSQL accorde EXECUTE à PUBLIC par
-- défaut à la création d'une fonction, ce qui exposerait sinon un moyen, pour
-- N'IMPORTE QUEL compte authentifié (voire anon), de forcer le statut de
-- n'importe quelle bouteille sans posséder la permission item.update — même
-- garde que public.custom_access_token_hook (db/roles.sql). Seul un rôle
-- système (pg_cron, exécuté par le propriétaire de la fonction ; service_role
-- pour un rattrapage manuel côté serveur) peut l'invoquer.
revoke execute on function public.item_bouteille_appliquer_hors_validite(date) from public, authenticated, anon;
grant execute on function public.item_bouteille_appliquer_hors_validite(date) to service_role;

-- Planification quotidienne — no-op si l'extension pg_cron n'est pas
-- disponible OU si le rôle courant n'a pas le droit de la créer
-- (environnement local/CI restreint) : le fichier reste rejouable partout,
-- seule la planification effective dépend de l'hébergeur.
do $$
begin
    if exists (select 1 from pg_available_extensions where name = 'pg_cron') then
        create extension if not exists pg_cron;

        if not exists (select 1 from cron.job where jobname = 'item-bouteille-hors-validite') then
            perform cron.schedule(
                'item-bouteille-hors-validite',
                '0 3 * * *',
                'select public.item_bouteille_appliquer_hors_validite();'
            );
        end if;
    end if;
exception
    when insufficient_privilege then
        raise notice 'pg_cron non planifié (privilège insuffisant pour créer l''extension) : appeler public.item_bouteille_appliquer_hors_validite() manuellement ou via un job externe.';
end $$;

commit;
