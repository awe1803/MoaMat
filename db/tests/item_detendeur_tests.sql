-- =============================================================================
--  MoaMat — Tests du moteur métier Ensembles détendeur
--  (db/item_detendeur.sql, db/detendeur_evenement.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--  Rien n'est conservé (comptes de test, items de test, référentiels insérés).
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/transform_item.sql, db/rls.sql, db/audit.sql,
--      db/item_etat.sql, db/item_detendeur.sql, db/detendeur_evenement.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/item_detendeur_tests.sql
--
--  Couverture :
--    1. Moteur d'échéance : dernière intervention + périodicité en vigueur ;
--       NULL si l'une des deux manque (jamais une date inventée) ; résolution
--       insensible à la casse/aux espaces ; règle en vigueur à la date de
--       l'intervention, pas à aujourd'hui.
--    2. Synchronisation de item.date_echeance : depuis item_detendeur
--       (date_derniere_intervention) ET depuis item (marque/modele) ; remise
--       à NULL à la suppression de item_detendeur.
--    3. Référentiel administrable et daté : INSERT admin OK, UPDATE/DELETE
--       refusés même pour un admin (historisation append-only).
--    4. public.enregistrer_intervention_detendeur() : met à jour le compteur
--       sans jamais le faire reculer, idempotent sur p_request_id, refuse une
--       date future / un coût négatif / un ensemble désactivé ou terminal.
-- =============================================================================

begin;

set client_min_messages to notice;

-- -----------------------------------------------------------------------------
--  Outillage d'assertion (même gabarit que db/tests/item_bouteille_tests.sql)
-- -----------------------------------------------------------------------------

create schema moamat_test_detendeurs;
grant usage on schema moamat_test_detendeurs to authenticated, anon;

create function moamat_test_detendeurs.expect(p_label text, p_got boolean)
returns void language plpgsql as $$
begin
    if p_got then
        raise notice 'PASS: %', p_label;
    else
        raise exception 'FAIL: %', p_label;
    end if;
end $$;

create function moamat_test_detendeurs.expect_raises(p_label text, p_sql text)
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

create function moamat_test_detendeurs.expect_write_denied(p_label text, p_sql text)
returns void language plpgsql as $$
declare
    v_n bigint;
begin
    execute p_sql;
    get diagnostics v_n = row_count;
    if v_n = 0 then
        raise notice 'PASS: % (0 ligne affectee)', p_label;
    else
        raise exception 'FAIL: % — % ligne(s) affectee(s), 0 attendu', p_label, v_n;
    end if;
exception
    when insufficient_privilege then
        raise notice 'PASS: % (refuse RLS: %)', p_label, sqlerrm;
end $$;

grant execute on all functions in schema moamat_test_detendeurs to authenticated, anon;

-- -----------------------------------------------------------------------------
--  Référentiel de périodicité — un seul profil ("Scubapro" / "MK25") avant
--  toute insertion d'item, pour pouvoir calculer une échéance dès le départ.
-- -----------------------------------------------------------------------------

insert into public.ref_periodicite_detendeur (marque, modele, periodicite_mois, date_effet)
values ('Scubapro', 'MK25', 24, date '2020-01-01');

-- -----------------------------------------------------------------------------
--  1. Moteur de calcul pur
-- -----------------------------------------------------------------------------

select moamat_test_detendeurs.expect(
    'echeance — derniere intervention + periodicite en vigueur',
    public.detendeur_echeance('Scubapro', 'MK25', date '2024-01-15') = date '2026-01-15');

select moamat_test_detendeurs.expect(
    'echeance — aucune intervention connue => NULL (jamais une date inventee)',
    public.detendeur_echeance('Scubapro', 'MK25', null) is null);

select moamat_test_detendeurs.expect(
    'echeance — profil sans periodicite paramettree => NULL',
    public.detendeur_echeance('Marque Inconnue', 'Modele Inconnu', date '2024-01-15') is null);

select moamat_test_detendeurs.expect(
    'periodicite — resolution insensible a la casse et aux espaces superflus',
    public.detendeur_periodicite_mois('  scubapro ', 'MK25', date '2024-01-15') = 24);

-- Historisation : une nouvelle valeur datée dans le futur ne doit pas changer
-- l'échéance d'une intervention déjà passée sous l'ancienne règle.
insert into public.ref_periodicite_detendeur (marque, modele, periodicite_mois, date_effet)
values ('Scubapro', 'MK25', 36, current_date + 365);

select moamat_test_detendeurs.expect(
    'periodicite — regle en vigueur a la date de l''intervention, pas celle d''aujourd''hui',
    public.detendeur_echeance('Scubapro', 'MK25', date '2024-01-15') = date '2026-01-15');

-- -----------------------------------------------------------------------------
--  2. Synchronisation de item.date_echeance
--     (jeu de données créé en tant que "postgres", RLS contournée)
-- -----------------------------------------------------------------------------

insert into public.item (id, famille, marque, modele, statut_code, actif)
overriding system value
values (999300001, 'detendeur', 'Scubapro', 'MK25', 'en_stock', true);

insert into public.item_detendeur (item_id, a_premier_etage, a_second_etage)
values (999300001, true, true);

select moamat_test_detendeurs.expect(
    'sync — item.date_echeance reste NULL tant qu''aucune intervention n''est connue',
    (select date_echeance from public.item where id = 999300001) is null);

update public.item_detendeur
set date_derniere_intervention = date '2024-01-15'
where item_id = 999300001;

select moamat_test_detendeurs.expect(
    'sync — item.date_echeance reprend l''echeance calculee apres une intervention',
    (select date_echeance from public.item where id = 999300001) = date '2026-01-15');

-- Changement de marque/modele (colonnes génériques sur item) : l'échéance
-- doit être recalculée pour le NOUVEAU profil, sans qu'aucune UPDATE
-- explicite ne touche date_echeance.
select moamat_test_detendeurs.expect(
    'sync — profil sans periodicite avant le changement de marque/modele (temoin)',
    public.detendeur_periodicite_mois('Marque Sans Referentiel', 'X', date '2024-01-15') is null);

update public.item
set marque = 'Marque Sans Referentiel', modele = 'X'
where id = 999300001;

select moamat_test_detendeurs.expect(
    'sync — item.date_echeance recalculee a NULL quand marque/modele change vers un profil sans referentiel',
    (select date_echeance from public.item where id = 999300001) is null);

update public.item
set marque = 'Scubapro', modele = 'MK25'
where id = 999300001;

select moamat_test_detendeurs.expect(
    'sync — item.date_echeance recalculee correctement quand marque/modele revient a un profil connu',
    (select date_echeance from public.item where id = 999300001) = date '2026-01-15');

-- Suppression de la ligne item_detendeur : item.date_echeance ne doit pas
-- rester bloquée sur la dernière valeur calculée.
delete from public.item_detendeur where item_id = 999300001;

select moamat_test_detendeurs.expect(
    'sync — item.date_echeance remise a NULL quand item_detendeur est supprimee',
    (select date_echeance from public.item where id = 999300001) is null);

-- -----------------------------------------------------------------------------
--  3. Référentiel administrable et daté — INSERT admin OK, UPDATE/DELETE
--     refusés même pour un admin (historisation append-only)
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000d1',
        'authenticated', 'authenticated', 'item-detendeur-test-admin@moamat.test',
        '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'admin' where user_id = '00000000-0000-0000-0000-0000000000d1';

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000d1', 'email', 'item-detendeur-test-admin@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test_detendeurs.expect('setup — role courant = admin', public.moamat_current_role() = 'admin');

insert into public.ref_periodicite_detendeur (marque, modele, periodicite_mois, date_effet)
values ('Mares', 'Abyss', 30, current_date);

select moamat_test_detendeurs.expect(
    'referentiel — admin peut inserer une nouvelle valeur datee',
    (select count(*) from public.ref_periodicite_detendeur where marque = 'Mares' and modele = 'Abyss') = 1);

select moamat_test_detendeurs.expect_write_denied(
    'referentiel — UPDATE sans effet pour un admin (aucune policy UPDATE)',
    $q$ update public.ref_periodicite_detendeur set periodicite_mois = 1 where marque = 'Mares' and modele = 'Abyss' $q$);

select moamat_test_detendeurs.expect_write_denied(
    'referentiel — DELETE sans effet pour un admin (aucune policy DELETE)',
    $q$ delete from public.ref_periodicite_detendeur where marque = 'Mares' and modele = 'Abyss' $q$);

reset role;

-- Deuxième ligne de défense, indépendante de la RLS : le trigger append-only
-- refuse tout UPDATE/DELETE, y compris pour "postgres" (contourne la RLS).
select moamat_test_detendeurs.expect_raises(
    'referentiel — trigger append-only refuse l''UPDATE meme pour "postgres"',
    $q$ update public.ref_periodicite_detendeur set periodicite_mois = 1 where marque = 'Mares' and modele = 'Abyss' $q$);

select moamat_test_detendeurs.expect_raises(
    'referentiel — trigger append-only refuse le DELETE meme pour "postgres"',
    $q$ delete from public.ref_periodicite_detendeur where marque = 'Mares' and modele = 'Abyss' $q$);

-- -----------------------------------------------------------------------------
--  4. public.enregistrer_intervention_detendeur()
-- -----------------------------------------------------------------------------

insert into public.item (id, famille, marque, modele, statut_code, actif)
overriding system value
values (999300002, 'detendeur', 'Scubapro', 'MK25', 'en_stock', true);

insert into public.item_detendeur (item_id, a_premier_etage, a_second_etage)
values (999300002, true, true);

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000d1', 'email', 'item-detendeur-test-admin@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select public.enregistrer_intervention_detendeur(
    999300002, date '2024-06-01', 'Apragaz', 45.00, 'Entretien annuel', gen_random_uuid());

select moamat_test_detendeurs.expect(
    'intervention — evenement historise',
    (select count(*) from public.detendeur_evenement where item_id = 999300002) = 1);

select moamat_test_detendeurs.expect(
    'intervention — compteur de derniere intervention mis a jour',
    (select date_derniere_intervention from public.item_detendeur where item_id = 999300002) = date '2024-06-01');

select moamat_test_detendeurs.expect(
    'intervention — echeance recalculee par le trigger de synchronisation',
    (select date_echeance from public.item where id = 999300002) = date '2026-06-01');

-- Une intervention ANTÉRIEURE à la dernière connue est historisée mais ne
-- fait pas reculer le compteur ni l'échéance.
select public.enregistrer_intervention_detendeur(
    999300002, date '2023-01-01', null, null, 'Intervention plus ancienne, retrouvee tardivement', gen_random_uuid());

select moamat_test_detendeurs.expect(
    'intervention — deuxieme evenement bien historise',
    (select count(*) from public.detendeur_evenement where item_id = 999300002) = 2);

select moamat_test_detendeurs.expect(
    'intervention — le compteur ne recule jamais',
    (select date_derniere_intervention from public.item_detendeur where item_id = 999300002) = date '2024-06-01');

-- Idempotence : un rejeu avec le MÊME request_id ne crée pas de doublon.
do $$
declare
    v_request_id uuid := gen_random_uuid();
    v_id_1 bigint;
    v_id_2 bigint;
begin
    v_id_1 := public.enregistrer_intervention_detendeur(
        999300002, date '2024-09-01', 'Apragaz', 10.00, 'rejeu', v_request_id);
    v_id_2 := public.enregistrer_intervention_detendeur(
        999300002, date '2024-09-01', 'Apragaz', 10.00, 'rejeu', v_request_id);

    perform moamat_test_detendeurs.expect('intervention — rejeu idempotent renvoie le meme id', v_id_1 = v_id_2);
end $$;

select moamat_test_detendeurs.expect(
    'intervention — rejeu idempotent ne cree pas de doublon',
    (select count(*) from public.detendeur_evenement where item_id = 999300002) = 3);

-- Refus : date future, cout negatif, ensemble introuvable.
select moamat_test_detendeurs.expect_raises(
    'intervention — date future refusee',
    format($q$ select public.enregistrer_intervention_detendeur(%s, current_date + 1, null, null, null) $q$, 999300002));

select moamat_test_detendeurs.expect_raises(
    'intervention — cout negatif refuse',
    format($q$ select public.enregistrer_intervention_detendeur(%s, current_date, null, -1, null) $q$, 999300002));

select moamat_test_detendeurs.expect_raises(
    'intervention — ensemble introuvable refuse',
    $q$ select public.enregistrer_intervention_detendeur(999999999, current_date, null, null, null) $q$);

reset role;

-- Ensemble désactivé : refusé.
insert into public.item (id, famille, marque, modele, statut_code, actif)
overriding system value
values (999300003, 'detendeur', 'Scubapro', 'MK25', 'en_stock', false);

insert into public.item_detendeur (item_id, a_premier_etage, a_second_etage)
values (999300003, true, true);

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000d1', 'email', 'item-detendeur-test-admin@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test_detendeurs.expect_raises(
    'intervention — ensemble desactive refuse',
    format($q$ select public.enregistrer_intervention_detendeur(%s, current_date, null, null, null) $q$, 999300003));

reset role;

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
