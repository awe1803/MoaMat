-- =============================================================================
--  MoaMat — Backfill item_detendeur.date_derniere_intervention depuis le
--  dernier contrôle connu de l'import Access, quand aucune intervention n'a
--  pu être rattachée.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/item_detendeur.sql (a besoin de public.item_detendeur).
--  Ré-exécutable sans erreur : ne touche que les lignes
--  date_derniere_intervention IS NULL, jamais une valeur déjà connue
--  (intervention saisie dans l'appli ou reprise depuis
--  detendeur_intervention — cf. db/detendeur_evenement.sql §3).
--
--  Contexte : item_detendeur.date_dernier_controle est une colonne copiée
--  TELLE QUELLE depuis le miroir Access par db/transform_item.sql (elle
--  survit donc à un rejeu de ce script, contrairement aux colonnes ajoutées
--  après coup comme date_derniere_intervention). Certains ensembles portent
--  une date de contrôle connue dans le miroir sans avoir de ligne
--  correspondante dans detendeur_intervention (aucune intervention Access
--  rattachée, ou ensemble entré en inventaire sans historique d'entretien) :
--  pour ceux-là, date_derniere_intervention reste NULL même après la reprise
--  de db/detendeur_evenement.sql, et l'échéance (public.detendeur_echeance)
--  reste donc NULL faute de point de départ, quelle que soit la périodicité
--  (y compris le défaut 12 mois posé par db/item_detendeur.sql).
--
--  Ce script pose, pour rattrapage, date_dernier_controle comme point de
--  départ du compteur : à défaut d'un historique d'intervention ENREGISTRÉ
--  mais avec un contrôle Access CONNU, c'est ce contrôle qui fait courir le
--  prochain délai d'entretien — même logique que
--  db/correction_greg/backfill_dernier_controle_mise_en_service.sql pour les
--  bouteilles (différence : ici la source est le dernier contrôle connu, pas
--  la mise en service, car date_dernier_controle est une donnée de contrôle
--  réel, pas une date d'achat).
--
--  Ce UPDATE passe par la colonne surveillée par
--  public.tg_item_detendeur_sync_echeance (db/item_detendeur.sql §5) :
--  l'échéance des ensembles concernés est donc recalculée automatiquement
--  dans la foulée (défaut 12 mois si aucune périodicité marque/modèle n'est
--  encore paramétrée).
-- =============================================================================

begin;

update public.item_detendeur d
set date_derniere_intervention = d.date_dernier_controle
where d.date_derniere_intervention is null
  and d.date_dernier_controle is not null
returning d.item_id, d.date_dernier_controle, d.date_derniere_intervention;

-- -----------------------------------------------------------------------------
--  Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_combles      bigint;
    v_sans_echeance bigint;
begin
    select count(*) into v_combles
        from public.item_detendeur
        where date_derniere_intervention is not null;

    select count(*) into v_sans_echeance
        from public.item i
        join public.item_detendeur d on d.item_id = i.id
        where i.actif and i.date_echeance is null;

    raise notice 'backfill_detendeur_derniere_intervention_controle : % ensemble(s) avec dernière intervention connue au total, % ensemble(s) actif(s) encore sans échéance.',
        v_combles, v_sans_echeance;
end $$;

commit;

-- Pour prévisualiser SANS écrire :
--   select item_id, date_dernier_controle, date_derniere_intervention
--   from public.item_detendeur
--   where date_derniere_intervention is null
--     and date_dernier_controle is not null;
