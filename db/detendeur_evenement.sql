-- =============================================================================
--  MoaMat — Historique des interventions d'entretien d'un ensemble détendeur :
--  reprise des 115 interventions Access (detendeur_intervention) + saisie
--  bloquante des 5 points de contrôle obligatoires (ticket « reprise
--  détendeurs » — corrige la dérive « NO CTL » constatée : 48 des 115 lignes
--  historiques portent NO CTL sur les 5 points simultanément, signe d'un
--  contrôle enregistré sans contrôle réellement effectué).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/transform_item.sql, db/rls.sql, db/audit.sql et
--  db/item_detendeur.sql (a besoin de public.item, public.item_detendeur,
--  public.has_permission, public.audit_write et du miroir
--  public.detendeur_intervention). Ré-exécutable : reprise idempotente (clé
--  d'origine unique).
--
--  POINT DE VIGILANCE — la jointure réelle : detendeur_intervention.detendeur_id
--  référence TOUJOURS public.detendeur (jamais detendeur_sortie_inventaire —
--  vérifié : les 83 valeurs distinctes existent toutes dans detendeur.id,
--  espaces d'ID disjoints entre les deux tables miroir comme pour les
--  bouteilles). Le rapprochement actif/déclassé de db/transform_item.sql
--  (fusion par n° de série) ne crée donc aucune ambiguïté ici : l'item résolu
--  est toujours (origine_table = 'detendeur', origine_id = detendeur_id). Voir
--  la note « Historique legacy » en tête de db/item_detendeur.sql : c'est cet
--  audit qui lève le report décidé lors du ticket précédent (moteur
--  marque/modèle), d'où la reprise ci-dessous.
--
--  Ce fichier pose :
--    1. public.detendeur_evenement — table d'événements (append-only côté
--       client : écriture exclusivement par la fonction SECURITY DEFINER
--       ci-dessous). Les 5 points de contrôle acceptent 'no_ctl' (« non
--       contrôlé ») UNIQUEMENT pour la reprise historique : la fonction
--       d'écriture le refuse pour toute nouvelle saisie (critère
--       d'acceptation : saisie bloquée tant que les 5 points n'ont pas été
--       renseignés).
--    2. RLS lecture (item.read).
--    3. Reprise idempotente des 115 interventions depuis le miroir — alimente
--       aussi public.item_detendeur.date_derniere_intervention (plus proche
--       connue par détendeur), que db/item_detendeur.sql recalcule ensuite en
--       échéance via son trigger existant.
--    4. public.enregistrer_intervention_detendeur() — saisie, 5 points
--       obligatoires (ok / non_ok / sans_objet), idempotente sur
--       p_request_id ; met à jour date_derniere_intervention SANS JAMAIS LA
--       FAIRE RECULER ; refusée sur un ensemble désactivé ou en statut
--       terminal, ou sur une date future.
--    5. Contrôle de volume (NOTICE).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Table d'événements
-- -----------------------------------------------------------------------------

create table if not exists public.detendeur_evenement (
    id                    bigint generated always as identity primary key,
    item_id               bigint not null references public.item (id) on delete cascade,
    -- Un seul type aujourd'hui : contrairement aux bouteilles (contrôle
    -- optique/hydraulique, résultat conforme/echec), une intervention
    -- d'entretien détendeur n'a pas de notion pass/fail globale — seuls les 5
    -- points de contrôle et la date comptent.
    type                  text not null check (type in ('intervention')),
    date_evenement        date not null,
    prestataire           text,
    -- 'no_ctl' : reprise historique uniquement (dérive constatée) — jamais
    -- écrit par public.enregistrer_intervention_detendeur (voir plus bas).
    ctrl_manometre        text check (ctrl_manometre        is null or ctrl_manometre        in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    ctrl_detendeur        text check (ctrl_detendeur        is null or ctrl_detendeur        in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    ctrl_octopus          text check (ctrl_octopus          is null or ctrl_octopus          in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    ctrl_inflateur        text check (ctrl_inflateur        is null or ctrl_inflateur        in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    ctrl_visuel_flexibles text check (ctrl_visuel_flexibles is null or ctrl_visuel_flexibles in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    cout_eur              numeric(12,2) check (cout_eur is null or cout_eur >= 0),
    remarque              text,
    -- id de la ligne du miroir (detendeur_intervention.id) — NULL pour un
    -- événement saisi dans l'appli. UNIQUE : rend la reprise idempotente.
    origine_id            bigint unique,
    -- Clé d'idempotence générée par le formulaire : un rejeu (timeout réseau
    -- puis nouvel envoi) ne crée pas de doublon. NULL pour l'historique repris.
    request_id            uuid unique,
    cree_le               timestamptz not null default now(),
    cree_par              uuid default auth.uid()
);

-- Rattrapage pour une base ayant déjà créé cette table via une version
-- précédente : « create table if not exists » ci-dessus est un no-op total
-- si la table existe déjà, donc ne renomme/n'ajoute jamais les colonnes
-- apparues depuis (même gap que public.item.statut_motif dans
-- db/model_item.sql et public.item_transition.autorite_decision dans
-- db/item_etat.sql). Sans ceci, un REJEU échoue en « column ... does not
-- exist ». Deux générations antérieures à rattraper :
--   - moteur marque/modèle seul (ticket précédent) : déjà date_evenement /
--     type / prestataire, il manque ctrl_* / origine_id ;
--   - version intermédiaire de CE ticket (avant la fusion des deux
--     conceptions) : colonne date_intervention au lieu de date_evenement,
--     pas de type ni de prestataire du tout.
do $$
begin
    if not exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'detendeur_evenement' and column_name = 'date_evenement'
    ) then
        if exists (
            select 1 from information_schema.columns
            where table_schema = 'public' and table_name = 'detendeur_evenement' and column_name = 'date_intervention'
        ) then
            alter table public.detendeur_evenement rename column date_intervention to date_evenement;
        else
            alter table public.detendeur_evenement add column date_evenement date;
        end if;
    end if;
end $$;

alter table public.detendeur_evenement
    add column if not exists type text default 'intervention' check (type in ('intervention')),
    add column if not exists prestataire           text,
    add column if not exists ctrl_manometre        text check (ctrl_manometre        is null or ctrl_manometre        in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    add column if not exists ctrl_detendeur        text check (ctrl_detendeur        is null or ctrl_detendeur        in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    add column if not exists ctrl_octopus          text check (ctrl_octopus          is null or ctrl_octopus          in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    add column if not exists ctrl_inflateur        text check (ctrl_inflateur        is null or ctrl_inflateur        in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    add column if not exists ctrl_visuel_flexibles text check (ctrl_visuel_flexibles is null or ctrl_visuel_flexibles in ('ok', 'non_ok', 'sans_objet', 'no_ctl')),
    add column if not exists origine_id            bigint unique;

comment on table public.detendeur_evenement is
    'Chronologie des interventions d''entretien d''un ensemble détendeur (5 points de contrôle). Repris de detendeur_intervention via detendeur_id -> item.origine_id (famille detendeur). Écritures exclusivement via public.enregistrer_intervention_detendeur.';

create index if not exists ix_detendeur_evenement_item
    on public.detendeur_evenement (item_id, date_evenement desc, id desc);

-- -----------------------------------------------------------------------------
--  2. RLS — lecture seule pour les clients (aucune policy insert/update/delete).
-- -----------------------------------------------------------------------------

alter table public.detendeur_evenement enable row level security;

drop policy if exists detendeur_evenement_sel on public.detendeur_evenement;
create policy detendeur_evenement_sel on public.detendeur_evenement
    for select to authenticated
    using (public.has_permission('item.read'));

-- -----------------------------------------------------------------------------
--  3. Reprise des 115 interventions historiques (idempotente). Alimente aussi
--     item_detendeur.date_derniere_intervention (plus récente connue par
--     détendeur) : db/item_detendeur.sql recalcule ensuite l'échéance via son
--     trigger existant (item_detendeur_sync_echeance).
-- -----------------------------------------------------------------------------

create or replace function pg_temp.detendeur_ctrl_code(p text)
returns text
language sql
immutable
as $$
    select case upper(btrim(coalesce(p, '')))
        when 'OK'         then 'ok'
        when 'NOK'        then 'non_ok'
        when 'SANS OBJET' then 'sans_objet'
        when 'NO CTL'     then 'no_ctl'
        else null
    end
$$;

with source as (
    select
        r.id                as origine_id,
        r.detendeur_id,
        r.date_intervention,
        r.cout_eur,
        nullif(btrim(r.remarque), '') as remarque,
        pg_temp.detendeur_ctrl_code(r.ctrl_manometre)        as ctrl_manometre,
        pg_temp.detendeur_ctrl_code(r.ctrl_detendeur)        as ctrl_detendeur,
        pg_temp.detendeur_ctrl_code(r.ctrl_octopus)          as ctrl_octopus,
        pg_temp.detendeur_ctrl_code(r.ctrl_inflateur)        as ctrl_inflateur,
        pg_temp.detendeur_ctrl_code(r.ctrl_visuel_flexibles) as ctrl_visuel_flexibles,
        (select i.id from public.item i
          where i.origine_table = 'detendeur' and i.origine_id = r.detendeur_id) as item_id
    from public.detendeur_intervention r
)
insert into public.detendeur_evenement
    (item_id, type, date_evenement, prestataire, ctrl_manometre, ctrl_detendeur,
     ctrl_octopus, ctrl_inflateur, ctrl_visuel_flexibles, cout_eur, remarque, origine_id)
select
    s.item_id,
    'intervention',
    s.date_intervention,
    -- Prestataire jamais saisi dans Access pour les détendeurs : NULL plutôt
    -- qu'une valeur devinée (même choix que public.bouteille_evenement).
    null,
    s.ctrl_manometre,
    s.ctrl_detendeur,
    s.ctrl_octopus,
    s.ctrl_inflateur,
    s.ctrl_visuel_flexibles,
    -- 0.0 = « non renseigné » dans Access, comme pour les bouteilles.
    nullif(s.cout_eur, 0),
    s.remarque,
    s.origine_id
from source s
where s.item_id is not null
on conflict (origine_id) do nothing;

-- Lignes non rattachées (detendeur_id introuvable) : consignées verbatim,
-- jamais rattachées « au plus proche ». Aucune aujourd'hui (vérifié), ce
-- bloc protège un futur rejeu avec un miroir modifié.
insert into public.item_reject (origine_table, origine_id, colonne, valeur_brute, raison)
select 'detendeur_intervention', r.id, 'detendeur_id', coalesce(r.detendeur_id::text, ''),
       'id de détendeur introuvable dans detendeur — intervention du ' ||
       coalesce(r.date_intervention::text, 'date inconnue') || ' non rattachée ; arbitrage manuel'
from public.detendeur_intervention r
where not exists (select 1 from public.item i where i.origine_table = 'detendeur' and i.origine_id = r.detendeur_id)
  and not exists (select 1 from public.item_reject j
                   where j.origine_table = 'detendeur_intervention' and j.origine_id = r.id and j.colonne = 'detendeur_id');

-- Rattachement du compteur d'échéance (jamais en arrière, comme pour une
-- saisie applicative) : la plus récente date d'intervention connue par
-- détendeur, qu'elle vienne de l'historique ou d'une saisie déjà enregistrée.
update public.item_detendeur d
set date_derniere_intervention = plus_recente.date_evenement
from (
    select item_id, max(date_evenement) as date_evenement
    from public.detendeur_evenement
    group by item_id
) plus_recente
where plus_recente.item_id = d.item_id
  and (d.date_derniere_intervention is null or d.date_derniere_intervention < plus_recente.date_evenement);

-- -----------------------------------------------------------------------------
--  4. Enregistrer une intervention depuis la fiche détendeur — les 5 points de
--     contrôle sont OBLIGATOIRES et doivent être une valeur de contrôle réelle
--     (ok / non_ok / sans_objet) : 'no_ctl' est refusé, impossible de
--     reproduire la dérive historique depuis l'appli. Met à jour la dernière
--     intervention connue (jamais en arrière), le trigger
--     public.tg_item_detendeur_sync_echeance (db/item_detendeur.sql) recalcule
--     ensuite l'échéance depuis le référentiel marque/modèle.
-- -----------------------------------------------------------------------------

create or replace function public.enregistrer_intervention_detendeur(
    p_item_id               bigint,
    p_date                  date,
    p_ctrl_manometre        text,
    p_ctrl_detendeur        text,
    p_ctrl_octopus          text,
    p_ctrl_inflateur        text,
    p_ctrl_visuel_flexibles text,
    p_prestataire           text,
    p_cout_eur              numeric,
    p_remarque              text,
    p_request_id            uuid default null)
returns bigint
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_id   bigint;
    v_cout numeric(12,2) := round(p_cout_eur, 2);
begin
    if not public.has_permission('item.update') then
        raise exception 'Droit insuffisant : permission « item.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_date is null then
        raise exception 'Date de l''intervention obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date > (now() at time zone 'Europe/Paris')::date then
        raise exception 'Date de l''intervention dans le futur : refusée.' using errcode = 'check_violation';
    end if;

    if p_ctrl_manometre        is null or p_ctrl_manometre        not in ('ok', 'non_ok', 'sans_objet')
    or p_ctrl_detendeur        is null or p_ctrl_detendeur        not in ('ok', 'non_ok', 'sans_objet')
    or p_ctrl_octopus          is null or p_ctrl_octopus          not in ('ok', 'non_ok', 'sans_objet')
    or p_ctrl_inflateur        is null or p_ctrl_inflateur        not in ('ok', 'non_ok', 'sans_objet')
    or p_ctrl_visuel_flexibles is null or p_ctrl_visuel_flexibles not in ('ok', 'non_ok', 'sans_objet') then
        raise exception 'Les 5 points de contrôle (manomètre, détendeur, octopus, inflateur, visuel flexibles) sont obligatoires (ok / non_ok / sans_objet).'
            using errcode = 'check_violation';
    end if;

    if v_cout is not null and v_cout < 0 then
        raise exception 'Coût négatif : refusé.' using errcode = 'check_violation';
    end if;

    -- Verrou sur l'ensemble : sérialise deux saisies concurrentes (le calcul
    -- « jamais en arrière » ci-dessous lit puis écrit le compteur).
    perform 1 from public.item_detendeur where item_id = p_item_id for update;
    if not found then
        raise exception 'Ensemble détendeur introuvable : %.', p_item_id using errcode = 'no_data_found';
    end if;

    -- Une intervention renouvelle l'échéance : elle n'a pas de sens sur un
    -- ensemble désactivé ou en statut terminal (rebuté, perdu…) — même règle
    -- que public.enregistrer_requalification_bouteille.
    if exists (
        select 1 from public.item i
        join public.ref_statut s on s.code = i.statut_code
        where i.id = p_item_id and (not i.actif or s.est_terminal)
    ) then
        raise exception 'Intervention refusée : ensemble désactivé ou en statut terminal.'
            using errcode = 'check_violation';
    end if;

    -- Idempotence : un rejeu (même p_request_id) renvoie l'événement déjà créé.
    insert into public.detendeur_evenement
        (item_id, type, date_evenement, prestataire, ctrl_manometre, ctrl_detendeur,
         ctrl_octopus, ctrl_inflateur, ctrl_visuel_flexibles, cout_eur, remarque, request_id)
    values
        (p_item_id, 'intervention', p_date, nullif(btrim(p_prestataire), ''), p_ctrl_manometre, p_ctrl_detendeur,
         p_ctrl_octopus, p_ctrl_inflateur, p_ctrl_visuel_flexibles, v_cout, nullif(btrim(p_remarque), ''), p_request_id)
    on conflict (request_id) do nothing
    returning id into v_id;

    if v_id is null then
        select id into v_id from public.detendeur_evenement where request_id = p_request_id;
        return v_id;
    end if;

    update public.item_detendeur
    set date_derniere_intervention = p_date
    where item_id = p_item_id
      and (date_derniere_intervention is null or date_derniere_intervention < p_date);

    perform public.audit_write(
        'detendeur.intervention',
        'item',
        p_item_id::text,
        null,
        jsonb_build_object(
            'evenement_id', v_id, 'date', p_date, 'cout_eur', v_cout,
            'prestataire', nullif(btrim(p_prestataire), ''),
            'ctrl_manometre', p_ctrl_manometre, 'ctrl_detendeur', p_ctrl_detendeur,
            'ctrl_octopus', p_ctrl_octopus, 'ctrl_inflateur', p_ctrl_inflateur,
            'ctrl_visuel_flexibles', p_ctrl_visuel_flexibles),
        '{}'::jsonb);

    return v_id;
end $$;

comment on function public.enregistrer_intervention_detendeur(bigint, date, text, text, text, text, text, text, numeric, text, uuid) is
    'Historise une intervention d''entretien sur un ensemble détendeur. Les 5 points de contrôle sont obligatoires (ok/non_ok/sans_objet — jamais no_ctl, réservé à la reprise historique). Met à jour date_derniere_intervention SANS JAMAIS LA FAIRE RECULER (échéance recalculée par trigger, db/item_detendeur.sql, depuis le référentiel marque/modèle). Refuse une date future, un coût négatif, ou un ensemble désactivé/en statut terminal. Idempotent sur p_request_id. Exige « item.update ». Journalisé (detendeur.intervention).';

revoke execute on function public.enregistrer_intervention_detendeur(bigint, date, text, text, text, text, text, text, numeric, text, uuid) from public;
grant execute on function public.enregistrer_intervention_detendeur(bigint, date, text, text, text, text, text, text, numeric, text, uuid) to authenticated;

-- Ancienne signature (ticket précédent, sans les 5 points) : évite une
-- surcharge ambiguë pour les bases l'ayant déjà posée.
drop function if exists public.enregistrer_intervention_detendeur(bigint, date, text, numeric, text, uuid);

-- -----------------------------------------------------------------------------
--  5. Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_source  bigint;
    v_repris  bigint;
    v_rejetes bigint;
begin
    select count(*) into v_source  from public.detendeur_intervention;
    select count(*) into v_repris  from public.detendeur_evenement where origine_id is not null;
    select count(distinct origine_id) into v_rejetes from public.item_reject where origine_table = 'detendeur_intervention';

    raise notice 'detendeur_evenement : % intervention(s) source, % reprise(s), % rejetée(s) (item_reject) — écart % (attendu 0).',
        v_source, v_repris, v_rejetes, v_source - v_repris - v_rejetes;
end $$;

commit;
