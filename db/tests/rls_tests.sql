-- =============================================================================
--  MoaMat — Tests de sécurité RLS / rôles / audit.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--  Rien n'est conservé (comptes de test, lignes de test, entrées d'audit).
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/rls.sql, db/audit.sql
--  puis lancer CE fichier avec un rôle non restreint :
--      éditeur SQL Supabase (rôle « postgres »)
--      ou :  psql "$SUPABASE_DB_URL" -f db/tests/rls_tests.sql
--
--  Lecture des résultats : chaque contrôle émet « PASS: … » (NOTICE) ou
--  interrompt le script sur « FAIL: … » (EXCEPTION). Fin attendue :
--      NOTICE:  ===== TOUS LES CONTROLES SONT PASSES =====
--      ROLLBACK
--
--  Couverture (exigences de sécurité) :
--    1. Un utilisateur NON AUTHENTIFIÉ est bloqué sur toutes les tables métier.
--    2. Un utilisateur « User » (lecture) ne peut pas modifier les référentiels
--       (bouteilles, échéances, tarifs…).
--    3. Un utilisateur « CA » (lecture) ne peut pas écrire sur les items
--       (lecture seule).
--    4. L'équipe matériel (gestion / admin) peut écrire sur les items.
--    5. L'administration (référentiels, rôles, audit) est réservée à
--       admin / super-admin.
--    6. Le siège super-admin n'est jamais posé par défaut ; seul un
--       super-admin peut attribuer le rôle admin / super-admin.
--    7. Le journal d'audit est append-only (ni UPDATE ni DELETE) et alimenté
--       par trigger lors d'un changement de rôle.
-- =============================================================================

begin;

set client_min_messages to notice;

-- -----------------------------------------------------------------------------
--  Outillage d'assertion (schéma jetable « moamat_test »)
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

-- Attendu : l'écriture est refusée — soit erreur 42501 (RLS / WITH CHECK),
-- soit 0 ligne affectée (clause USING fausse).
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

-- Attendu : l'écriture réussit et touche >= 1 ligne.
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

-- Attendu : la requête lève une erreur (ex. trigger append-only).
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
--  Jeu de données de test (créé en tant que « postgres », RLS contournée)
-- -----------------------------------------------------------------------------

-- 6 comptes : le trigger on_auth_user_created insère pour chacun une ligne
-- public.utilisateur_role au rôle « lecture ».
--   a1 : lecture   (cible d'attribution de rôle)
--   a2 : lecture   (« User »)
--   a3 : gestion   (équipe matériel)
--   a4 : admin
--   a5 : super-admin
--   a6 : lecture   (contrôle « rôle par défaut »)
do $$
declare
    v_id uuid;
    v_i  int := 0;
begin
    foreach v_id in array array[
        '00000000-0000-0000-0000-0000000000a1'::uuid,
        '00000000-0000-0000-0000-0000000000a2'::uuid,
        '00000000-0000-0000-0000-0000000000a3'::uuid,
        '00000000-0000-0000-0000-0000000000a4'::uuid,
        '00000000-0000-0000-0000-0000000000a5'::uuid,
        '00000000-0000-0000-0000-0000000000a6'::uuid
    ]
    loop
        v_i := v_i + 1;
        insert into auth.users (instance_id, id, aud, role, email,
                                encrypted_password, email_confirmed_at,
                                created_at, updated_at,
                                raw_app_meta_data, raw_user_meta_data)
        values ('00000000-0000-0000-0000-000000000000', v_id,
                'authenticated', 'authenticated',
                'test-' || v_i || '@moamat.test',
                '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);
    end loop;
end $$;

-- 6a. Rôle par défaut à la création : « lecture », jamais « super-admin ».
select moamat_test.expect(
    'nouveau compte -> role « lecture » par defaut',
    (select role::text from public.utilisateur_role where user_id = '00000000-0000-0000-0000-0000000000a6') = 'lecture');
select moamat_test.expect(
    'le trigger n''attribue jamais « super-admin »',
    (select count(*) from public.utilisateur_role where role = 'super-admin') = 0);

-- Élévation des rôles de test (en tant que postgres : pas de RLS).
update public.utilisateur_role set role = 'gestion'     where user_id = '00000000-0000-0000-0000-0000000000a3';
update public.utilisateur_role set role = 'admin'       where user_id = '00000000-0000-0000-0000-0000000000a4';
update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000a5';

-- Lignes métier de test (ids hauts, hors plage de la reprise Access).
insert into public.bouteille (id, num_peint, est_declassee)
values (999000001, 'TEST-BTL', false);
insert into public.ref_tarif_requalification (id, libelle_prestation, sigle, tarif_eur, annee)
values (999000001, 'TEST-TARIF', 'TT', 10.00, 2026);
insert into public.detendeur (id, num_ordre, est_declasse)
values (999000001, 'TEST-DET', false);

-- =============================================================================
--  1. Utilisateur NON AUTHENTIFIÉ — bloqué partout
-- =============================================================================

select set_config('request.jwt.claims', '', true);
set local role anon;

select moamat_test.expect('anon — auth.uid() est NULL', auth.uid() is null);
select moamat_test.expect('anon — 0 bouteille visible',          (select count(*) from public.bouteille)                 = 0);
select moamat_test.expect('anon — 0 detendeur visible',          (select count(*) from public.detendeur)                 = 0);
select moamat_test.expect('anon — 0 tarif de requalif. visible', (select count(*) from public.ref_tarif_requalification) = 0);
select moamat_test.expect('anon — 0 pret visible',               (select count(*) from public.pret)                      = 0);
select moamat_test.expect('anon — 0 personne visible',           (select count(*) from public.personne)                  = 0);
select moamat_test.expect('anon — 0 facture visible',            (select count(*) from public.achat_facture)             = 0);
select moamat_test.expect_write_denied(
    'anon — INSERT bouteille refuse',
    $q$ insert into public.bouteille (num_peint) values ('HACK-ANON') $q$);

reset role;

-- =============================================================================
--  2. « User » (lecture) — pas de modification des référentiels
--  3. « CA » (lecture) — lecture seule sur les items
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000a2', 'email', 'test-2@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('lecture — role courant = lecture', public.moamat_current_role() = 'lecture');
select moamat_test.expect('CA/User — peut LIRE les bouteilles',   (select count(*) from public.bouteille)                 >= 1);
select moamat_test.expect('CA/User — peut LIRE les detendeurs',   (select count(*) from public.detendeur)                 >= 1);
select moamat_test.expect('CA/User — peut LIRE les referentiels', (select count(*) from public.ref_tarif_requalification) >= 1);

select moamat_test.expect_write_denied(
    'CA/User — INSERT bouteille refuse (lecture seule sur items)',
    $q$ insert into public.bouteille (num_peint) values ('HACK-LECT') $q$);
select moamat_test.expect_write_denied(
    'CA/User — UPDATE bouteille refuse (lecture seule sur items)',
    $q$ update public.bouteille set num_peint = 'HACK' where id = 999000001 $q$);
select moamat_test.expect_write_denied(
    'User — UPDATE referentiel tarif refuse',
    $q$ update public.ref_tarif_requalification set tarif_eur = 0 where id = 999000001 $q$);
select moamat_test.expect_write_denied(
    'User — INSERT referentiel tarif refuse',
    $q$ insert into public.ref_tarif_requalification (libelle_prestation, annee) values ('HACK', 2026) $q$);
select moamat_test.expect_write_denied(
    'User — DELETE referentiel tarif refuse',
    $q$ delete from public.ref_tarif_requalification where id = 999000001 $q$);
select moamat_test.expect('CA/User — NE VOIT PAS le journal d''audit',        (select count(*) from public.audit_log) = 0);
select moamat_test.expect('CA/User — NE VOIT PAS l''annuaire des membres',    (select count(*) from public.personne)  = 0);
select moamat_test.expect('CA/User — ne voit que sa propre ligne de role',
    (select count(*) from public.utilisateur_role) = 1
    and (select user_id from public.utilisateur_role) = '00000000-0000-0000-0000-0000000000a2');
select moamat_test.expect_write_denied(
    'User — ne peut pas s''auto-promouvoir',
    $q$ update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000a2' $q$);

reset role;

-- =============================================================================
--  4. Équipe matériel — « gestion » écrit sur les items, pas sur les référentiels
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000a3', 'email', 'test-3@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('gestion — role courant = gestion', public.moamat_current_role() = 'gestion');
select moamat_test.expect_write_ok(
    'gestion — INSERT bouteille autorise',
    $q$ insert into public.bouteille (num_peint) values ('OK-GEST') $q$);
select moamat_test.expect_write_ok(
    'gestion — UPDATE bouteille autorise',
    $q$ update public.bouteille set remarque = 'ctrl' where id = 999000001 $q$);
select moamat_test.expect_write_ok(
    'gestion — UPDATE detendeur autorise',
    $q$ update public.detendeur set remarque = 'ctrl' where id = 999000001 $q$);
select moamat_test.expect_write_denied(
    'gestion — UPDATE referentiel tarif refuse',
    $q$ update public.ref_tarif_requalification set tarif_eur = 0 where id = 999000001 $q$);
select moamat_test.expect_write_denied(
    'gestion — INSERT role refuse (administration)',
    $q$ insert into public.utilisateur_role (user_id, role) values ('00000000-0000-0000-0000-0000000000a1', 'gestion') $q$);
select moamat_test.expect('gestion — NE VOIT PAS le journal d''audit', (select count(*) from public.audit_log) = 0);

reset role;

-- =============================================================================
--  5 & 6. Administration — « admin » gère les référentiels et l'audit ;
--         seul « super-admin » attribue les rôles élevés
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000a4', 'email', 'test-4@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('admin — role courant = admin', public.moamat_current_role() = 'admin');
select moamat_test.expect_write_ok(
    'admin — UPDATE referentiel tarif autorise',
    $q$ update public.ref_tarif_requalification set tarif_eur = 42 where id = 999000001 $q$);
select moamat_test.expect('admin — VOIT le journal d''audit', (select count(*) from public.audit_log) >= 1);
select moamat_test.expect_write_ok(
    'admin — peut attribuer le role « gestion »',
    $q$ insert into public.utilisateur_role (user_id, role)
        values ('00000000-0000-0000-0000-0000000000a1', 'gestion')
        on conflict (user_id) do update set role = 'gestion' $q$);
select moamat_test.expect_write_denied(
    'admin — NE PEUT PAS attribuer le role « admin »',
    $q$ update public.utilisateur_role set role = 'admin' where user_id = '00000000-0000-0000-0000-0000000000a1' $q$);
select moamat_test.expect_write_denied(
    'admin — NE PEUT PAS attribuer le role « super-admin »',
    $q$ update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000a1' $q$);
select moamat_test.expect_write_ok(
    'admin — peut redescendre « gestion » vers « lecture »',
    $q$ update public.utilisateur_role set role = 'lecture' where user_id = '00000000-0000-0000-0000-0000000000a1' $q$);

reset role;

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000a5', 'email', 'test-5@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('super-admin — role courant = super-admin', public.moamat_current_role() = 'super-admin');
select moamat_test.expect_write_ok(
    'super-admin — PEUT attribuer le role « admin »',
    $q$ update public.utilisateur_role set role = 'admin' where user_id = '00000000-0000-0000-0000-0000000000a1' $q$);
select moamat_test.expect_write_ok(
    'super-admin — PEUT attribuer le role « super-admin »',
    $q$ update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000a2' $q$);

reset role;

-- =============================================================================
--  7. Journal d'audit — append-only + alimentation par trigger
-- =============================================================================

select moamat_test.expect(
    'audit — les changements de role sont traces',
    (select count(*) from public.audit_log
     where entity_table = 'utilisateur_role' and action in ('role.changed', 'role.assigned')) >= 1);

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000a4', 'email', 'test-4@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;
select moamat_test.expect_raises(
    'audit — UPDATE du journal refuse (append-only)',
    $q$ update public.audit_log set action = 'tampered' where id = (select min(id) from public.audit_log) $q$);
select moamat_test.expect_raises(
    'audit — DELETE dans le journal refuse (append-only)',
    $q$ delete from public.audit_log where id = (select min(id) from public.audit_log) $q$);
reset role;

-- Même « postgres » (propriétaire) ne peut ni modifier ni purger le journal.
select moamat_test.expect_raises(
    'audit — UPDATE refuse meme pour le proprietaire',
    $q$ update public.audit_log set action = 'tampered' where id = (select min(id) from public.audit_log) $q$);

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
