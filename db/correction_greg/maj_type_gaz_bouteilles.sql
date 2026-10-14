-- =============================================================================
--  MoaMat — Reclassification du type de gaz (item_bouteille.famille) depuis le
--  miroir Access, règle affinée (code_club pour distinguer bloc_tampon).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Contexte : db/correction_greg/backfill_classification_plongee.sql ne sait
--  distinguer que 'PLONGEE' (brut) du reste, et classe tout 'PLONGEE' en
--  famille = 'plongee'. Or le miroir Access porte en réalité trois valeurs
--  utiles dans bouteille.famille / bouteille_sortie_inventaire.famille :
--    - 'SECOURS'                      -> o2_secourisme
--    - 'DECO'                         -> deco_o2
--    - 'PLONGEE'                      -> air : soit une bouteille de plongée
--      normale, soit un bloc tampon (même composant). Les deux ne se
--      distinguent PAS par famille mais par item.code_club, qui contient le
--      mot « TAMPON » pour les blocs tampons (convention club).
--  Les 6 profils réglementaires n'ont que 2 familles non couvertes par ces 3
--  valeurs brutes (deco_o2 / o2_secourisme / bloc_tampon) : les 3 variantes de
--  matière plongée (acier/alu/carbone) restent elles indiscernables
--  automatiquement — matiere garde son défaut 'acier' (cf.
--  backfill_matiere_acier.sql), à corriger manuellement si besoin via la
--  fiche bouteille pour les bouteilles alu/carbone connues.
--
--  Idempotent et REJOUABLE après backfill_classification_plongee.sql : met à
--  jour famille même si déjà renseignée (ex. 'plongee' posé par erreur sur un
--  bloc tampon par le backfill générique), dès lors que la règle ci-dessus
--  donne un résultat différent. Ne touche JAMAIS une ligne dont le miroir ne
--  donne aucune des 3 valeurs reconnues (famille_brute NULL ou autre) :
--  listées en diagnostic (§2) pour arbitrage manuel.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Reclassification
-- -----------------------------------------------------------------------------

with source as (
    select
        i.id                                     as item_id,
        i.code_club,
        upper(btrim(coalesce(mb.famille, ms.famille))) as famille_brute
    from public.item i
    left join public.bouteille mb
        on i.origine_table = 'bouteille' and mb.id = i.origine_id
    left join public.bouteille_sortie_inventaire ms
        on i.origine_table = 'bouteille_sortie_inventaire' and ms.id = i.origine_id
    where i.origine_table in ('bouteille', 'bouteille_sortie_inventaire')
),
classification as (
    select
        item_id,
        case
            when famille_brute = 'SECOURS' then 'o2_secourisme'
            when famille_brute = 'DECO'    then 'deco_o2'
            when famille_brute = 'PLONGEE' and upper(coalesce(code_club, '')) like '%TAMPON%'
                then 'bloc_tampon'
            when famille_brute = 'PLONGEE' then 'plongee'
            else null
        end as famille_cible
    from source
)
update public.item_bouteille b
set famille = c.famille_cible
from classification c
where b.item_id = c.item_id
  and c.famille_cible is not null
  and b.famille is distinct from c.famille_cible
returning b.item_id, b.famille;

-- Matière par défaut pour toute ligne encore NULL (ex. lignes qui viennent
-- de recevoir une famille ci-dessus pour la première fois). Ne régresse
-- jamais une matière déjà saisie (alu / carbone compris).
update public.item_bouteille b
set matiere = 'acier'
where b.matiere is null;

-- -----------------------------------------------------------------------------
--  2. Diagnostic (SELECT seul) : lignes dont le miroir ne donne aucune des 3
--     valeurs reconnues (SECOURS / DECO / PLONGEE) — à arbitrer manuellement.
-- -----------------------------------------------------------------------------

select
    i.id   as item_id,
    i.code_club,
    i.num_serie,
    coalesce(mb.famille, ms.famille) as famille_brute_miroir,
    b.famille                        as famille_actuelle
from public.item i
left join public.bouteille mb
    on i.origine_table = 'bouteille' and mb.id = i.origine_id
left join public.bouteille_sortie_inventaire ms
    on i.origine_table = 'bouteille_sortie_inventaire' and ms.id = i.origine_id
join public.item_bouteille b on b.item_id = i.id
where i.origine_table in ('bouteille', 'bouteille_sortie_inventaire')
  and upper(btrim(coalesce(mb.famille, ms.famille))) not in ('SECOURS', 'DECO', 'PLONGEE')
order by i.code_club;

-- -----------------------------------------------------------------------------
--  3. Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_plongee  bigint;
    v_deco     bigint;
    v_secours  bigint;
    v_tampon   bigint;
    v_inconnu  bigint;
begin
    select count(*) into v_plongee  from public.item_bouteille where famille = 'plongee';
    select count(*) into v_deco     from public.item_bouteille where famille = 'deco_o2';
    select count(*) into v_secours  from public.item_bouteille where famille = 'o2_secourisme';
    select count(*) into v_tampon   from public.item_bouteille where famille = 'bloc_tampon';
    select count(*) into v_inconnu  from public.item_bouteille where famille is null;

    raise notice 'maj_type_gaz_bouteilles : % plongee, % deco_o2, % o2_secourisme, % bloc_tampon, % encore sans famille.',
        v_plongee, v_deco, v_secours, v_tampon, v_inconnu;
end $$;

commit;
