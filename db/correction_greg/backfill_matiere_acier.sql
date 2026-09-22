-- =============================================================================
--  MoaMat — Backfill item_bouteille.matiere = 'acier' par défaut, uniquement
--  pour les bouteilles sans classification faite
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/item_bouteille.sql (a besoin de public.item_bouteille).
--  Ré-exécutable sans erreur : ne touche que les lignes matiere IS NULL,
--  jamais une classification déjà saisie (acier, alu OU carbone).
--
--  matiere n'est significative que pour famille = 'plongee'
--  (db/item_bouteille.sql §1) mais ce UPDATE ne filtre pas sur famille : il
--  s'applique à toute ligne item_bouteille dont matiere est encore NULL, y
--  compris deco_o2 / o2_secourisme / bloc_tampon, où matiere sera écrite mais
--  n'entre dans aucun calcul (public.bouteille_type_referentiel ignore
--  matiere hors 'plongee').
--
--  matiere fait partie des colonnes surveillées par
--  public.tg_item_bouteille_sync_echeance (db/item_bouteille.sql §7) : pour
--  les bouteilles de plongée jusque-là non classées, l'échéance hydraulique
--  (jusqu'ici NULL, faute de type_referentiel résolvable) est donc calculée
--  automatiquement dans la foulée, avec la périodicité 'plongee_acier'.
-- -----------------------------------------------------------------------------

begin;

update public.item_bouteille b
set matiere = 'acier'
where b.matiere is null
returning b.item_id, b.famille, b.matiere;

commit;

-- Pour prévisualiser SANS écrire :
--   select item_id, famille, matiere
--   from public.item_bouteille
--   where matiere is null;
