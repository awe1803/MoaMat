-- =============================================================================
--  MoaMat — Préfixe le code_club des bouteilles, détendeurs et gilets avec la
--  clé unique (item.id).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Résultat : code_club = <item.id> || '_' || <ancien code_club>
--  (ex. item 42, code_club 'B12' -> '42_B12').
--
--  Périmètre : items de famille 'bouteille', 'detendeur' ou 'gilet'.
--  Un code_club NULL ou vide devient '<item.id>_NoCodeClub'
--  (ex. item 57 -> '57_NoCodeClub').
--
--  ATTENTION — effets de bord à connaître avant de lancer :
--    - Les prêts du miroir public.pret sont pontés par code_club
--      (public.item_est_pret_en_cours, db/model_item.sql) : après ce script,
--      pret.bouteille_code / detendeur_code / gilet_code ne correspondent plus
--      au nouveau code_club. Les prêts ouverts ne seront plus détectés tant que
--      le miroir n'est pas aligné.
--    - code_club_ambigu est recalculé par trigger (plus aucun doublon, puisque
--      l'id est unique) ; le motif « structurant » du code peut changer.
--    - Les anciennes valeurs sont conservées dans
--      public.sauvegarde_code_club_items (retour arrière en §4).
--
--  Idempotent : une ligne dont code_club commence déjà par '<id>_' n'est pas
--  retouchée. Un code d'origine qui commencerait naturellement par '<id>_'
--  serait donc aussi ignoré (cas improbable).
--  Le test de préfixe utilise starts_with() et non LIKE : '_' est un joker en
--  LIKE.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Sauvegarde des valeurs d'origine (une ligne déjà sauvegardée n'est jamais
--     écrasée).
-- -----------------------------------------------------------------------------

create table if not exists public.sauvegarde_code_club_items (
    item_id          bigint primary key,
    famille          text   not null,
    ancien_code_club text,  -- NULL = l'item n'avait pas de code_club
    sauvegarde_le    timestamptz not null default now()
);

alter table public.sauvegarde_code_club_items alter column ancien_code_club drop not null;
alter table public.sauvegarde_code_club_items enable row level security;

insert into public.sauvegarde_code_club_items (item_id, famille, ancien_code_club)
select i.id, i.famille, i.code_club
from public.item i
where i.famille in ('bouteille', 'detendeur', 'gilet')
  and not starts_with(coalesce(i.code_club, ''), i.id::text || '_')
on conflict (item_id) do nothing;

-- -----------------------------------------------------------------------------
--  2. Mise à jour
-- -----------------------------------------------------------------------------

update public.item i
set code_club = i.id::text || '_' || coalesce(nullif(btrim(i.code_club), ''), 'NoCodeClub')
where i.famille in ('bouteille', 'detendeur', 'gilet')
  and not starts_with(coalesce(i.code_club, ''), i.id::text || '_')
returning i.id, i.famille, i.code_club;

-- -----------------------------------------------------------------------------
--  3. Diagnostic (SELECT seul) : items ayant reçu '_NoCodeClub'.
-- -----------------------------------------------------------------------------

select i.id as item_id, i.famille, i.code_club, i.num_serie
from public.item i
where i.famille in ('bouteille', 'detendeur', 'gilet')
  and i.code_club = i.id::text || '_NoCodeClub'
order by i.famille, i.id;

-- -----------------------------------------------------------------------------
--  4. Retour arrière (à lancer à part, en commentaire par défaut) :
--
--    update public.item i
--    set code_club = s.ancien_code_club
--    from public.sauvegarde_code_club_items s
--    where s.item_id = i.id;
-- -----------------------------------------------------------------------------

commit;
