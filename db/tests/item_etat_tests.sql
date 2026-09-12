-- =============================================================================
--  MoaMat — Tests de la machine à états item (db/item_etat.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--  Rien n'est conservé (comptes de test, items de test, transitions, audit).
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/transform_item.sql, db/rls.sql, db/audit.sql,
--      db/item_etat.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/item_etat_tests.sql
--
--  Couverture :
--    1. Aucune colonne « disponibilité » manuelle/éditable n'existe sur
--       public.item (A16 : le statut/la disponibilité est calculé, jamais
--       saisi).
--    2. Tout changement de statut exige motif + date d'effet.
--    3. Perdu / Volé exigent une pièce jointe justificative.
--    4. Toute transition vers un statut terminal exige l'autorité
--       décisionnaire et est journalisée (item_transition + audit_log).
--    5. Un statut terminal est irréversible pour un rôle « gestion » (RLS),
--       y compris via un changement de statut malgré motif/date fournis, et
--       le retour n'est possible QUE pour un rôle disposant de
--       « status.terminal.override » (admin/super-admin), lui aussi
--       journalisé.
--    6. Disponibilité calculée : cas A15/A16/A20 (échéance dépassée, prêt
--       ouvert, maintenance en cours) — voir db/MODELE.md §9.4 pour la
--       correspondance avec l'audit des données existantes.
-- =============================================================================

begin;

set client_min_messages to notice;

-- -----------------------------------------------------------------------------
--  Outillage d'assertion (même schéma jetable que db/tests/rls_tests.sql)
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

create function moamat_test.expect_write_denied(p_label text, p_sql text)
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

create function moamat_test.expect_write_ok(p_label text, p_sql text)
returns void language plpgsql as $$
declare
    v_n bigint;
begin
    execute p_sql;
    get diagnostics v_n = row_count;
    if v_n >= 1 then
        raise notice 'PASS: % (% ligne(s))', p_label, v_n;
    else
        raise exception 'FAIL: % — 0 ligne affectee, >= 1 attendu', p_label;
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

-- -----------------------------------------------------------------------------
--  0. Aucune disponibilité manuelle n'existe nulle part dans le modèle (A16)
-- -----------------------------------------------------------------------------

select moamat_test.expect(
    'A16 — aucune colonne "disponib*" sur public.item (calculee, jamais saisie)',
    not exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'item'
          and column_name ilike '%disponib%'
    ));

select moamat_test.expect(
    'A16 — public.item_est_disponible() existe et est appelable',
    to_regprocedure('public.item_est_disponible(public.item)') is not null);

-- -----------------------------------------------------------------------------
--  Jeu de données de test (créé en tant que « postgres », RLS contournée)
-- -----------------------------------------------------------------------------

-- 2 comptes : b1 (gestion, équipe matériel), b2 (admin, override terminal).
do $$
declare
    v_id uuid;
    v_i  int := 0;
begin
    foreach v_id in array array[
        '00000000-0000-0000-0000-0000000000b1'::uuid,
        '00000000-0000-0000-0000-0000000000b2'::uuid
    ]
    loop
        v_i := v_i + 1;
        insert into auth.users (instance_id, id, aud, role, email,
                                encrypted_password, email_confirmed_at,
                                created_at, updated_at,
                                raw_app_meta_data, raw_user_meta_data)
        values ('00000000-0000-0000-0000-000000000000', v_id,
                'authenticated', 'authenticated',
                'item-etat-test-' || v_i || '@moamat.test',
                '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);
    end loop;
end $$;

update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000b1';
update public.utilisateur_role set role = 'admin'   where user_id = '00000000-0000-0000-0000-0000000000b2';

-- Item de test (petit matériel : pas de contrainte de famille bouteille/detendeur).
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999100001, 'petit_materiel', 'en_stock', true);

-- =============================================================================
--  1. Motif / date obligatoires pour tout changement de statut
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000b1', 'email', 'item-etat-test-1@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('setup — role courant = gestion', public.moamat_current_role() = 'gestion');

select moamat_test.expect_raises(
    'transition — motif manquant refuse',
    $q$ update public.item set statut_code = 'en_maintenance' where id = 999100001 $q$);

select moamat_test.expect_raises(
    'transition — date d''effet manquante refusee',
    $q$ update public.item set statut_code = 'en_maintenance', statut_motif = 'test' where id = 999100001 $q$);

select moamat_test.expect_write_ok(
    'transition — motif + date fournis : en_stock -> en_maintenance',
    $q$ update public.item
        set statut_code = 'en_maintenance', statut_motif = 'controle preventif', statut_date_effet = current_date
        where id = 999100001 $q$);

select moamat_test.expect(
    'transition — colonnes transitoires remises a NULL apres coup',
    (select statut_motif is null and statut_date_effet is null
     from public.item where id = 999100001));

select moamat_test.expect(
    'transition — journalisee dans item_transition',
    (select count(*) from public.item_transition
     where item_id = 999100001 and ancien_statut = 'en_stock' and nouveau_statut = 'en_maintenance') = 1);

-- =============================================================================
--  2. Perte / Vol : pièce jointe obligatoire ; autorité obligatoire (terminal)
-- =============================================================================

select moamat_test.expect_raises(
    'transition vers perdu — sans piece jointe : refusee',
    $q$ update public.item
        set statut_code = 'perdu', statut_motif = 'inventaire annuel', statut_date_effet = current_date
        where id = 999100001 $q$);

select moamat_test.expect_raises(
    'transition vers perdu — piece jointe sans autorite : refusee',
    $q$ update public.item
        set statut_code = 'perdu', statut_motif = 'inventaire annuel', statut_date_effet = current_date,
            statut_piece_jointe_url = 'materiel-photos/test-pv-perte.pdf'
        where id = 999100001 $q$);

select moamat_test.expect_write_ok(
    'transition vers perdu — motif+date+piece jointe+autorite : acceptee (gestion peut ENTRER en terminal)',
    $q$ update public.item
        set statut_code = 'perdu', statut_motif = 'inventaire annuel', statut_date_effet = current_date,
            statut_piece_jointe_url = 'materiel-photos/test-pv-perte.pdf', statut_autorite = 'gestionnaire_materiel'
        where id = 999100001 $q$);

select moamat_test.expect(
    'transition vers perdu — journalisee avec autorite + piece jointe',
    (select count(*) from public.item_transition
     where item_id = 999100001 and nouveau_statut = 'perdu'
       and autorite_decision = 'gestionnaire_materiel'
       and piece_jointe_url is not null) = 1);

select moamat_test.expect(
    'transition vers perdu — auditee (audit_log, statut terminal)',
    (select count(*) from public.audit_log
     where entity_table = 'item' and entity_id = '999100001' and action = 'status.terminal'
       and after ->> 'statut_code' = 'perdu') = 1);

-- =============================================================================
--  3. Statut terminal irréversible pour « gestion » (RLS + trigger)
-- =============================================================================

select moamat_test.expect_write_denied(
    'terminal — gestion NE PEUT PAS revenir depuis "perdu" (RLS item_upd)',
    $q$ update public.item
        set statut_code = 'en_stock', statut_motif = 'retrouve', statut_date_effet = current_date
        where id = 999100001 $q$);

select moamat_test.expect(
    'terminal — item reste "perdu" apres tentative refusee',
    (select statut_code from public.item where id = 999100001) = 'perdu');

reset role;

-- =============================================================================
--  4. Retour depuis un statut terminal : reserve a status.terminal.override
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000b2', 'email', 'item-etat-test-2@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('setup — role courant = admin', public.moamat_current_role() = 'admin');
select moamat_test.expect('setup — admin a status.terminal.override', public.has_permission('status.terminal.override'));

select moamat_test.expect_write_ok(
    'terminal — admin (override) PEUT revenir depuis "perdu"',
    $q$ update public.item
        set statut_code = 'en_stock', statut_motif = 'materiel retrouve lors de l''inventaire', statut_date_effet = current_date
        where id = 999100001 $q$);

select moamat_test.expect(
    'terminal — retour journalise (item_transition)',
    (select count(*) from public.item_transition
     where item_id = 999100001 and ancien_statut = 'perdu' and nouveau_statut = 'en_stock') = 1);

select moamat_test.expect(
    'terminal — retour journalise (audit_log, from_terminal=true)',
    (select count(*) from public.audit_log
     where entity_table = 'item' and entity_id = '999100001'
       and action = 'status.terminal'
       and (context ->> 'from_terminal')::boolean = true
       and after ->> 'statut_code' = 'en_stock') = 1);

reset role;

-- =============================================================================
--  5. Disponibilité calculée — cas A15 / A16 / A20 (db/MODELE.md §9.4)
-- =============================================================================

-- A15 : statut "en_stock" mais échéance réglementaire DÉPASSÉE -> indisponible.
insert into public.item (id, famille, statut_code, actif, date_echeance)
overriding system value
values (999100015, 'petit_materiel', 'en_stock', true, current_date - 1);

select moamat_test.expect(
    'A15 — en_stock + echeance depassee => indisponible',
    (select disponible from public.v_item where id = 999100015) = false);

-- A16 : statut "en_stock", pas d'échéance, mais un PRÊT reste ouvert sur le
-- code club de l'item (dérive de données que le calcul doit rattraper — cf.
-- le principe A16 lui-même : la disponibilité n'est jamais prise pour argent
-- comptant sur le seul statut affiché).
insert into public.item (id, famille, code_club, statut_code, actif)
overriding system value
values (999100016, 'bouteille', 'TEST-A16', 'en_stock', true);

insert into public.pret (id, bouteille_code, date_pret, est_cloture, date_retour_reelle)
overriding system value
values (999100016, 'TEST-A16', current_date - 3, false, null);

select moamat_test.expect(
    'A16 — en_stock + pret ouvert => indisponible malgre le statut affiche',
    (select disponible from public.v_item where id = 999100016) = false);

-- A20 : statut "en_maintenance" -> indisponible, même sans échéance ni prêt.
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999100020, 'petit_materiel', 'en_maintenance', true);

select moamat_test.expect(
    'A20 — en_maintenance => indisponible',
    (select disponible from public.v_item where id = 999100020) = false);

-- Témoin : en_stock, pas d'échéance, pas de prêt -> disponible.
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999100099, 'petit_materiel', 'en_stock', true);

select moamat_test.expect(
    'temoin — en_stock sans echeance ni pret => disponible',
    (select disponible from public.v_item where id = 999100099) = true);

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
