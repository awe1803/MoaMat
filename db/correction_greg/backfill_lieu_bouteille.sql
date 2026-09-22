-- =============================================================================
--  MoaMat — Backfill item.lieu_contenant_id pour les bouteilles (famille
--  'bouteille'), dont l'arbitrage lieu_mapping n'avait jamais été complété
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql et db/transform_item.sql. Ne rejoue PAS
--  transform_item.sql (qui truncate/recharge public.item en cascade et
--  détruirait l'historique déjà enregistré via l'appli — item_transition,
--  bouteille_evenement, etc.) : ce script corrige item.lieu_contenant_id en
--  place, sans toucher à public.item.id ni à aucune table d'historique.
--
--  Constat : lieu_mapping ne matche que sur UN SEUL champ source (site OU
--  local) à la fois. Comme le local Access "Piscine" est partagé par
--  plusieurs sites, un mapping global sur 'bouteille.local' collerait à tort
--  toutes les "Piscine" dans un seul contenant. On mappe donc ici sur la paire
--  (site, local) exacte, ce que lieu_mapping ne sait pas exprimer — d'où un
--  script dédié plutôt qu'un simple UPDATE de lieu_mapping + rejeu.
--
--  Ré-exécutable sans erreur : les INSERT sont protégés par ON CONFLICT DO
--  NOTHING, et l'UPDATE réaffecte idempotemment le même contenant.
-- -----------------------------------------------------------------------------

begin;

-- 1) Crée la hiérarchie Local/Contenant manquante (additif, idempotent).
--    Les sections (Seraing, Haccourt, Fléron, Robertville…) existent déjà,
--    reprises d'Access ; seuls les niveaux Local et Contenant manquaient.
with sections as (
    select id, libelle from public.lieu_section
),
targets(section_libelle, local_libelle) as (
    values
        ('Seraing', 'Piscine'),
        ('Haccourt', 'Piscine'),
        ('Fléron', 'Piscine'),
        ('Robertville', 'Centre de Plongée'),
        ('Robertville', 'Piscine')
)
insert into public.lieu_local (section_id, libelle)
select s.id, t.local_libelle
from targets t
join sections s on s.libelle = t.section_libelle
on conflict (section_id, libelle) do nothing;

insert into public.lieu_contenant (local_id, libelle)
select l.id, l.libelle
from public.lieu_local l
join public.lieu_section s on s.id = l.section_id
where (s.libelle, l.libelle) in (
    ('Seraing', 'Piscine'),
    ('Haccourt', 'Piscine'),
    ('Fléron', 'Piscine'),
    ('Robertville', 'Centre de Plongée'),
    ('Robertville', 'Piscine')
)
on conflict (local_id, libelle) do nothing;

-- 2) Corrige directement les items bouteille existants, par paire
--    (site, local) brute Access exacte.
with raw_mapping(site_brut, local_brut, section_libelle, local_libelle) as (
    values
        ('SERAING',     'Piscine',            'Seraing',     'Piscine'),
        ('Seraing',     'Piscine',            'Seraing',     'Piscine'),
        ('SERAING',     'PISCINE',            'Seraing',     'Piscine'),
        ('HACCOURT',    'Piscine',            'Haccourt',    'Piscine'),
        ('FLERON',      'Piscine',            'Fléron',      'Piscine'),
        ('ROBERTVILLE', 'Centre de Plongée',  'Robertville', 'Centre de Plongée'),
        ('ROBERTVILLE', 'Piscine',            'Robertville', 'Piscine')
),
resolved as (
    select rm.site_brut, rm.local_brut, c.id as contenant_id
    from raw_mapping rm
    join public.lieu_section s on s.libelle = rm.section_libelle
    join public.lieu_local   l on l.section_id = s.id and l.libelle = rm.local_libelle
    join public.lieu_contenant c on c.local_id = l.id and c.libelle = rm.local_libelle
)
update public.item i
set lieu_contenant_id = r.contenant_id
from public.bouteille b
join resolved r on btrim(b.site) = r.site_brut and btrim(b.local) = r.local_brut
where i.origine_table = 'bouteille'
  and i.origine_id = b.id
  and i.famille = 'bouteille';

-- 3) Garde lieu_mapping à jour pour les prochaines reprises Access.
--    ROBERTVILLE reste volontairement non arbitré ici : deux contenants
--    possibles selon le local (Centre de Plongée / Piscine), que la table
--    lieu_mapping ne peut pas distinguer sur le seul champ 'site'.
update public.lieu_mapping lm
set contenant_id = r.contenant_id
from (
    select rm.source_valeur, c.id as contenant_id
    from (values
        ('SERAING', 'Seraing', 'Piscine'),
        ('Seraing', 'Seraing', 'Piscine'),
        ('HACCOURT', 'Haccourt', 'Piscine'),
        ('FLERON', 'Fléron', 'Piscine')
    ) as rm(source_valeur, section_libelle, local_libelle)
    join public.lieu_section s on s.libelle = rm.section_libelle
    join public.lieu_local l on l.section_id = s.id and l.libelle = rm.local_libelle
    join public.lieu_contenant c on c.local_id = l.id and c.libelle = rm.local_libelle
) r
where lm.source_champ = 'bouteille.site' and lm.source_valeur = r.source_valeur;

commit;

-- Vérification (doit renvoyer 0 sans_localisation) :
--   select
--       count(*) filter (where lieu_contenant_id is null)     as sans_localisation,
--       count(*) filter (where lieu_contenant_id is not null) as avec_localisation,
--       count(*)                                              as total
--   from public.item
--   where famille = 'bouteille' and actif = true;
