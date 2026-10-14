-- =============================================================================
--  MoaMat — Tests de la chronologie détendeur (db/detendeur_evenement.sql) et
--  du moteur d'échéance par marque/modèle (db/item_detendeur.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/ref_marque.sql, db/transform_item.sql, db/rls.sql,
--      db/audit.sql, db/item_etat.sql, db/item_detendeur.sql,
--      db/detendeur_evenement.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/detendeur_evenement_tests.sql
--
--  Couverture :
--    1. Reprise : 115 = repris + rejetés, écart 0 ; rejouer la reprise ne
--       duplique rien ; normalisation des marques appliquée (constat A22) ;
--       fusion actif/déclassé par n° de série (id 62 fusionné dans l'id 79,
--       sans doublon) ; 100 items détendeur au total (86 + 15 - 1 fusionné) ;
--       date_derniere_intervention rattachée depuis l'historique (plus
--       proche connue), jamais en arrière.
--    2. Saisie d'intervention : les 5 points de contrôle sont obligatoires
--       (ok/non_ok/sans_objet) ; 'no_ctl' refusé ; met à jour la dernière
--       intervention sans jamais reculer ; échéance recalculée depuis le
--       référentiel marque/modèle ; refus pour un rôle "lecture" ; refus sur
--       un ensemble désactivé ou en statut terminal.
--    3. Aucune écriture directe (pas de policy insert).
-- =============================================================================

begin;

set client_min_messages to notice;

create schema moamat_test_detendeur;
grant usage on schema moamat_test_detendeur to authenticated, anon;

create function moamat_test_detendeur.expect(p_label text, p_got boolean)
returns void language plpgsql as $$
begin
    if p_got then
        raise notice 'PASS: %', p_label;
    else
        raise exception 'FAIL: %', p_label;
    end if;
end $$;

create function moamat_test_detendeur.expect_raises(p_label text, p_sql text)
returns void language plpgsql as $$
begin
    execute p_sql;
    raise exception 'FAIL: % — aucune erreur levee', p_label;
exception
    when others then
        if sqlerrm like 'FAIL:%' then
            raise;
        end if;
        raise notice 'PASS: % (erreur levee: %)', p_label, sqlerrm;
end $$;

grant execute on all functions in schema moamat_test_detendeur to authenticated, anon;

-- -----------------------------------------------------------------------------
--  1. Reprise (en rôle non restreint, avant tout « set role »)
-- -----------------------------------------------------------------------------

select moamat_test_detendeur.expect(
    'reprise — 115 interventions source',
    (select count(*) from public.detendeur_intervention) = 115);

select moamat_test_detendeur.expect(
    'reprise — repris + rejetes = source (ecart 0)',
    (select count(*) from public.detendeur_evenement where origine_id is not null)
  + (select count(distinct origine_id) from public.item_reject where origine_table = 'detendeur_intervention')
  = (select count(*) from public.detendeur_intervention));

select moamat_test_detendeur.expect(
    'reprise — NO CTL historique conserve (dérive constatée, 48 lignes)',
    (select count(*) from public.detendeur_evenement
      where ctrl_manometre = 'no_ctl' and ctrl_detendeur = 'no_ctl' and ctrl_octopus = 'no_ctl'
        and ctrl_inflateur = 'no_ctl' and ctrl_visuel_flexibles = 'no_ctl') = 48);

-- Fusion actif/déclassé par n° de série : detendeur_sortie_inventaire#62
-- (Fléron, MF7) partage le n° de série 80105-00013 avec detendeur#79
-- (Seraing, EFNE) : un seul item, pas de doublon.
select moamat_test_detendeur.expect(
    'fusion — detendeur_sortie_inventaire#62 n''ouvre pas d''item',
    not exists (select 1 from public.item where origine_table = 'detendeur_sortie_inventaire' and origine_id = 62));

select moamat_test_detendeur.expect(
    'fusion — l''item de detendeur#79 est bascule retire_du_service et porte la remarque du fusionne',
    exists (select 1 from public.item
             where origine_table = 'detendeur' and origine_id = 79
               and statut_code = 'retire_du_service'
               and remarque like '%Fusionné avec détendeur déclassé #62%'));

select moamat_test_detendeur.expect(
    'reprise — 100 items detendeur au total (86 + 15 - 1 fusionne)',
    (select count(*) from public.item where origine_table in ('detendeur', 'detendeur_sortie_inventaire')) = 100);

-- Normalisation des marques (constat A22) : plus aucune variante de casse.
select moamat_test_detendeur.expect(
    'normalisation marque — une seule graphie par marque (Scubapro, Mares, Aqualung, La Spirotechnique)',
    not exists (
        select 1 from public.item
        where famille = 'detendeur' and marque is not null
        group by upper(marque)
        having count(distinct marque) > 1));

select moamat_test_detendeur.expect(
    'normalisation marque — SCUBAPRO / scubapro normalises en Scubapro',
    exists (select 1 from public.item where famille = 'detendeur' and marque = 'Scubapro')
    and not exists (select 1 from public.item where famille = 'detendeur' and marque in ('SCUBAPRO', 'scubapro')));

-- date_derniere_intervention rattachée depuis l'historique (plus récente
-- connue), jamais depuis la date d'achat (Q3.2).
select moamat_test_detendeur.expect(
    'reprise — date_derniere_intervention rattachee depuis l''historique pour au moins un detendeur',
    exists (select 1 from public.item_detendeur where date_derniere_intervention is not null));

-- Idempotence : rejouer la reprise (ré-exécution du fichier) ne duplique rien.
do $$
declare
    v_avant bigint;
begin
    select count(*) into v_avant from public.detendeur_evenement;
    insert into public.detendeur_evenement
        (item_id, type, date_evenement, ctrl_manometre, ctrl_detendeur, ctrl_octopus, ctrl_inflateur, ctrl_visuel_flexibles, origine_id)
    select e.item_id, e.type, e.date_evenement, e.ctrl_manometre, e.ctrl_detendeur, e.ctrl_octopus, e.ctrl_inflateur, e.ctrl_visuel_flexibles, e.origine_id
    from public.detendeur_evenement e
    where e.origine_id is not null
    on conflict (origine_id) do nothing;
    if (select count(*) from public.detendeur_evenement) <> v_avant then
        raise exception 'FAIL: reprise non idempotente';
    end if;
    raise notice 'PASS: reprise — idempotente (origine_id unique)';
end $$;

-- -----------------------------------------------------------------------------
--  Fixture : un compte "gestion" et un compte "lecture", deux détendeurs du
--  même profil marque/modèle, avec une périodicité de 12 mois paramétrée.
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f1',
     'authenticated', 'authenticated', 'detendeur-gestion@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f2',
     'authenticated', 'authenticated', 'detendeur-lecture@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000f1';
update public.utilisateur_role set role = 'lecture' where user_id = '00000000-0000-0000-0000-0000000000f2';

insert into public.ref_periodicite_detendeur (marque, modele, periodicite_mois, date_effet)
values ('TestMarque', 'TestModele', 12, date '2020-01-01');

insert into public.item (id, famille, marque, modele, statut_code, actif)
overriding system value
values (999400001, 'detendeur', 'TestMarque', 'TestModele', 'en_stock', true),
       (999400002, 'detendeur', 'TestMarque', 'TestModele', 'en_stock', true),
       (999400003, 'detendeur', 'TestMarque', 'TestModele', 'retire_du_service', false);

insert into public.item_detendeur (item_id, date_derniere_intervention)
values (999400001, date '2024-06-10'),
       (999400002, date '2024-06-10'),
       (999400003, date '2024-06-10');

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f1', 'email', 'detendeur-gestion@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

-- =============================================================================
--  2. Saisie d'intervention
-- =============================================================================

select public.enregistrer_intervention_detendeur(
    999400001, date '2025-06-10', 'ok', 'ok', 'sans_objet', 'ok', 'ok', 'Apragaz', 0, 'RAS', null);

select moamat_test_detendeur.expect(
    'intervention — evenement historise avec les 5 points',
    exists (select 1 from public.detendeur_evenement
            where item_id = 999400001 and ctrl_manometre = 'ok' and ctrl_octopus = 'sans_objet'));

select moamat_test_detendeur.expect(
    'intervention — derniere intervention mise a jour',
    (select date_derniere_intervention from public.item_detendeur where item_id = 999400001) = date '2025-06-10');

select moamat_test_detendeur.expect(
    'intervention — echeance recalculee depuis le referentiel marque/modele (+ 12 mois)',
    (select date_echeance from public.item where id = 999400001) = date '2026-06-10');

-- Date antérieure à la dernière intervention connue : historisée, mais le compteur ne recule pas.
select public.enregistrer_intervention_detendeur(
    999400001, date '2023-01-01', 'ok', 'ok', 'ok', 'ok', 'ok', null, null, 'saisie tardive', null);

select moamat_test_detendeur.expect(
    'intervention anterieure — compteur jamais recule',
    (select date_derniere_intervention from public.item_detendeur where item_id = 999400001) = date '2025-06-10');

select moamat_test_detendeur.expect_raises('intervention — manometre obligatoire',
    $q$ select public.enregistrer_intervention_detendeur(999400002, date '2025-06-10', null, 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);
select moamat_test_detendeur.expect_raises('intervention — no_ctl refuse (ne reproduit pas la derive historique)',
    $q$ select public.enregistrer_intervention_detendeur(999400002, date '2025-06-10', 'no_ctl', 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);
select moamat_test_detendeur.expect_raises('intervention — date obligatoire',
    $q$ select public.enregistrer_intervention_detendeur(999400002, null, 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);
select moamat_test_detendeur.expect_raises('intervention — date future refusee',
    $q$ select public.enregistrer_intervention_detendeur(999400002, current_date + 1, 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);
select moamat_test_detendeur.expect_raises('intervention — cout negatif refuse',
    $q$ select public.enregistrer_intervention_detendeur(999400002, date '2025-06-10', 'ok', 'ok', 'ok', 'ok', 'ok', null, -1, null, null) $q$);
select moamat_test_detendeur.expect_raises('intervention — detendeur inconnu refuse',
    $q$ select public.enregistrer_intervention_detendeur(999499999, date '2025-06-10', 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);
select moamat_test_detendeur.expect_raises('intervention — ensemble desactive/terminal refuse',
    $q$ select public.enregistrer_intervention_detendeur(999400003, date '2025-06-10', 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);

-- Idempotence : même request_id = un seul événement, compteur et audit intacts.
do $$
declare
    v_a bigint;
    v_b bigint;
    v_req uuid := '00000000-0000-0000-0000-00000000bbbb';
begin
    v_a := public.enregistrer_intervention_detendeur(
        999400002, date '2025-07-01', 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, v_req);
    v_b := public.enregistrer_intervention_detendeur(
        999400002, date '2025-07-01', 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, v_req);
    if v_a is distinct from v_b then raise exception 'FAIL: rejeu — ids differents'; end if;
    if (select count(*) from public.detendeur_evenement where request_id = v_req) <> 1 then
        raise exception 'FAIL: rejeu — doublon cree';
    end if;
    raise notice 'PASS: intervention — rejeu du meme request_id = un seul evenement';
end $$;

-- =============================================================================
--  3. Pas d'écriture directe ; droits
-- =============================================================================

select moamat_test_detendeur.expect_raises('ecriture directe — insert refuse (aucune policy)',
    $q$ insert into public.detendeur_evenement (item_id, type, date_evenement, ctrl_manometre, ctrl_detendeur, ctrl_octopus, ctrl_inflateur, ctrl_visuel_flexibles)
        values (999400001, 'intervention', current_date, 'ok', 'ok', 'ok', 'ok', 'ok') $q$);

reset role;
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f2', 'email', 'detendeur-lecture@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test_detendeur.expect('lecture — voit la chronologie',
    exists (select 1 from public.detendeur_evenement where item_id = 999400001));
select moamat_test_detendeur.expect_raises('lecture — intervention refusee',
    $q$ select public.enregistrer_intervention_detendeur(999400001, date '2025-06-12', 'ok', 'ok', 'ok', 'ok', 'ok', null, null, null, null) $q$);

reset role;

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
