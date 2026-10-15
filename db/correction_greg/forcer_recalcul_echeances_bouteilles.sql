-- =============================================================================
--  MoaMat — Script de RÉCUPÉRATION ponctuel : force le recalcul des échéances
--  bouteille après un rejeu de db/transform_item.sql.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Contexte : db/transform_item.sql ne renseigne jamais
--  item_bouteille.famille / .matiere / date_dernier_controle_optique /
--  date_dernier_controle_hydraulique depuis le miroir Access — ces valeurs
--  sont normalement posées APRÈS coup par les scripts de backfill ci-dessous
--  (db/correction_greg/). Tant qu'elles sont NULL,
--  public.bouteille_type_referentiel() renvoie NULL et aucune échéance ne
--  peut être calculée (« date inconnue » côté appli), même si
--  db/correction_greg/recreate_bouteille_evenement.sql a déjà restauré
--  l'historique des contrôles.
--
--  Ce script rejoue, dans l'ordre, les backfills existants (tous
--  idempotents : ne touchent que les lignes encore NULL) :
--    1. backfill_classification_plongee.sql — famille='plongee' / matiere='acier'
--       par défaut pour les bouteilles marquées 'PLONGEE' dans le miroir brut.
--    2. backfill_matiere_acier.sql          — matiere='acier' par défaut pour
--       tout ce qui reste NULL (y compris deco_o2/o2_secourisme/bloc_tampon,
--       où matiere n'entre dans aucun calcul).
--    3. backfill_dernier_controle_mise_en_service.sql — compteurs encore NULL
--       initialisés à la date de mise en service.
--    4. aligner_controle_optique_sur_hydraulique.sql — corrige un éventuel
--       écart optique/hydraulique introduit par l'étape 3.
--
--  À exécuter APRÈS db/correction_greg/recreate_bouteille_evenement.sql
--  (sinon les compteurs de contrôle réellement connus seraient écrasés par le
--  fallback « date de mise en service » de l'étape 3 au lieu des vraies
--  dates reprises de l'historique).
--
--  CE QUE CE SCRIPT NE RÉCUPÈRE PAS : la famille pour les bouteilles
--  deco_o2 / o2_secourisme / bloc_tampon, et la matière alu/carbone — ces
--  classifications n'existent dans AUCUNE source automatique (le miroir Access
--  ne distingue que 'PLONGEE' vs le reste) : elles avaient été saisies à la
--  main via public.corriger_bouteille et ne sont tracées que dans
--  public.audit_log (indexé par l'ancien item.id, cf. §3 de
--  recreate_bouteille_evenement.sql). Le §5 ci-dessous liste ce qui reste
--  à classifier manuellement.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Classification plongée depuis le miroir brut (bouteille.famille = 'PLONGEE')
-- -----------------------------------------------------------------------------

with cible as (
    select b.item_id
    from public.item_bouteille b
    join public.item i on i.id = b.item_id
    where b.famille is null
      and b.matiere is null
      and (
        (i.origine_table = 'bouteille' and exists (
            select 1 from public.bouteille br
            where br.id = i.origine_id
              and upper(btrim(br.famille)) = 'PLONGEE'
        ))
        or
        (i.origine_table = 'bouteille_sortie_inventaire' and exists (
            select 1 from public.bouteille_sortie_inventaire br
            where br.id = i.origine_id
              and upper(btrim(br.famille)) = 'PLONGEE'
        ))
      )
)
update public.item_bouteille b
set famille = 'plongee',
    matiere = 'acier'
from cible c
where b.item_id = c.item_id;

-- -----------------------------------------------------------------------------
--  2. Matière par défaut pour tout ce qui reste NULL
-- -----------------------------------------------------------------------------

update public.item_bouteille b
set matiere = 'acier'
where b.matiere is null;

-- -----------------------------------------------------------------------------
--  3. Compteurs encore NULL -> date de mise en service (si connue)
-- -----------------------------------------------------------------------------

update public.item_bouteille b
set date_dernier_controle_optique     = coalesce(b.date_dernier_controle_optique, b.date_mise_en_service),
    date_dernier_controle_hydraulique = coalesce(b.date_dernier_controle_hydraulique, b.date_mise_en_service)
where b.date_mise_en_service is not null
  and (b.date_dernier_controle_optique is null or b.date_dernier_controle_hydraulique is null);

-- -----------------------------------------------------------------------------
--  4. Alignement optique <- hydraulique quand ce dernier est plus récent
-- -----------------------------------------------------------------------------

update public.item_bouteille b
set date_dernier_controle_optique = b.date_dernier_controle_hydraulique
where b.date_dernier_controle_optique is not null
  and b.date_dernier_controle_hydraulique is not null
  and b.date_dernier_controle_hydraulique > b.date_dernier_controle_optique;

-- -----------------------------------------------------------------------------
--  5. Diagnostic (SELECT seul) : ce qui reste à classifier manuellement
--     (famille toujours NULL -> ni plongee ni mirrored 'PLONGEE' : deco_o2 /
--     o2_secourisme / bloc_tampon perdus, à ressaisir via la fiche bouteille).
-- -----------------------------------------------------------------------------

select
    i.id            as item_id,
    i.code_club,
    i.num_serie,
    i.marque,
    i.modele,
    b.famille,
    b.matiere,
    b.date_dernier_controle_optique,
    b.date_dernier_controle_hydraulique,
    i.date_echeance
from public.item i
join public.item_bouteille b on b.item_id = i.id
where i.actif
  and b.famille is null
order by i.code_club;

-- -----------------------------------------------------------------------------
--  6. Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_total       bigint;
    v_sans_classe bigint;
    v_sans_echeance bigint;
begin
    select count(*) into v_total         from public.item_bouteille;
    select count(*) into v_sans_classe   from public.item_bouteille where famille is null;
    select count(*) into v_sans_echeance
        from public.item i
        join public.item_bouteille b on b.item_id = i.id
        where i.actif and i.date_echeance is null;

    raise notice 'forcer_recalcul_echeances_bouteilles : % bouteille(s) au total, % encore sans famille classifiée, % bouteille(s) active(s) sans échéance calculée.',
        v_total, v_sans_classe, v_sans_echeance;
end $$;

commit;
