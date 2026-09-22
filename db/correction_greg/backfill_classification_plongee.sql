-- =============================================================================
--  MoaMat — Backfill de classification par défaut pour les bouteilles de
--  plongée (Type de gaz + Matière)
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/transform_item.sql et db/item_bouteille.sql (a besoin
--  de public.item, public.item_bouteille, public.bouteille,
--  public.bouteille_sortie_inventaire). Ré-exécutable sans erreur : ne touche
--  que les lignes encore NULL, jamais une classification déjà saisie.
--
--  Contexte : db/transform_item.sql ne renseigne jamais
--  item_bouteille.famille / .matiere depuis le miroir Access — cf.
--  db/item_bouteille.sql §1 (« Nullable : pas de contrainte NOT NULL
--  rétroactive... db/transform_item.sql ne les renseigne pas ») et
--  db/MODELE.md §"Bouteilles" (classification pas encore faite, hors base de
--  données). « Type de gaz » sur la fiche bouteille (CylinderSheet.razor,
--  PdfLifeSheetRenderer.cs) est l'étiquette affichée pour
--  item_bouteille.famille (public.item_bouteille.famille -> CylinderUsage,
--  db/MoaMat.Domain/Cylinders/CylinderUsage.cs) : ce n'est PAS
--  bouteille_membre.gaz_id (ref_gaz), qui concerne le gaz emprunté par un
--  membre, pas la bouteille elle-même.
--
--  Ce script identifie les bouteilles de plongée à partir du miroir brut
--  (public.bouteille.famille / public.bouteille_sortie_inventaire.famille =
--  'PLONGEE', cf. db/initial_load.sql) et leur applique un classement par
--  défaut :
--    - item_bouteille.famille = 'plongee'  (Type de gaz : Plongée)
--    - item_bouteille.matiere = 'acier'    (Matière par défaut — à corriger
--      manuellement via la fiche bouteille pour les bouteilles alu/carbone,
--      cette distinction n'existe pas dans le miroir)
--
--  Ne touche QUE les lignes dont famille ET matiere sont encore NULL toutes
--  les deux : une classification déjà saisie (ex. par un super-admin via
--  public.corriger_bouteille) n'est jamais écrasée. Ce UPDATE passe par les
--  colonnes surveillées par public.tg_item_bouteille_sync_echeance
--  (db/item_bouteille.sql §7) : les échéances des bouteilles concernées sont
--  donc recalculées automatiquement dans la foulée.
-- -----------------------------------------------------------------------------

begin;

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
where b.item_id = c.item_id
returning b.item_id, b.famille, b.matiere;

commit;

-- Pour prévisualiser SANS écrire, exécuter uniquement la CTE `cible`
-- ci-dessus (remplacer le UPDATE final par un `select item_id from cible`),
-- ou compter les candidats :
--   select count(*)
--   from public.item_bouteille b
--   join public.item i on i.id = b.item_id
--   where b.famille is null and b.matiere is null
--     and (
--       (i.origine_table = 'bouteille' and exists (
--           select 1 from public.bouteille br
--           where br.id = i.origine_id and upper(btrim(br.famille)) = 'PLONGEE'))
--       or (i.origine_table = 'bouteille_sortie_inventaire' and exists (
--           select 1 from public.bouteille_sortie_inventaire br
--           where br.id = i.origine_id and upper(btrim(br.famille)) = 'PLONGEE'))
--     );
