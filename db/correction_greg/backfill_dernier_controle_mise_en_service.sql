-- =============================================================================
--  MoaMat — Backfill des compteurs de contrôle depuis la date de mise en
--  service, quand ils sont encore inconnus
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/item_bouteille.sql (a besoin de public.item_bouteille).
--  Ré-exécutable sans erreur : ne touche que les compteurs encore NULL.
--
--  Contexte : comme public.item_bouteille.famille / .matiere (cf.
--  db/backfill_classification_plongee.sql), db/transform_item.sql ne
--  renseigne jamais date_dernier_controle_optique / date_dernier_controle_hydraulique
--  depuis le miroir Access — ces deux compteurs restent NULL tant qu'aucune
--  requalification n'a été saisie sur la fiche. Tant qu'ils sont NULL,
--  public.bouteille_echeance() renvoie NULL (cf. db/item_bouteille.sql §5) :
--  la bouteille n'a donc aucune échéance calculée, même si sa classification
--  (famille/matiere) est correcte.
--
--  Ce script pose, pour rattrapage, la date de mise en service
--  (item_bouteille.date_mise_en_service) comme point de départ des compteurs
--  encore NULL : à défaut d'un historique de contrôle connu, c'est la mise en
--  service qui fait courir le premier délai réglementaire, comme pour une
--  bouteille neuve. Chaque compteur (optique / hydraulique) est traité
--  indépendamment, sans alternance codée en dur, cohérent avec le moteur
--  d'échéances (db/item_bouteille.sql §5 / CylinderDueDateEngine.cs).
--
--  Ne touche QUE les compteurs NULL, jamais un contrôle déjà saisi ; ne fait
--  rien si date_mise_en_service est elle-même NULL (rien à propager). Ce
--  UPDATE passe par les colonnes surveillées par
--  public.tg_item_bouteille_sync_echeance (db/item_bouteille.sql §7) : les
--  échéances des bouteilles concernées sont donc recalculées automatiquement
--  dans la foulée.
-- -----------------------------------------------------------------------------

begin;

update public.item_bouteille b
set date_dernier_controle_optique     = coalesce(b.date_dernier_controle_optique, b.date_mise_en_service),
    date_dernier_controle_hydraulique = coalesce(b.date_dernier_controle_hydraulique, b.date_mise_en_service)
where b.date_mise_en_service is not null
  and (b.date_dernier_controle_optique is null or b.date_dernier_controle_hydraulique is null)
returning b.item_id, b.date_mise_en_service,
          b.date_dernier_controle_optique, b.date_dernier_controle_hydraulique;

commit;

-- Pour prévisualiser SANS écrire, remplacer le UPDATE par :
--   select item_id, date_mise_en_service,
--          date_dernier_controle_optique, date_dernier_controle_hydraulique
--   from public.item_bouteille
--   where date_mise_en_service is not null
--     and (date_dernier_controle_optique is null or date_dernier_controle_hydraulique is null);
