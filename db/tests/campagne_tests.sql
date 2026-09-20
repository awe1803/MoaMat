-- =============================================================================
--  MoaMat — Tests des campagnes de réépreuve (db/campagne.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--  Rien n'est conservé (comptes de test, items de test, campagnes de test).
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/transform_item.sql, db/rls.sql, db/audit.sql,
--      db/item_etat.sql, db/item_bouteille.sql, db/campagne.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/campagne_tests.sql
--
--  Couverture :
--    1. Création d'une campagne (preparation) avec une ligne par bouteille ;
--       le tableau de bouteilles est dédoublonné.
--    2. Une bouteille déjà engagée dans une campagne active ne peut pas être
--       ajoutée à une deuxième (trigger d'unicité).
--    2bis. Une sélection invalide (id inexistant, item pas de la famille
--       bouteille, bouteille désactivée ou en statut terminal) est refusée
--       à la création, avec un message précis.
--    3. Envoi refusé tant qu'une ligne n'a pas de prestation renseignée ;
--       accepté une fois toutes renseignées, bascule chaque bouteille en
--       "en_controle".
--    4. Retour groupé partiel : la bouteille pointée revient en "en_stock"
--       avec son compteur de contrôle et son échéance recalculés ; la
--       bouteille non pointée reste "en_controle" et apparaît manquante.
--    5. Coût réel obligatoire et >= 0 au pointage. Rejeu idempotent d'un
--       pointage déjà appliqué (valeurs identiques) accepté sans erreur ;
--       re-pointage avec des valeurs différentes refusé.
--    5bis. Un appel qui ne pointe rien de neuf (rejeu pur) est un VRAI no-op
--       de bout en bout : campagne.date_retour/modifie_le ne bougent pas,
--       même si p_date_retour diffère de l'appel précédent.
--    6. Retour vide refusé ; date de retour antérieure à l'envoi refusée.
--    7. Résultat de requalification obligatoire (conforme/echec). "echec" :
--       la ligne est enregistrée (plus "manquante") mais le compteur de
--       contrôle et le statut de la bouteille ne sont JAMAIS touchés.
--    8. Envoi refusé si une bouteille est devenue inéligible (déclassée)
--       PENDANT la préparation, entre la création et l'envoi de la campagne.
-- =============================================================================

begin;

set client_min_messages to notice;

-- -----------------------------------------------------------------------------
--  Outillage d'assertion (même gabarit que db/tests/item_etat_tests.sql)
-- -----------------------------------------------------------------------------

create schema moamat_test;
grant usage on schema moamat_test to authenticated, anon;

create function moamat_test.expect(p_label text, p_got boolean)
returns void language plpgsql as $$
begin
    if p_got then
        raise notice 'PASS: %', p_label;
    else
        raise exception 'FAIL: %', p_label;
    end if;
end $$;

create function moamat_test.expect_raises(p_label text, p_sql text)
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

grant execute on all functions in schema moamat_test to authenticated, anon;

-- Tables de travail (créées AVANT « set local role », dans le schéma de test,
-- au lieu de tables temporaires : robuste si l'éditeur SQL change de session).
create table moamat_test.t_campagne        (id bigint);
create table moamat_test.t_campagne_dedup  (id bigint);
create table moamat_test.t_campagne5       (id bigint);
create table moamat_test.t_ligne5          (id bigint);
create table moamat_test.t_snapshot5       (date_retour date, modifie_le timestamptz);
create table moamat_test.t_campagne6       (id bigint);
create table moamat_test.t_campagne7       (id bigint);
create table moamat_test.t_ligne7          (id bigint);
create table moamat_test.t_campagne8       (id bigint);
grant all on all tables in schema moamat_test to authenticated, anon;

-- -----------------------------------------------------------------------------
--  Fixture : un compte "gestion" (campagne.create / campagne.update),
--  deux bouteilles classifiées plongée acier avec un dernier contrôle connu
--  (pour vérifier le recalcul d'échéance au retour).
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000c1',
        'authenticated', 'authenticated', 'campagne-test-1@moamat.test',
        '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000c1';

insert into public.item (id, famille, statut_code, actif)
overriding system value
values
    (999200001, 'bouteille', 'en_stock', true),
    (999200002, 'bouteille', 'en_stock', true),
    -- Pas de la famille bouteille (aucune ligne item_bouteille) — pour le test 2bis.
    (999200003, 'petit_materiel', 'en_stock', true),
    -- Statut terminal — pour le test 2bis (perdu = est_terminal, cf. db/model_item.sql).
    (999200004, 'bouteille', 'perdu', true),
    -- Bouteille "normale", utilisée uniquement pour le test de dédoublonnage.
    (999200005, 'bouteille', 'en_stock', true),
    -- Bouteille dédiée aux tests de validation du coût réel / rejeu idempotent (section 5).
    (999200006, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values
    (999200001, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
    (999200002, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
    (999200004, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
    (999200005, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
    (999200006, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000c1', 'email', 'campagne-test-1@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('setup — role courant = gestion', public.moamat_current_role() = 'gestion');

-- =============================================================================
--  1. Création
-- =============================================================================


do $$
declare
    v_id bigint;
begin
    v_id := public.creer_campagne('Apragaz', array[999200001, 999200002]);
    insert into moamat_test.t_campagne values (v_id);
end $$;

select moamat_test.expect(
    'creation — campagne en statut preparation',
    (select statut from public.campagne where id = (select id from moamat_test.t_campagne)) = 'preparation');

select moamat_test.expect(
    'creation — une ligne par bouteille selectionnee',
    (select count(*) from public.campagne_ligne where campagne_id = (select id from moamat_test.t_campagne)) = 2);

select moamat_test.expect(
    'creation — prestation non renseignee par defaut',
    (select count(*) from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_campagne) and type_prestation is not null) = 0);

-- Doublons dans le tableau d'entrée : dédoublonnés, une seule ligne créée
-- (et pas une "unique_violation" opaque en cours de boucle).

do $$
declare
    v_id bigint;
begin
    v_id := public.creer_campagne('Apragaz', array[999200005, 999200005, 999200005]);
    insert into moamat_test.t_campagne_dedup values (v_id);
end $$;

select moamat_test.expect(
    'creation — doublons du tableau dedoublonnes (une seule ligne)',
    (select count(*) from public.campagne_ligne where campagne_id = (select id from moamat_test.t_campagne_dedup)) = 1);

-- =============================================================================
--  2. Unicite — une bouteille deja engagee ne peut pas rejoindre une 2e campagne active
-- =============================================================================

select moamat_test.expect_raises(
    'unicite — bouteille deja engagee dans une campagne active refusee',
    $q$ select public.creer_campagne('Apragaz', array[999200001]) $q$);

-- =============================================================================
--  2bis. Selection invalide refusee a la creation
-- =============================================================================

select moamat_test.expect_raises(
    'creation — identifiant inexistant refuse',
    $q$ select public.creer_campagne('Apragaz', array[999999999]) $q$);

select moamat_test.expect_raises(
    'creation — item hors famille bouteille refuse',
    $q$ select public.creer_campagne('Apragaz', array[999200003]) $q$);

select moamat_test.expect_raises(
    'creation — bouteille en statut terminal refusee',
    $q$ select public.creer_campagne('Apragaz', array[999200004]) $q$);

-- Liste blanche de statuts (pas une simple exclusion des statuts terminaux) :
-- une bouteille prêtée n'est pas physiquement disponible pour l'expédition.
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200010, 'bouteille', 'prete', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999200010, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

select moamat_test.expect_raises(
    'creation — bouteille pretee refusee (liste blanche de statuts)',
    $q$ select public.creer_campagne('Apragaz', array[999200010]) $q$);

-- =============================================================================
--  3. Envoi — refuse tant qu'une prestation manque, accepte une fois completees
-- =============================================================================

select moamat_test.expect_raises(
    'envoi — refuse tant qu''une ligne n''a pas de prestation',
    $q$ select public.envoyer_campagne((select id from moamat_test.t_campagne), 'BON-TEST-1', current_date) $q$);

do $$
declare
    v_ligne_id bigint;
begin
    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = (select id from moamat_test.t_campagne) and item_id = 999200001;
    perform public.definir_prestation_campagne_ligne(v_ligne_id, 'rr');

    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = (select id from moamat_test.t_campagne) and item_id = 999200002;
    perform public.definir_prestation_campagne_ligne(v_ligne_id, 'hydraulique_eau');
end $$;

select moamat_test.expect(
    'prestation — cout estime calcule via le referentiel tarifaire',
    (select cout_estime_eur from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_campagne) and item_id = 999200001) is not null);

select public.envoyer_campagne((select id from moamat_test.t_campagne), 'BON-TEST-1', current_date);

select moamat_test.expect(
    'envoi — campagne passee en statut envoyee',
    (select statut from public.campagne where id = (select id from moamat_test.t_campagne)) = 'envoyee');

select moamat_test.expect(
    'envoi — les 2 bouteilles basculent en en_controle',
    (select count(*) from public.item where id in (999200001, 999200002) and statut_code = 'en_controle') = 2);

-- =============================================================================
--  4. Retour partiel — une bouteille pointee, l'autre manquante
-- =============================================================================

do $$
declare
    v_ligne_id bigint;
    v_lignes jsonb;
begin
    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = (select id from moamat_test.t_campagne) and item_id = 999200001;

    v_lignes := jsonb_build_array(jsonb_build_object(
        'ligne_id', v_ligne_id,
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 21.32,
        'num_certificat', 'CERT-TEST-1',
        'resultat', 'conforme'));

    perform public.pointer_retour_campagne((select id from moamat_test.t_campagne), current_date, v_lignes);
end $$;

select moamat_test.expect(
    'retour — campagne passee en statut retournee',
    (select statut from public.campagne where id = (select id from moamat_test.t_campagne)) = 'retournee');

select moamat_test.expect(
    'retour — la bouteille pointee revient en en_stock',
    (select statut_code from public.item where id = 999200001) = 'en_stock');

select moamat_test.expect(
    'retour — certificat et cout reel enregistres sur la ligne pointee',
    (select num_certificat = 'CERT-TEST-1' and cout_reel_eur = 21.32 from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_campagne) and item_id = 999200001));

select moamat_test.expect(
    'retour — compteur optique de la bouteille pointee mis a jour (echeance recalculee)',
    (select date_dernier_controle_optique from public.item_bouteille where item_id = 999200001) = current_date);

select moamat_test.expect(
    'retour — bouteille non pointee reste en_controle',
    (select statut_code from public.item where id = 999200002) = 'en_controle');

select moamat_test.expect(
    'retour — bouteille non pointee signalee manquante (v_campagne_ligne)',
    (select date_retour_ligne is null from public.v_campagne_ligne
     where campagne_id = (select id from moamat_test.t_campagne) and item_id = 999200002));

-- =============================================================================
--  5. Coût réel obligatoire / >= 0 ; rejeu idempotent vs re-pointage refusé
-- =============================================================================


do $$
declare
    v_campagne_id bigint;
    v_ligne_id bigint;
begin
    v_campagne_id := public.creer_campagne('Apragaz', array[999200006]);
    insert into moamat_test.t_campagne5 values (v_campagne_id);

    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = v_campagne_id and item_id = 999200006;
    insert into moamat_test.t_ligne5 values (v_ligne_id);

    perform public.definir_prestation_campagne_ligne(v_ligne_id, 'rr');
    perform public.envoyer_campagne(v_campagne_id, 'BON-TEST-5', current_date);
end $$;

select moamat_test.expect_raises(
    'retour — cout reel absent refuse',
    format($q$ select public.pointer_retour_campagne(%s, current_date,
        jsonb_build_array(jsonb_build_object('ligne_id', %s, 'date_retour', to_char(current_date, 'YYYY-MM-DD'), 'num_certificat', 'CERT-5'))) $q$,
        (select id from moamat_test.t_campagne5), (select id from moamat_test.t_ligne5)));

select moamat_test.expect_raises(
    'retour — cout reel negatif refuse',
    format($q$ select public.pointer_retour_campagne(%s, current_date,
        jsonb_build_array(jsonb_build_object('ligne_id', %s, 'date_retour', to_char(current_date, 'YYYY-MM-DD'), 'cout_reel_eur', -5, 'num_certificat', 'CERT-5'))) $q$,
        (select id from moamat_test.t_campagne5), (select id from moamat_test.t_ligne5)));

select public.pointer_retour_campagne(
    (select id from moamat_test.t_campagne5), current_date,
    jsonb_build_array(jsonb_build_object(
        'ligne_id', (select id from moamat_test.t_ligne5),
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 19.90,
        'num_certificat', 'CERT-5',
        'resultat', 'conforme')));

select moamat_test.expect(
    'retour — pointage initial accepte avec cout valide',
    (select cout_reel_eur from public.campagne_ligne where id = (select id from moamat_test.t_ligne5)) = 19.90);

-- Rejeu IDENTIQUE (retry réseau côté client) : accepté sans erreur.
select public.pointer_retour_campagne(
    (select id from moamat_test.t_campagne5), current_date,
    jsonb_build_array(jsonb_build_object(
        'ligne_id', (select id from moamat_test.t_ligne5),
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 19.90,
        'num_certificat', 'CERT-5',
        'resultat', 'conforme')));

select moamat_test.expect(
    'retour — rejeu identique idempotent accepte (valeurs inchangees)',
    (select cout_reel_eur from public.campagne_ligne where id = (select id from moamat_test.t_ligne5)) = 19.90);

-- Rejeu envoyant plus de décimales que la colonne numeric(12,2) : arrondi
-- AVANT comparaison, donc reconnu comme le même rejeu idempotent plutôt que
-- refusé à tort comme "valeurs différentes".
select public.pointer_retour_campagne(
    (select id from moamat_test.t_campagne5), current_date,
    jsonb_build_array(jsonb_build_object(
        'ligne_id', (select id from moamat_test.t_ligne5),
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 19.9004,
        'num_certificat', 'CERT-5',
        'resultat', 'conforme')));

select moamat_test.expect(
    'retour — rejeu avec decimales excedentaires arrondi avant comparaison (accepte)',
    (select cout_reel_eur from public.campagne_ligne where id = (select id from moamat_test.t_ligne5)) = 19.90);

-- Le rejeu identique est un VRAI no-op : une décision manuelle prise sur la
-- bouteille entretemps (ici : passée en maintenance) ne doit jamais être
-- écrasée par un simple retry réseau qui reforcerait "en_stock".
update public.item
set statut_code = 'en_maintenance', statut_motif = 'test : verification non-ecrasement par rejeu', statut_date_effet = current_date
where id = 999200006;

select public.pointer_retour_campagne(
    (select id from moamat_test.t_campagne5), current_date,
    jsonb_build_array(jsonb_build_object(
        'ligne_id', (select id from moamat_test.t_ligne5),
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 19.90,
        'num_certificat', 'CERT-5',
        'resultat', 'conforme')));

select moamat_test.expect(
    'retour — rejeu identique ne re-ecrase pas un statut change manuellement entretemps',
    (select statut_code from public.item where id = 999200006) = 'en_maintenance');

-- Re-pointage avec des valeurs DIFFÉRENTES : refusé (ne doit jamais faire
-- régresser silencieusement date_dernier_controle_optique / l'échéance).
select moamat_test.expect_raises(
    'retour — re-pointage avec valeurs differentes refuse',
    format($q$ select public.pointer_retour_campagne(%s, current_date,
        jsonb_build_array(jsonb_build_object('ligne_id', %s, 'date_retour', to_char(current_date, 'YYYY-MM-DD'), 'cout_reel_eur', 25.00, 'num_certificat', 'CERT-5-BIS', 'resultat', 'conforme'))) $q$,
        (select id from moamat_test.t_campagne5), (select id from moamat_test.t_ligne5)));

-- =============================================================================
--  5bis. Rejeu pur (aucune ligne neuve) : VRAI no-op y compris au niveau
--     campagne (date_retour/modifie_le inchangés)
-- =============================================================================


insert into moamat_test.t_snapshot5
select date_retour, modifie_le from public.campagne where id = (select id from moamat_test.t_campagne5);

-- Même ligne, mêmes valeurs, mais un p_date_retour DIFFÉRENT (plus tardif) :
-- si le no-op n'était pas total, campagne.date_retour avancerait quand même
-- jusqu'à cette nouvelle date malgré l'absence de toute information neuve.
-- (Note : la non-duplication de l'entrée d'audit "campagne.returned" n'est
-- pas vérifiable ici — la lecture de public.audit_log exige la permission
-- "audit.read", réservée à admin+, hors du rôle "gestion" utilisé par ce
-- script ; ce garde-fou est couvert par relecture de code, pas par ce test.)
select public.pointer_retour_campagne(
    (select id from moamat_test.t_campagne5), current_date + 5,
    jsonb_build_array(jsonb_build_object(
        'ligne_id', (select id from moamat_test.t_ligne5),
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 19.90,
        'num_certificat', 'CERT-5',
        'resultat', 'conforme')));

select moamat_test.expect(
    'retour — rejeu pur : campagne.date_retour inchangee malgre un p_date_retour different',
    (select date_retour from public.campagne where id = (select id from moamat_test.t_campagne5))
    = (select date_retour from moamat_test.t_snapshot5));

select moamat_test.expect(
    'retour — rejeu pur : campagne.modifie_le inchangee (aucune ecriture)',
    (select modifie_le from public.campagne where id = (select id from moamat_test.t_campagne5))
    = (select modifie_le from moamat_test.t_snapshot5));

-- =============================================================================
--  6. Retour vide refuse ; date de retour anterieure a l'envoi refusee
-- =============================================================================


-- Bouteille dédiée, indépendante des sections précédentes (999200005 reste
-- engagée dans la campagne "dedup", restée en "preparation" — donc encore
-- "active" pour le trigger d'unicité, cf. section 1).
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200007, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999200007, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

do $$
declare
    v_campagne_id bigint;
    v_ligne_id bigint;
begin
    v_campagne_id := public.creer_campagne('Apragaz', array[999200007]);
    insert into moamat_test.t_campagne6 values (v_campagne_id);

    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = v_campagne_id and item_id = 999200007;
    perform public.definir_prestation_campagne_ligne(v_ligne_id, 'rr');
    perform public.envoyer_campagne(v_campagne_id, 'BON-TEST-6', current_date);
end $$;

select moamat_test.expect_raises(
    'retour — tableau de lignes vide refuse',
    format($q$ select public.pointer_retour_campagne(%s, current_date, '[]'::jsonb) $q$,
        (select id from moamat_test.t_campagne6)));

select moamat_test.expect(
    'retour — tableau vide refuse : campagne toujours envoyee (pas de bascule)',
    (select statut from public.campagne where id = (select id from moamat_test.t_campagne6)) = 'envoyee');

select moamat_test.expect_raises(
    'retour — date de retour anterieure a la date d''envoi refusee',
    format($q$ select public.pointer_retour_campagne(%s, (current_date - interval '10 days')::date,
        jsonb_build_array(jsonb_build_object('ligne_id',
            (select id from public.campagne_ligne where campagne_id = %s and item_id = 999200007),
            'date_retour', to_char(current_date - interval '10 days', 'YYYY-MM-DD'),
            'cout_reel_eur', 19.90, 'num_certificat', 'CERT-6'))) $q$,
        (select id from moamat_test.t_campagne6), (select id from moamat_test.t_campagne6)));

-- =============================================================================
--  7. Résultat de requalification obligatoire ; "echec" laisse la bouteille
--     intacte (pas de bascule en_stock, pas de mise a jour du compteur)
-- =============================================================================

select moamat_test.expect_raises(
    'retour — resultat manquant refuse',
    format($q$ select public.pointer_retour_campagne(%s, current_date,
        jsonb_build_array(jsonb_build_object('ligne_id',
            (select id from public.campagne_ligne where campagne_id = %s and item_id = 999200007),
            'date_retour', to_char(current_date, 'YYYY-MM-DD'),
            'cout_reel_eur', 19.90, 'num_certificat', 'CERT-6'))) $q$,
        (select id from moamat_test.t_campagne6), (select id from moamat_test.t_campagne6)));

select moamat_test.expect_raises(
    'retour — resultat invalide refuse',
    format($q$ select public.pointer_retour_campagne(%s, current_date,
        jsonb_build_array(jsonb_build_object('ligne_id',
            (select id from public.campagne_ligne where campagne_id = %s and item_id = 999200007),
            'date_retour', to_char(current_date, 'YYYY-MM-DD'),
            'cout_reel_eur', 19.90, 'num_certificat', 'CERT-6', 'resultat', 'bof'))) $q$,
        (select id from moamat_test.t_campagne6), (select id from moamat_test.t_campagne6)));

-- Bouteille dédiée à la campagne "echec".

insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200008, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999200008, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

do $$
declare
    v_campagne_id bigint;
    v_ligne_id bigint;
begin
    v_campagne_id := public.creer_campagne('Apragaz', array[999200008]);
    insert into moamat_test.t_campagne7 values (v_campagne_id);

    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = v_campagne_id and item_id = 999200008;
    insert into moamat_test.t_ligne7 values (v_ligne_id);

    perform public.definir_prestation_campagne_ligne(v_ligne_id, 'hydraulique_eau');
    perform public.envoyer_campagne(v_campagne_id, 'BON-TEST-7', current_date);
end $$;

select public.pointer_retour_campagne(
    (select id from moamat_test.t_campagne7), current_date,
    jsonb_build_array(jsonb_build_object(
        'ligne_id', (select id from moamat_test.t_ligne7),
        'date_retour', to_char(current_date, 'YYYY-MM-DD'),
        'cout_reel_eur', 45.92,
        'num_certificat', 'CERT-7-ECHEC',
        'resultat', 'echec')));

select moamat_test.expect(
    'retour echec — ligne enregistree (cout/certificat/resultat)',
    (select cout_reel_eur = 45.92 and num_certificat = 'CERT-7-ECHEC' and resultat = 'echec'
     from public.campagne_ligne where id = (select id from moamat_test.t_ligne7)));

select moamat_test.expect(
    'retour echec — bouteille NON rebasculee en en_stock (reste en_controle)',
    (select statut_code from public.item where id = 999200008) = 'en_controle');

select moamat_test.expect(
    'retour echec — compteur de controle hydraulique NON modifie',
    (select date_dernier_controle_hydraulique from public.item_bouteille where item_id = 999200008) = date '2024-01-15');

select moamat_test.expect(
    'retour echec — ligne non signalee manquante (a ete pointee)',
    (select date_retour_ligne is not null from public.v_campagne_ligne
     where id = (select id from moamat_test.t_ligne7)));

-- Bouteille condamnée (resultat = 'echec'), toujours "en_controle" : NE DOIT
-- PAS pouvoir être réengagée dans une nouvelle campagne tant qu'elle n'a pas
-- été résolue manuellement (déclassée, ou remise en_stock après vérification) —
-- sans quoi elle pourrait être ré-expédiée par inadvertance vers le prestataire.
select moamat_test.expect_raises(
    'creation — bouteille condamnee (echec) non resolue refusee dans une nouvelle campagne',
    $q$ select public.creer_campagne('Apragaz', array[999200008]) $q$);

-- =============================================================================
--  8. Envoi refuse si une bouteille est devenue inéligible PENDANT la
--     préparation (déclassée après avoir été ajoutée à la campagne)
-- =============================================================================


insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200009, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999200009, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

do $$
declare
    v_campagne_id bigint;
    v_ligne_id bigint;
begin
    v_campagne_id := public.creer_campagne('Apragaz', array[999200009]);
    insert into moamat_test.t_campagne8 values (v_campagne_id);

    select id into v_ligne_id from public.campagne_ligne
        where campagne_id = v_campagne_id and item_id = 999200009;
    perform public.definir_prestation_campagne_ligne(v_ligne_id, 'rr');
end $$;

-- Déclassée APRÈS avoir été ajoutée à la campagne (encore "preparation") —
-- via l'écran de statut normal, pas via ce module. "gestion" peut entrer en
-- statut terminal (motif + pièce jointe + autorité), cf. db/tests/item_etat_tests.sql.
update public.item
set statut_code = 'perdu', statut_motif = 'test : declassee pendant la preparation de la campagne',
    statut_date_effet = current_date, statut_piece_jointe_url = 'materiel-photos/test-pv-perte.pdf',
    statut_autorite = 'gestionnaire_materiel'
where id = 999200009;

select moamat_test.expect_raises(
    'envoi — refuse si une bouteille est devenue inéligible depuis la préparation',
    format($q$ select public.envoyer_campagne(%s, 'BON-TEST-8', current_date) $q$,
        (select id from moamat_test.t_campagne8)));

select moamat_test.expect(
    'envoi refuse — campagne reste en preparation (pas de bascule partielle)',
    (select statut from public.campagne where id = (select id from moamat_test.t_campagne8)) = 'preparation');

select moamat_test.expect(
    'envoi refuse — la bouteille declassee reste "perdu" (statut non ecrase)',
    (select statut_code from public.item where id = 999200009) = 'perdu');

reset role;

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
