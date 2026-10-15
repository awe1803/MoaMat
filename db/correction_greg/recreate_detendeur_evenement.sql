-- =============================================================================
--  MoaMat — Script de RÉCUPÉRATION ponctuel (hors cycle de migration normal).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Contexte : comme pour les bouteilles (cf.
--  db/correction_greg/recreate_bouteille_evenement.sql), un rejeu de
--  db/transform_item.sql fait « truncate table public.item restart identity
--  cascade », qui vide aussi public.item_detendeur et
--  public.detendeur_evenement (FK on delete cascade vers public.item).
--
--  Contrairement aux bouteilles, rien d'autre n'est perdu : marque/modele
--  sont des colonnes génériques sur public.item, réécrites à l'identique
--  depuis le miroir par transform_item.sql, et public.ref_periodicite_detendeur
--  n'a aucune FK vers item (table de référentiel indépendante, pas touchée par
--  le truncate). Il suffit donc de restaurer :
--
--    1. public.detendeur_evenement — 115 interventions reprises du miroir
--       (detendeur_intervention), rattachement robuste au truncate car fondé
--       sur (origine_table, origine_id), jamais sur item.id.
--    2. public.item_detendeur.date_derniere_intervention — la plus récente
--       date connue par détendeur, recalculée depuis l'historique restauré en
--       §1. Le trigger existant public.tg_item_detendeur_sync_echeance
--       (db/item_detendeur.sql) recopie ensuite automatiquement
--       public.item.date_echeance depuis public.ref_periodicite_detendeur
--       (marque/modèle) — rien à recalculer à la main pour l'échéance.
--
--  CE QUE CE SCRIPT NE RÉCUPÈRE PAS : les interventions saisies à la main
--  dans l'appli (public.enregistrer_intervention_detendeur) après la reprise
--  initiale — tracées uniquement dans public.audit_log
--  (action = 'detendeur.intervention'), indexé par l'ANCIEN item.id, qui ne
--  correspond plus aux ids actuels après le truncate. Le §3 liste ces entrées
--  pour arbitrage manuel (aucune écriture automatique).
--
--  Pré-requis : db/detendeur_evenement.sql et db/item_detendeur.sql déjà
--  exécutés (structures en place). Idempotent sur detendeur_evenement
--  (unique sur origine_id) ; le §2 ne fait jamais reculer une date déjà
--  renseignée par ailleurs depuis ce rejeu.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Reprise des 115 interventions historiques (copie de db/detendeur_evenement.sql §3)
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
    null,
    s.ctrl_manometre,
    s.ctrl_detendeur,
    s.ctrl_octopus,
    s.ctrl_inflateur,
    s.ctrl_visuel_flexibles,
    nullif(s.cout_eur, 0),
    s.remarque,
    s.origine_id
from source s
where s.item_id is not null
on conflict (origine_id) do nothing;

-- -----------------------------------------------------------------------------
--  2. Recalcul de la dernière intervention connue depuis l'historique restauré
--     (jamais en arrière — même règle que public.enregistrer_intervention_detendeur).
--     Déclenche automatiquement public.tg_item_detendeur_sync_echeance, qui
--     recopie l'échéance dans public.item.date_echeance.
-- -----------------------------------------------------------------------------

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
--  3. Diagnostic (SELECT seul, aucune écriture) : interventions saisies dans
--     l'appli après la reprise initiale, que ce script ne peut pas rattacher
--     automatiquement (l'ancien item.id référencé dans entity_id ne
--     correspond plus aux ids actuels).
-- -----------------------------------------------------------------------------

select
    occurred_at,
    actor_email,
    action,
    entity_id as ancien_item_id,
    after,
    context
from public.audit_log
where entity_table = 'item'
  and action = 'detendeur.intervention'
order by occurred_at desc;

-- -----------------------------------------------------------------------------
--  4. Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_source        bigint;
    v_repris        bigint;
    v_avec_derniere bigint;
    v_avec_echeance bigint;
begin
    select count(*) into v_source        from public.detendeur_intervention;
    select count(*) into v_repris        from public.detendeur_evenement where origine_id is not null;
    select count(*) into v_avec_derniere from public.item_detendeur where date_derniere_intervention is not null;
    select count(*) into v_avec_echeance
        from public.item i
        join public.item_detendeur d on d.item_id = i.id
        where i.actif and i.date_echeance is not null;

    raise notice 'recreate_detendeur_evenement : % intervention(s) source, % reprise(s) ; % ensemble(s) avec dernière intervention connue, % avec échéance calculée.',
        v_source, v_repris, v_avec_derniere, v_avec_echeance;
end $$;

commit;
