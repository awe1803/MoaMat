-- =============================================================================
--  MoaMat — Script de RÉCUPÉRATION ponctuel (hors cycle de migration normal).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Contexte : un rejeu de db/transform_item.sql fait
--  « truncate table public.item restart identity cascade », qui vide aussi
--  public.item_bouteille et public.bouteille_evenement (FK on delete cascade
--  vers public.item). Deux pertes distinctes à réparer :
--
--    1. public.bouteille_evenement — entièrement vide (412 événements repris
--       d'Access + tout événement saisi dans l'appli). Récupérable à 100 % :
--       la reprise se fait sur (origine_table, origine_id), stable, jamais
--       sur item.id (remis à zéro par le truncate).
--
--    2. public.item_bouteille.date_dernier_controle_optique /
--       date_dernier_controle_hydraulique — remis à NULL (colonnes absentes
--       du miroir Access, jamais renseignées par transform_item.sql).
--       Recalculées ci-dessous à partir de l'historique restauré en §1.
--
--  CE QUE CE SCRIPT NE RÉCUPÈRE PAS : les événements saisis à la main dans
--  l'appli (enregistrer_requalification_bouteille / signaler_incident_bouteille)
--  après la reprise initiale, et les corrections manuelles (corriger_bouteille)
--  — tracés uniquement dans public.audit_log, indexé par l'ANCIEN item.id, qui
--  ne correspond plus aux ids actuels après le truncate. Le §3 liste ces
--  entrées pour arbitrage manuel (aucune écriture automatique).
--
--  Pré-requis : db/bouteille_evenement.sql et db/item_bouteille.sql déjà
--  exécutés (structures en place). Idempotent sur bouteille_evenement
--  (unique sur origine_id) ; le §2 ne fait jamais reculer une date déjà
--  renseignée par ailleurs depuis ce rejeu.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Reprise des 412 événements historiques (copie de db/bouteille_evenement.sql §3)
-- -----------------------------------------------------------------------------

with source as (
    select
        r.id                                        as origine_id,
        r.bouteille_id,
        r.date_operation,
        r.type_operation,
        r.cout_eur,
        r.date_echeance_suivante,
        nullif(btrim(r.remarque), '')               as remarque,
        nullif(btrim(r.num_certificat), '')         as num_certificat,
        case btrim(r.type_operation)
            when 'New'                              then 'mise_en_service'
            when 'RR - Optique'                     then 'controle_optique'
            when 'R - Hydraulique'                  then 'controle_hydraulique'
            when 'Ecartée suite à une défectuosité' then 'ecartee'
            when '-REBUTEE-'                        then 'rebut'
        end                                         as type,
        coalesce(
            (select i.id from public.item i
              where i.origine_table = 'bouteille' and i.origine_id = r.bouteille_id),
            (select i.id from public.item i
              where i.origine_table = 'bouteille_sortie_inventaire' and i.origine_id = r.bouteille_id)
        )                                           as item_id
    from public.bouteille_requalification r
)
insert into public.bouteille_evenement
    (item_id, type, date_evenement, prestataire, cout_eur, num_certificat,
     date_echeance_suivante, remarque, origine_id)
select
    s.item_id,
    s.type,
    s.date_operation,
    null,
    nullif(s.cout_eur, 0),
    s.num_certificat,
    s.date_echeance_suivante,
    s.remarque,
    s.origine_id
from source s
where s.item_id is not null
  and s.type is not null
on conflict (origine_id) do nothing;

-- -----------------------------------------------------------------------------
--  2. Recalcul des compteurs de contrôle depuis l'historique restauré.
--     « echec » n'avance jamais le compteur (même règle que
--     enregistrer_requalification_bouteille) ; les lignes reprises d'Access
--     ont resultat NULL et comptent comme faits historiques valides.
-- -----------------------------------------------------------------------------

with dernier_optique as (
    select item_id, max(date_evenement) as date_max
    from public.bouteille_evenement
    where type = 'controle_optique'
      and (resultat is null or resultat = 'conforme')
      and date_evenement is not null
    group by item_id
)
update public.item_bouteille b
set date_dernier_controle_optique = d.date_max
from dernier_optique d
where d.item_id = b.item_id
  and (b.date_dernier_controle_optique is null or b.date_dernier_controle_optique < d.date_max);

with dernier_hydraulique as (
    select item_id, max(date_evenement) as date_max
    from public.bouteille_evenement
    where type = 'controle_hydraulique'
      and (resultat is null or resultat = 'conforme')
      and date_evenement is not null
    group by item_id
)
update public.item_bouteille b
set date_dernier_controle_hydraulique = d.date_max
from dernier_hydraulique d
where d.item_id = b.item_id
  and (b.date_dernier_controle_hydraulique is null or b.date_dernier_controle_hydraulique < d.date_max);

-- -----------------------------------------------------------------------------
--  3. Diagnostic (SELECT seul, aucune écriture) : ce qui reste à arbitrer
--     manuellement depuis audit_log — corrections et événements saisis dans
--     l'appli que ce script ne peut pas rattacher automatiquement (l'ancien
--     item.id référencé dans entity_id ne correspond plus aux ids actuels).
-- -----------------------------------------------------------------------------

select
    occurred_at,
    actor_email,
    action,
    entity_id as ancien_item_id,
    before,
    after,
    context
from public.audit_log
where entity_table = 'item'
  and action in ('bouteille.correction', 'bouteille.requalification', 'bouteille.incident')
order by occurred_at desc;

-- -----------------------------------------------------------------------------
--  4. Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_source   bigint;
    v_repris   bigint;
    v_optique  bigint;
    v_hydrau   bigint;
begin
    select count(*) into v_source  from public.bouteille_requalification;
    select count(*) into v_repris  from public.bouteille_evenement where origine_id is not null;
    select count(*) into v_optique from public.item_bouteille where date_dernier_controle_optique is not null;
    select count(*) into v_hydrau  from public.item_bouteille where date_dernier_controle_hydraulique is not null;

    raise notice 'recreate_bouteille_evenement : % événement(s) source, % repris ; % bouteille(s) avec contrôle optique daté, % avec contrôle hydraulique daté.',
        v_source, v_repris, v_optique, v_hydrau;
end $$;

commit;
