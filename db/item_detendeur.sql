-- =============================================================================
--  MoaMat — Moteur métier « Ensembles détendeur » : référentiel de périodicité
--  administrable par marque/modèle, moteur d'échéance (dernière intervention
--  connue, jamais la date d'achat), synchronisation de public.item.date_echeance.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql, db/transform_item.sql, db/rls.sql,
--  db/audit.sql et db/item_etat.sql (a besoin de public.item, public.item_detendeur,
--  public.has_permission). Ré-exécutable sans erreur.
--
--  Ticket : « Backend — moteur métier détendeurs : modèle et échéances
--  d'entretien ». Un ensemble détendeur (public.item_detendeur) reste un seul
--  enregistrement quels que soient ses composants présents (booléens
--  a_premier_etage / a_second_etage / a_octopus / a_inflateur / a_manometre,
--  déjà posés par db/model_item.sql) et ses jusqu'à 3 numéros de série
--  (num_serie_premier_etage / num_serie_second_etage / num_serie_octopus,
--  déjà présents eux aussi, rattachés par composant plutôt que génériques —
--  conforme à Q3.1, pas de sous-item séparé). Rien à changer sur ce plan : le
--  travail de ce fichier porte uniquement sur l'échéance d'entretien.
--
--  Marque/modèle : public.item porte déjà des colonnes génériques marque/modele
--  (db/model_item.sql §1), alimentées pour un détendeur par
--  db/transform_item.sql depuis le miroir (detendeur.marque /
--  detendeur.modele_premier_etage — le 1er étage est le composant qui porte la
--  désignation commerciale de l'ensemble). La périodicité est donc indexée sur
--  (item.marque, item.modele), PAS sur une colonne dupliquée dans
--  item_detendeur — une seule source de vérité pour un champ déjà générique.
--
--  Échéance : calculée depuis public.item_detendeur.date_derniere_intervention
--  (compteur ajouté ci-dessous, §1, jamais depuis public.item.date_acquisition/
--  l'ancien date_achat du miroir — Q3.2). NULL tant qu'aucune intervention
--  n'est connue : public.item.date_echeance reste alors NULL, et
--  Domain.Inventory.InventoryItem.DueStatusOn() (déjà générique) affiche
--  « échéance inconnue » (DueStatus.Unknown) sans qu'aucun calcul ne soit
--  jamais tenté sur une base fausse.
--
--  Historique legacy — REPRIS (mise à jour, ticket « reprise détendeurs ») :
--  l'audit laissé en suspens ci-dessus a été fait. public.detendeur_intervention
--  porte 115 lignes (pas 402 — ancien commentaire erroné), et
--  detendeur_id y référence TOUJOURS public.detendeur, jamais
--  detendeur_sortie_inventaire : les 83 valeurs distinctes existent toutes
--  dans detendeur.id, espaces d'id disjoints entre les deux tables miroir
--  (vérifié, même garantie que public.bouteille_requalification). La
--  résolution (origine_table = 'detendeur', origine_id = detendeur_id) est
--  donc sans ambiguïté. Voir db/detendeur_evenement.sql pour la reprise
--  elle-même (et son rattachement de date_derniere_intervention) et pour
--  l'enregistrement des nouvelles interventions, qui porte désormais aussi
--  les 5 points de contrôle obligatoires (ticket « reprise détendeurs »).
--
--  Ce fichier pose :
--    1. public.item_detendeur.date_derniere_intervention — compteur ajouté.
--    2. public.ref_periodicite_detendeur — référentiel administrable et DATÉ
--       (append-only, comme public.ref_periodicite_bouteille), clé
--       (marque, modèle), comparaison insensible à la casse/aux espaces (texte
--       libre repris du miroir, pas un enum fermé comme bouteille.famille).
--    3. public.detendeur_echeance() — moteur : dernière intervention +
--       périodicité en vigueur à cette date (défaut 12 mois tant qu'aucun
--       profil marque/modèle n'est paramétré) ; NULL seulement si la dernière
--       intervention elle-même est inconnue.
--    4. public.v_item_detendeur — vue de lecture (échéance calculée).
--    5. Synchronisation de public.item.date_echeance : depuis item_detendeur
--       (changement de date_derniere_intervention) ET depuis item lui-même
--       (changement de marque/modele — ces colonnes vivent sur item, pas sur
--       item_detendeur, cf. note marque/modèle ci-dessus).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Compteur — ajouté à public.item_detendeur. Nullable : aucune valeur par
--     défaut ni reprise automatique (cf. note historique legacy ci-dessus).
-- -----------------------------------------------------------------------------

alter table public.item_detendeur
    add column if not exists date_derniere_intervention date;

comment on column public.item_detendeur.date_derniere_intervention is
    'Date de la dernière intervention d''entretien CONNUE (jamais la date d''achat, Q3.2). NULL = aucune intervention connue -> échéance explicitement inconnue (public.detendeur_echeance). Mis à jour uniquement par public.enregistrer_intervention_detendeur() (db/detendeur_evenement.sql), jamais en arrière.';

-- -----------------------------------------------------------------------------
--  2. Référentiel administrable et DATÉ — périodicité par (marque, modèle).
--     Append-only comme public.ref_periodicite_bouteille (db/item_bouteille.sql
--     §4bis) : « modifier » une valeur, c'est insérer une nouvelle ligne avec
--     un date_effet plus récent. Comparaison insensible à la casse et aux
--     espaces superflus (public.detendeur_periodicite_mois, §3) : marque/modèle
--     sont du texte libre repris du miroir Access, pas un enum fermé comme
--     bouteille.famille/matiere — une saisie imparfaite ('Scubapro' vs
--     ' scubapro ') ne doit pas silencieusement faire échouer une
--     correspondance par ailleurs correcte. Absence de ligne pour un couple
--     (marque, modèle) = périodicité pas encore paramétrée pour ce profil :
--     NULL, jamais une valeur par défaut inventée.
-- -----------------------------------------------------------------------------

create table if not exists public.ref_periodicite_detendeur (
    id               bigint generated always as identity primary key,
    marque           text not null check (btrim(marque) <> ''),
    modele           text not null check (btrim(modele) <> ''),
    periodicite_mois integer not null check (periodicite_mois > 0),
    date_effet       date not null default current_date,
    cree_le          timestamptz not null default now(),
    cree_par         uuid default auth.uid(),
    unique (marque, modele, date_effet)
);

comment on table public.ref_periodicite_detendeur is
    'Référentiel administrable et daté : périodicité d''entretien (mois) par (marque, modèle) d''ensemble détendeur. Append-only — voir public.tg_ref_bouteille_append_only (générique, réutilisé tel quel). Aucune valeur initiale : à paramétrer par le gestionnaire du matériel, profil par profil.';

create index if not exists ix_ref_periodicite_detendeur_lookup
    on public.ref_periodicite_detendeur (lower(btrim(marque)), lower(btrim(modele)), date_effet desc);

-- Résolution « en vigueur » à une date donnée — même principe que
-- public.bouteille_periodicite_mois (db/item_bouteille.sql §3) : fondée
-- uniquement sur date_effet, jamais sur l'ordre d'insertion.
--
-- Périodicité par défaut (12 mois) : contrairement aux bouteilles (6 profils
-- réglementaires connus dès le départ), le référentiel détendeur est alimenté
-- profil par profil au fil de l'eau par le gestionnaire. En attendant qu'une
-- marque/modèle y soit déclarée, une échéance « inconnue » bloquait le suivi
-- (aucune relance d'entretien possible) — moins sûr qu'une échéance par
-- défaut prudente d'un an, à affiner dès qu'un profil spécifique est
-- paramétré (qui prend alors le dessus, cf. le COALESCE ci-dessous).
create or replace function public.detendeur_periodicite_mois(
    p_marque text, p_modele text, p_a_la_date date default current_date)
returns integer
language sql
stable
set search_path = ''
as $$
    select coalesce(
        (select periodicite_mois
         from public.ref_periodicite_detendeur
         where lower(btrim(marque)) = lower(btrim(p_marque))
           and lower(btrim(modele)) = lower(btrim(p_modele))
           and date_effet <= p_a_la_date
         order by date_effet desc
         limit 1),
        12)
    where p_marque is not null and p_modele is not null
$$;

comment on function public.detendeur_periodicite_mois(text, text, date) is
    'Périodicité (mois) en vigueur à p_a_la_date pour (marque, modèle), comparaison insensible à la casse/aux espaces. Défaut 12 mois tant qu''aucune ligne de référentiel ne correspond encore à ce profil (remplacé dès qu''un profil spécifique est paramétré). NULL uniquement si marque ou modèle manquant.';

-- Append-only : réutilise le trigger générique posé par db/item_bouteille.sql
-- (public.tg_ref_bouteille_append_only ne référence que tg_table_name/tg_op,
-- donc déjà table-agnostique malgré son nom — pas de duplication de logique).
drop trigger if exists ref_periodicite_detendeur_no_update on public.ref_periodicite_detendeur;
create trigger ref_periodicite_detendeur_no_update
    before update on public.ref_periodicite_detendeur
    for each row execute function public.tg_ref_bouteille_append_only();

drop trigger if exists ref_periodicite_detendeur_no_delete on public.ref_periodicite_detendeur;
create trigger ref_periodicite_detendeur_no_delete
    before delete on public.ref_periodicite_detendeur
    for each row execute function public.tg_ref_bouteille_append_only();

alter table public.ref_periodicite_detendeur enable row level security;

drop policy if exists moamat_dev_all on public.ref_periodicite_detendeur;

-- Lecture : tout authentifié (referentiel.read). Écriture : INSERT seul
-- (referentiel.create, admin+) — même règle que ref_periodicite_bouteille.
drop policy if exists ref_periodicite_detendeur_sel on public.ref_periodicite_detendeur;
create policy ref_periodicite_detendeur_sel on public.ref_periodicite_detendeur
    for select to authenticated
    using (public.has_permission('referentiel.read'));

drop policy if exists ref_periodicite_detendeur_ins on public.ref_periodicite_detendeur;
create policy ref_periodicite_detendeur_ins on public.ref_periodicite_detendeur
    for insert to authenticated
    with check (public.has_permission('referentiel.create'));

-- -----------------------------------------------------------------------------
--  3. Moteur de calcul — un seul compteur (pas d'alternance optique/hydraulique
--     comme pour les bouteilles). Même concaténation textuelle que
--     public.bouteille_echeance (db/item_bouteille.sql §5) : une périodicité
--     NULL donne une échéance NULL, jamais une erreur ni une valeur par défaut.
-- -----------------------------------------------------------------------------

create or replace function public.detendeur_echeance(
    p_marque text, p_modele text, p_derniere_intervention date)
returns date
language sql
stable
set search_path = ''
as $$
    select (p_derniere_intervention
         + (public.detendeur_periodicite_mois(p_marque, p_modele, p_derniere_intervention) || ' months')::interval)::date
$$;

comment on function public.detendeur_echeance(text, text, date) is
    'Échéance d''entretien = dernière intervention connue + périodicité (marque, modèle) en vigueur à cette date, avec défaut 12 mois tant qu''aucun profil spécifique n''est paramétré (public.detendeur_periodicite_mois). NULL seulement si la dernière intervention est inconnue (jamais une date calculée sur la date d''achat, Q3.2).';

-- -----------------------------------------------------------------------------
--  4. Vue de lecture — public.v_item_detendeur (security_invoker, même famille
--     que public.v_item_bouteille). marque/modele lus sur public.item : voir
--     la note en tête de fichier.
-- -----------------------------------------------------------------------------

drop view if exists public.v_item_detendeur;
create view public.v_item_detendeur
    with (security_invoker = true) as
    select
        d.item_id,
        i.marque,
        i.modele,
        d.date_derniere_intervention,
        public.detendeur_echeance(i.marque, i.modele, d.date_derniere_intervention) as echeance
    from public.item_detendeur d
    join public.item i on i.id = d.item_id;

comment on view public.v_item_detendeur is
    'Lecture calculée de l''échéance d''entretien d''un ensemble détendeur. security_invoker : RLS de item_detendeur/item appliquée.';

grant select on public.v_item_detendeur to authenticated;

-- -----------------------------------------------------------------------------
--  5. Synchronisation de public.item.date_echeance.
--     Deux déclencheurs, car l'échéance dépend de colonnes réparties sur DEUX
--     tables (contrairement aux bouteilles, où tout vit sur item_bouteille) :
--       a) item_detendeur.date_derniere_intervention change -> relire
--          marque/modele sur item et recalculer (comme public.item_bouteille,
--          AFTER + UPDATE explicite : la ligne modifiée n'est pas celle qu'il
--          faut écrire).
--       b) item.marque / item.modele change -> recalculer SI cet item est un
--          détendeur (BEFORE UPDATE : on modifie directement NEW.date_echeance,
--          pas d'UPDATE imbriqué sur la même ligne — pas de risque de
--          récursion, contrairement à un déclencheur AFTER qui réécrirait
--          public.item depuis un déclencheur posé sur public.item lui-même).
--     Ni l'un ni l'autre ne touche statut_code : ne déclenche donc jamais
--     public.tg_item_valider_transition_statut (db/item_etat.sql).
-- -----------------------------------------------------------------------------

create or replace function public.tg_item_detendeur_sync_echeance()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_echeance date;
begin
    select v.echeance into v_echeance from public.v_item_detendeur v where v.item_id = new.item_id;

    update public.item
    set date_echeance = v_echeance
    where id = new.item_id
      and date_echeance is distinct from v_echeance;

    return null;
end $$;

comment on function public.tg_item_detendeur_sync_echeance() is
    'Recopie public.v_item_detendeur.echeance dans public.item.date_echeance à chaque changement de public.item_detendeur.date_derniere_intervention.';

drop trigger if exists item_detendeur_sync_echeance on public.item_detendeur;
create trigger item_detendeur_sync_echeance
    after insert or update of date_derniere_intervention
    on public.item_detendeur
    for each row execute function public.tg_item_detendeur_sync_echeance();

-- Si la ligne item_detendeur disparaît, ce trigger ne se déclenche plus :
-- sans ceci, public.item.date_echeance garderait la dernière valeur calculée
-- (même raisonnement que public.tg_item_bouteille_clear_echeance).
create or replace function public.tg_item_detendeur_clear_echeance()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    update public.item
    set date_echeance = null
    where id = old.item_id
      and date_echeance is not null;

    return null;
end $$;

comment on function public.tg_item_detendeur_clear_echeance() is
    'Remet public.item.date_echeance à NULL quand sa ligne item_detendeur est supprimée.';

drop trigger if exists item_detendeur_clear_echeance on public.item_detendeur;
create trigger item_detendeur_clear_echeance
    after delete on public.item_detendeur
    for each row execute function public.tg_item_detendeur_clear_echeance();

-- marque/modele vivent sur public.item (générique à toute famille) : un
-- changement n'affecte l'échéance que pour un item qui EST un détendeur
-- (présence d'une ligne item_detendeur), jamais pour les autres familles.
create or replace function public.tg_item_sync_date_echeance_detendeur()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_derniere_intervention date;
begin
    select d.date_derniere_intervention into v_derniere_intervention
    from public.item_detendeur d
    where d.item_id = new.id;

    if found then
        new.date_echeance := public.detendeur_echeance(new.marque, new.modele, v_derniere_intervention);
    end if;

    return new;
end $$;

comment on function public.tg_item_sync_date_echeance_detendeur() is
    'Recalcule public.item.date_echeance quand marque/modele changent sur un item qui est un ensemble détendeur (ces colonnes génériques vivent sur item, pas sur item_detendeur — cf. en-tête de fichier). BEFORE UPDATE : écrit directement NEW, aucun UPDATE imbriqué sur public.item.';

drop trigger if exists item_sync_date_echeance_detendeur on public.item;
create trigger item_sync_date_echeance_detendeur
    before update of marque, modele on public.item
    for each row execute function public.tg_item_sync_date_echeance_detendeur();

-- -----------------------------------------------------------------------------
--  6. Resynchronisation — un rejeu de ce fichier (ex. après un changement de
--     règle dans public.detendeur_periodicite_mois, comme l'ajout du défaut
--     12 mois) ne redéclenche pas les triggers ci-dessus pour les lignes déjà
--     en base : leur public.item.date_echeance reste figée sur l'ancienne
--     valeur calculée tant qu'aucun UPDATE ne survient. Recalcule donc
--     explicitement, pour tout ensemble détendeur, depuis
--     public.v_item_detendeur (toujours à jour). Ré-exécutable, no-op si déjà
--     synchronisé (clause WHERE ... IS DISTINCT FROM).
-- -----------------------------------------------------------------------------

update public.item i
set date_echeance = v.echeance
from public.v_item_detendeur v
where v.item_id = i.id
  and i.date_echeance is distinct from v.echeance;

commit;
