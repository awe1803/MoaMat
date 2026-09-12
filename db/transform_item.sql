-- =============================================================================
--  MoaMat — Reprise « miroir Access -> modèle Item ».
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql (et donc après schema / initial_load /
--  roles / permissions). AVANT db/rls.sql. Ré-exécutable : le script commence
--  par vider public.item / public.item_* / public.item_reject
--  (truncate ... restart identity cascade) puis recharge depuis les tables
--  miroir. Il ne touche jamais aux tables miroir elles-mêmes.
--
--  Ce que fait le transform
--  ------------------------
--   1. Sème public.lieu_section depuis ref_site + les colonnes « section »
--      texte des tables miroir.
--   2. Alimente public.lieu_mapping (clés seules, contenant_id NULL) avec les
--      valeurs de lieu en texte libre rencontrées. => liste d'arbitrage :
--      un admin complète contenant_id via /lieux, puis on REJOUE ce script.
--   3. Crée un public.item par ligne de matériel (familles vivantes ET
--      *_sortie_inventaire, ces dernières en actif = false / statut « reforme »).
--   4. Crée la ligne fille correspondante (item_bouteille, item_detendeur…),
--      en CONVERTISSANT les colonnes numériques stockées en texte dans le
--      miroir (constats A11/A22). Toute valeur non NULL non convertible part
--      VERBATIM dans public.item_reject (rien n'est perdu).
--   5. Recalcule public.item.code_club_ambigu.
--
--  Rejouabilité vs clés : public.item.id est GENERATED ALWAYS AS IDENTITY et
--  RESET à chaque exécution. Tant que l'appli n'est pas en service, aucun id
--  d'item n'est référencé ailleurs : le truncate/reload est sans risque, comme
--  db/initial_load.sql. Le couple (origine_table, origine_id) reste stable et
--  sert de clé de correspondance lors des rejeux.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  0. Helpers de conversion (laissés en place, inspectables). Renvoient NULL
--     sur toute entrée non convertible, sans lever d'erreur.
-- -----------------------------------------------------------------------------

create or replace function public.moamat_reprise_num(p text)
returns numeric
language plpgsql
immutable
set search_path = ''
as $$
declare
    v_raw  text := btrim(coalesce(p, ''));
    v_num  text;
begin
    if v_raw = '' then
        return null;
    end if;
    -- « 12 Li », « 14,3 », « 232 bar », « 2.5 kg » -> premier nombre rencontré.
    -- Pas de nombre exploitable => la regexp ne remplace rien : on le détecte.
    v_num := regexp_replace(v_raw, '^[^0-9+-]*([+-]?[0-9]+(?:[.,][0-9]+)?).*$', '\1');
    if v_num = v_raw and v_raw !~ '^[+-]?[0-9]+(?:[.,][0-9]+)?$' then
        return null;
    end if;
    return replace(v_num, ',', '.')::numeric;
exception when others then
    return null;
end $$;

comment on function public.moamat_reprise_num(text) is
    'Extrait le premier nombre d''une chaîne « sale » Access (« 12 Li » -> 12). NULL si rien d''exploitable.';

create or replace function public.moamat_reprise_int(p text)
returns integer
language sql
immutable
set search_path = ''
as $$
    select round(public.moamat_reprise_num(p))::integer
$$;

comment on function public.moamat_reprise_int(text) is
    'public.moamat_reprise_num() arrondi à l''entier.';

-- -----------------------------------------------------------------------------
--  1. Purge de la couche cible
-- -----------------------------------------------------------------------------

truncate table public.item_reject restart identity;
truncate table public.item restart identity cascade;   -- cascade -> item_*

-- Le drapeau code_club_ambigu est recalculé une seule fois en fin de script :
-- inutile de le refaire à chaque INSERT de famille.
alter table public.item disable trigger item_code_ambigu;

-- -----------------------------------------------------------------------------
--  2. Hiérarchie de lieux : sections + clés de mapping
-- -----------------------------------------------------------------------------

insert into public.lieu_section (libelle)
select distinct btrim(x)
from (
    select libelle from public.ref_site
    union all select section from public.detendeur
    union all select section from public.detendeur_sortie_inventaire
    union all select section from public.gilet
    union all select section from public.gilet_sortie_inventaire
    union all select section from public.petit_materiel
) s(x)
where nullif(btrim(x), '') is not null
on conflict (libelle) do nothing;

-- Clés de mapping (contenant_id laissé NULL = à arbitrer via /lieux).
insert into public.lieu_mapping (source_champ, source_valeur)
select src.champ, btrim(src.valeur)
from (
    select 'bouteille.site'                    as champ, site                 as valeur from public.bouteille
    union all select 'bouteille.local',             local                       from public.bouteille
    union all select 'bouteille_sortie.site',       site                        from public.bouteille_sortie_inventaire
    union all select 'detendeur.section',           section                     from public.detendeur
    union all select 'detendeur_sortie.section',    section                     from public.detendeur_sortie_inventaire
    union all select 'gilet.section',               section                     from public.gilet
    union all select 'gilet.emplacement_remarque',  emplacement_remarque        from public.gilet
    union all select 'gilet_sortie.section',        section                     from public.gilet_sortie_inventaire
    union all select 'petit_materiel.section',      section                     from public.petit_materiel
    union all select 'petit_materiel.local',        local                       from public.petit_materiel
    union all select 'piece_detachee.emplacement',  emplacement                 from public.piece_detachee
    union all select 'materiel_didactique.site',    rs.libelle
                 from public.materiel_didactique md
                 left join public.ref_site rs on rs.id = md.site_id
) src(champ, valeur)
where nullif(btrim(src.valeur), '') is not null
group by src.champ, btrim(src.valeur)
on conflict (source_champ, source_valeur) do nothing;

-- Résolution éventuelle (utile sur un REJEU une fois lieu_mapping complété).
create or replace function pg_temp.contenant_for(p_champ text, p_valeur text)
returns bigint
language sql
stable
as $$
    select lm.contenant_id
    from public.lieu_mapping lm
    where lm.source_champ = p_champ
      and lm.source_valeur = btrim(p_valeur)
$$;

-- -----------------------------------------------------------------------------
--  3. BOUTEILLES (+ bouteille_sortie_inventaire)
-- -----------------------------------------------------------------------------

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(b.code_club), ''),
    'bouteille',
    nullif(btrim(b.num_serie_fabricant), ''),
    nullif(btrim(b.marque), ''),
    nullif(btrim(b.modele), ''),
    b.date_mise_en_service,
    null,
    case
        when b.est_declassee   then 'retire_du_service'
        when b.a_requalifier   then 'en_attente_controle'
        when b.a_controler     then 'en_attente_controle'
        else 'en_stock'
    end,
    coalesce(pg_temp.contenant_for('bouteille.site', b.site),
             pg_temp.contenant_for('bouteille.local', b.local)),
    nullif(btrim(b.destination), ''),
    nullif(btrim(b.remarque), ''),
    b.date_echeance,
    true,
    'bouteille',
    b.id
from public.bouteille b;

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(b.code_club), ''),
    'bouteille',
    nullif(btrim(b.num_serie_fabricant), ''),
    nullif(btrim(b.marque), ''),
    nullif(btrim(b.modele), ''),
    b.date_mise_en_service,
    null,
    'retire_du_service',
    pg_temp.contenant_for('bouteille_sortie.site', b.site),
    null,
    nullif(btrim(b.remarque), ''),
    b.date_echeance,
    false,
    'bouteille_sortie_inventaire',
    b.id
from public.bouteille_sortie_inventaire b;

insert into public.item_bouteille (
    item_id, num_peint, num_robinet, filetage, double_sortie, sangles,
    volume_nominal_l, pression_service_bar, tare_kg, capacite_reelle_l,
    sortie_autorisee, controle_effectue, date_mise_en_service, autorite_declassement)
select
    it.id,
    nullif(btrim(b.num_peint), ''),
    nullif(btrim(b.num_robinet), ''),
    nullif(btrim(b.filetage), ''),
    b.double_sortie,
    nullif(btrim(b.sangles), ''),
    public.moamat_reprise_num(b.volume_nominal_l),
    public.moamat_reprise_int(b.pression_service_bar),
    public.moamat_reprise_num(b.tare_kg),
    public.moamat_reprise_num(b.capacite_reelle_l),
    b.sortie_autorisee,
    b.controle_effectue,
    b.date_mise_en_service,
    nullif(btrim(b.autorite_declassement), '')
from public.bouteille b
join public.item it
  on it.origine_table = 'bouteille' and it.origine_id = b.id;

insert into public.item_bouteille (
    item_id, num_peint, num_robinet, filetage, double_sortie, sangles,
    volume_nominal_l, pression_service_bar, tare_kg, capacite_reelle_l,
    sortie_autorisee, controle_effectue, date_mise_en_service, autorite_declassement)
select
    it.id,
    nullif(btrim(b.num_peint), ''),
    nullif(btrim(b.num_robinet), ''),
    nullif(btrim(b.filetage), ''),
    b.double_sortie,
    nullif(btrim(b.sangles), ''),
    public.moamat_reprise_num(b.volume_nominal_l),
    public.moamat_reprise_int(b.pression_service_bar),
    public.moamat_reprise_num(b.tare_kg),
    public.moamat_reprise_num(b.capacite_reelle_l),
    b.sortie_autorisee,
    b.controle_effectue,
    b.date_mise_en_service,
    nullif(btrim(b.autorite_declassement), '')
from public.bouteille_sortie_inventaire b
join public.item it
  on it.origine_table = 'bouteille_sortie_inventaire' and it.origine_id = b.id;

-- Rejets de conversion (constats A11/A22) — colonnes « mesures » du miroir.
insert into public.item_reject (origine_table, origine_id, colonne, valeur_brute, raison)
select t.tbl, t.id, t.col, t.val, 'valeur non convertible en numérique'
from (
    select 'bouteille'::text as tbl, id, 'volume_nominal_l'::text as col, volume_nominal_l as val from public.bouteille
    union all select 'bouteille', id, 'pression_service_bar', pression_service_bar from public.bouteille
    union all select 'bouteille', id, 'tare_kg',              tare_kg              from public.bouteille
    union all select 'bouteille', id, 'capacite_reelle_l',    capacite_reelle_l    from public.bouteille
    union all select 'bouteille_sortie_inventaire', id, 'volume_nominal_l',     volume_nominal_l     from public.bouteille_sortie_inventaire
    union all select 'bouteille_sortie_inventaire', id, 'pression_service_bar', pression_service_bar from public.bouteille_sortie_inventaire
    union all select 'bouteille_sortie_inventaire', id, 'tare_kg',             tare_kg              from public.bouteille_sortie_inventaire
    union all select 'bouteille_sortie_inventaire', id, 'capacite_reelle_l',   capacite_reelle_l    from public.bouteille_sortie_inventaire
) t(tbl, id, col, val)
where nullif(btrim(t.val), '') is not null
  and public.moamat_reprise_num(t.val) is null;

-- -----------------------------------------------------------------------------
--  4. DÉTENDEURS (+ detendeur_sortie_inventaire)
-- -----------------------------------------------------------------------------

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(d.code_club), ''),
    'detendeur',
    nullif(btrim(d.num_serie_premier_etage), ''),
    nullif(btrim(d.marque), ''),
    nullif(btrim(d.modele_premier_etage), ''),
    d.date_achat,
    null,
    case when d.est_declasse then 'retire_du_service' else 'en_stock' end,
    pg_temp.contenant_for('detendeur.section', d.section),
    null,
    nullif(btrim(d.remarque), ''),
    null,
    true,
    'detendeur',
    d.id
from public.detendeur d;

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(d.code_club), ''),
    'detendeur',
    nullif(btrim(d.num_serie_premier_etage), ''),
    nullif(btrim(d.marque), ''),
    nullif(btrim(d.modele_premier_etage), ''),
    d.date_achat,
    null,
    'retire_du_service',
    pg_temp.contenant_for('detendeur_sortie.section', d.section),
    null,
    nullif(btrim(d.remarque), ''),
    null,
    false,
    'detendeur_sortie_inventaire',
    d.id
from public.detendeur_sortie_inventaire d;

insert into public.item_detendeur (
    item_id, num_ordre, type_connexion, a_premier_etage, modele_premier_etage,
    num_serie_premier_etage, a_second_etage, modele_second_etage,
    num_serie_second_etage, a_octopus, modele_octopus, num_serie_octopus,
    a_inflateur, a_manometre, sortie_autorisee, reserve_enfants,
    controle_effectue, date_dernier_controle)
select
    it.id,
    nullif(btrim(d.num_ordre), ''),
    nullif(btrim(d.type_connexion), ''),
    d.a_premier_etage,
    nullif(btrim(d.modele_premier_etage), ''),
    nullif(btrim(d.num_serie_premier_etage), ''),
    d.a_second_etage,
    nullif(btrim(d.modele_second_etage), ''),
    nullif(btrim(d.num_serie_second_etage), ''),
    d.a_octopus,
    nullif(btrim(d.modele_octopus), ''),
    nullif(btrim(d.num_serie_octopus), ''),
    d.a_inflateur,
    d.a_manometre,
    d.sortie_autorisee,
    d.reserve_enfants,
    d.controle_effectue,
    d.date_dernier_controle
from public.detendeur d
join public.item it on it.origine_table = 'detendeur' and it.origine_id = d.id;

insert into public.item_detendeur (
    item_id, num_ordre, type_connexion, a_premier_etage, modele_premier_etage,
    num_serie_premier_etage, a_second_etage, modele_second_etage,
    num_serie_second_etage, a_octopus, modele_octopus, num_serie_octopus,
    a_inflateur, a_manometre, sortie_autorisee, reserve_enfants,
    controle_effectue, date_dernier_controle)
select
    it.id,
    nullif(btrim(d.num_ordre), ''),
    nullif(btrim(d.type_connexion), ''),
    d.a_premier_etage,
    nullif(btrim(d.modele_premier_etage), ''),
    nullif(btrim(d.num_serie_premier_etage), ''),
    d.a_second_etage,
    nullif(btrim(d.modele_second_etage), ''),
    nullif(btrim(d.num_serie_second_etage), ''),
    d.a_octopus,
    nullif(btrim(d.modele_octopus), ''),
    nullif(btrim(d.num_serie_octopus), ''),
    d.a_inflateur,
    d.a_manometre,
    d.sortie_autorisee,
    d.reserve_enfants,
    d.controle_effectue,
    d.date_dernier_controle
from public.detendeur_sortie_inventaire d
join public.item it on it.origine_table = 'detendeur_sortie_inventaire' and it.origine_id = d.id;

-- -----------------------------------------------------------------------------
--  5. GILETS (+ gilet_sortie_inventaire)
-- -----------------------------------------------------------------------------

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(g.code_club), ''),
    'gilet',
    nullif(btrim(g.num_serie_fabricant), ''),
    nullif(btrim(g.marque), ''),
    null,
    g.date_achat,
    null,
    case
        when g.est_declasse then 'retire_du_service'
        when g.est_manquant then 'perdu'
        else 'en_stock'
    end,
    coalesce(pg_temp.contenant_for('gilet.section', g.section),
             pg_temp.contenant_for('gilet.emplacement_remarque', g.emplacement_remarque)),
    null,
    nullif(btrim(g.emplacement_remarque), ''),
    null,
    true,
    'gilet',
    g.id
from public.gilet g;

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(g.code_club), ''),
    'gilet',
    nullif(btrim(g.num_serie_fabricant), ''),
    nullif(btrim(g.marque), ''),
    null,
    g.date_achat,
    null,
    'retire_du_service',
    pg_temp.contenant_for('gilet_sortie.section', g.section),
    null,
    nullif(btrim(g.emplacement_remarque), ''),
    null,
    false,
    'gilet_sortie_inventaire',
    g.id
from public.gilet_sortie_inventaire g;

insert into public.item_gilet (
    item_id, couleur, taille, marquage, sortie_autorisee, emplacement_remarque)
select it.id,
    nullif(btrim(g.couleur), ''),
    nullif(btrim(g.taille), ''),
    nullif(btrim(g.marquage), ''),
    g.sortie_autorisee,
    nullif(btrim(g.emplacement_remarque), '')
from public.gilet g
join public.item it on it.origine_table = 'gilet' and it.origine_id = g.id;

insert into public.item_gilet (
    item_id, couleur, taille, marquage, sortie_autorisee, emplacement_remarque)
select it.id,
    nullif(btrim(g.couleur), ''),
    nullif(btrim(g.taille), ''),
    null,
    g.sortie_autorisee,
    nullif(btrim(g.emplacement_remarque), '')
from public.gilet_sortie_inventaire g
join public.item it on it.origine_table = 'gilet_sortie_inventaire' and it.origine_id = g.id;

-- -----------------------------------------------------------------------------
--  6. PETIT MATÉRIEL
-- -----------------------------------------------------------------------------

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(p.code_club), ''),
    'petit_materiel',
    null,
    nullif(btrim(p.marque), ''),
    nullif(btrim(p.modele), ''),
    p.date_achat,
    p.prix_tvac_eur,
    case when p.est_declasse then 'retire_du_service' else 'en_stock' end,
    coalesce(pg_temp.contenant_for('petit_materiel.local', p.local),
             pg_temp.contenant_for('petit_materiel.section', p.section)),
    null,
    null,
    null,
    true,
    'petit_materiel',
    p.id
from public.petit_materiel p;

insert into public.item_petit_materiel (
    item_id, sous_famille, couleur, taille, pointure, quantite)
select it.id,
    nullif(btrim(p.famille), ''),
    nullif(btrim(p.couleur), ''),
    nullif(btrim(p.taille), ''),
    nullif(btrim(p.pointure), ''),
    p.quantite
from public.petit_materiel p
join public.item it on it.origine_table = 'petit_materiel' and it.origine_id = p.id;

-- -----------------------------------------------------------------------------
--  7. MATÉRIEL DIDACTIQUE
-- -----------------------------------------------------------------------------

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    nullif(btrim(m.code_club), ''),
    'materiel_didactique',
    nullif(btrim(m.num_serie_fabricant), ''),
    null,
    nullif(btrim(m.modele), ''),
    m.date_achat,
    null,
    case when m.est_declasse then 'retire_du_service' else 'en_stock' end,
    pg_temp.contenant_for('materiel_didactique.site', rs.libelle),
    null,
    null,
    null,
    true,
    'materiel_didactique',
    m.id
from public.materiel_didactique m
left join public.ref_site rs on rs.id = m.site_id;

insert into public.item_materiel_didactique (item_id, designation, quantite)
select it.id,
    nullif(btrim(m.designation), ''),
    m.quantite
from public.materiel_didactique m
join public.item it on it.origine_table = 'materiel_didactique' and it.origine_id = m.id;

-- -----------------------------------------------------------------------------
--  8. PIÈCES DÉTACHÉES (pas de code_club dans le miroir)
-- -----------------------------------------------------------------------------

insert into public.item (
    code_club, famille, num_serie, marque, modele, date_acquisition, prix_eur,
    statut_code, lieu_contenant_id, destination, remarque, date_echeance,
    actif, origine_table, origine_id)
select
    null,
    'piece_detachee',
    nullif(btrim(p.reference_fournisseur), ''),
    nullif(btrim(p.fournisseur), ''),
    null,
    null,
    p.prix_unitaire_eur,
    'en_stock',
    pg_temp.contenant_for('piece_detachee.emplacement', p.emplacement),
    nullif(btrim(p.destination), ''),
    nullif(btrim(p.utilisation), ''),
    null,
    true,
    'piece_detachee',
    p.id
from public.piece_detachee p;

insert into public.item_piece_detachee (
    item_id, reference_fournisseur, dimension, colisage, conditionnement,
    stock, prix_unitaire_eur, fournisseur, emplacement, utilisation, url_commande)
select it.id,
    nullif(btrim(p.reference_fournisseur), ''),
    nullif(btrim(p.dimension), ''),
    nullif(btrim(p.colisage), ''),
    null,
    p.stock,
    p.prix_unitaire_eur,
    nullif(btrim(p.fournisseur), ''),
    nullif(btrim(p.emplacement), ''),
    nullif(btrim(p.utilisation), ''),
    nullif(btrim(p.url_commande), '')
from public.piece_detachee p
join public.item it on it.origine_table = 'piece_detachee' and it.origine_id = p.id;

-- -----------------------------------------------------------------------------
--  9. Drapeau « code club ambigu »
-- -----------------------------------------------------------------------------

alter table public.item enable trigger item_code_ambigu;
select public.item_refresh_code_ambigu();

-- -----------------------------------------------------------------------------
--  10. Contrôles de volume (NOTICE, non bloquants)
-- -----------------------------------------------------------------------------

do $$
declare
    v_items   bigint;
    v_orph    bigint;
    v_rej     bigint;
    v_ambigu  bigint;
    v_map_todo bigint;
begin
    select count(*) into v_items from public.item;
    select count(*) into v_orph
    from public.item i
    where not exists (select 1 from public.item_bouteille           b where b.item_id = i.id)
      and not exists (select 1 from public.item_detendeur           d where d.item_id = i.id)
      and not exists (select 1 from public.item_gilet               g where g.item_id = i.id)
      and not exists (select 1 from public.item_petit_materiel      p where p.item_id = i.id)
      and not exists (select 1 from public.item_materiel_didactique m where m.item_id = i.id)
      and not exists (select 1 from public.item_piece_detachee      q where q.item_id = i.id);
    select count(*) into v_rej    from public.item_reject;
    select count(*) into v_ambigu from public.item where code_club_ambigu;
    select count(*) into v_map_todo from public.lieu_mapping where contenant_id is null;

    raise notice 'transform_item : % item(s) créé(s), % sans ligne fille, % rejet(s) de conversion, % code(s) ambigu(s), % mapping(s) de lieu à arbitrer.',
        v_items, v_orph, v_rej, v_ambigu, v_map_todo;
end $$;

commit;
