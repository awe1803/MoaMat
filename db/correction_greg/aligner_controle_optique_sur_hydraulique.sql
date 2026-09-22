-- =============================================================================
--  MoaMat — Aligne le dernier contrôle optique sur le dernier contrôle
--  hydraulique quand celui-ci est plus récent
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/item_bouteille.sql (a besoin de public.item_bouteille).
--  Ré-exécutable sans erreur : ne touche que les lignes où l'écart existe
--  encore.
--
--  Ne touche que les bouteilles où LES DEUX compteurs sont renseignés
--  (NULL n'est jamais "supérieur" à une date, donc une bouteille sans
--  contrôle optique connu n'est pas affectée par ce script). Un contrôle
--  hydraulique implique nécessairement un examen visuel de la bouteille au
--  passage : si sa date est postérieure au dernier contrôle optique connu,
--  ce dernier est donc considéré à jour à cette même date.
--
--  date_dernier_controle_optique fait partie des colonnes surveillées par
--  public.tg_item_bouteille_sync_echeance (db/item_bouteille.sql §7) : les
--  échéances optiques des bouteilles concernées sont donc recalculées
--  automatiquement dans la foulée.
-- -----------------------------------------------------------------------------

begin;

update public.item_bouteille b
set date_dernier_controle_optique = b.date_dernier_controle_hydraulique
where b.date_dernier_controle_optique is not null
  and b.date_dernier_controle_hydraulique is not null
  and b.date_dernier_controle_hydraulique > b.date_dernier_controle_optique
returning b.item_id, b.date_dernier_controle_optique, b.date_dernier_controle_hydraulique;

commit;

-- Pour prévisualiser SANS écrire :
--   select item_id, date_dernier_controle_optique, date_dernier_controle_hydraulique
--   from public.item_bouteille
--   where date_dernier_controle_optique is not null
--     and date_dernier_controle_hydraulique is not null
--     and date_dernier_controle_hydraulique > date_dernier_controle_optique;
