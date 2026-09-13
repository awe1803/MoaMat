-- =============================================================================
--  MoaMat — Tests des notifications push (db/notifications.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--
--  Pré-requis : db/roles.sql, db/permissions.sql, db/rls.sql, db/audit.sql,
--  db/comptes.sql, db/notifications.sql. Lancer avec un rôle non restreint :
--      psql "$SUPABASE_DB_URL" -f db/tests/notifications_tests.sql
--
--  Fin attendue :
--      NOTICE:  ===== TOUS LES CONTROLES NOTIFICATIONS SONT PASSES =====
--      ROLLBACK
--
--  Couverture :
--    1. Seul un compte détenant « role.assign » (admin / super-admin) peut
--       s'abonner ; lecture / gestion / en_attente sont refusés.
--    2. Aucune écriture directe sur public.abonnement_push ; chacun ne voit que
--       ses propres abonnements ; on ne supprime pas l'abonnement d'un autre.
--    3. Un endpoint réenregistré est rattaché au dernier compte habilité.
--    4. Les destinataires ne contiennent que des comptes habilités et actifs ;
--       la fonction n'est pas exécutable par « authenticated ».
--    5. Perdre « role.assign » purge les abonnements : rétrogradation,
--       suppression de la ligne de rôle, retrait de la permission dans la
--       matrice. Un changement de rôle qui conserve le droit ne purge rien.
--    6. Le trigger de notification ne bloque jamais la création d'un compte.
-- =============================================================================

begin;

set client_min_messages to notice;

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

-- Attendu : l'écriture est refusée — erreur 42501 ou 0 ligne affectée.
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
        raise notice 'PASS: % (refuse: %)', p_label, sqlerrm;
end $$;

grant execute on all functions in schema moamat_test to authenticated, anon;

-- Session simulée d'un compte de test.
create function moamat_test.as_user(p_id uuid)
returns void language sql as $$
    select set_config('request.jwt.claims',
        json_build_object('sub', p_id, 'role', 'authenticated')::text, true);
$$;

grant execute on function moamat_test.as_user(uuid) to authenticated, anon;

-- -----------------------------------------------------------------------------
--  Jeu de données : b1 admin, b2 lecture, b3 super-admin, b4 gestion,
--  b5 en_attente (jamais activé).
-- -----------------------------------------------------------------------------

do $$
declare
    v_i int := 0;
    v_id uuid;
begin
    foreach v_id in array array[
        '00000000-0000-0000-0000-0000000000b1'::uuid,
        '00000000-0000-0000-0000-0000000000b2'::uuid,
        '00000000-0000-0000-0000-0000000000b3'::uuid,
        '00000000-0000-0000-0000-0000000000b4'::uuid,
        '00000000-0000-0000-0000-0000000000b5'::uuid
    ]
    loop
        v_i := v_i + 1;
        insert into auth.users (instance_id, id, aud, role, email,
                                encrypted_password, email_confirmed_at,
                                created_at, updated_at,
                                raw_app_meta_data, raw_user_meta_data)
        values ('00000000-0000-0000-0000-000000000000', v_id,
                'authenticated', 'authenticated',
                'push-' || v_i || '@moamat.test',
                '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);
    end loop;
end $$;

-- 6. La création des comptes ci-dessus est passée par le trigger
--    notifier_compte_en_attente : elle n'a pas échoué, et le rôle est posé.
select moamat_test.expect(
    'trigger de notification — la creation d''un compte en_attente aboutit',
    (select count(*) from public.utilisateur_role
     where role = 'en_attente'
       and user_id::text like '00000000-0000-0000-0000-0000000000b_') = 5);

update public.utilisateur_role set role = 'admin'       where user_id = '00000000-0000-0000-0000-0000000000b1';
update public.utilisateur_role set role = 'lecture'     where user_id = '00000000-0000-0000-0000-0000000000b2';
update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000b3';
update public.utilisateur_role set role = 'gestion'     where user_id = '00000000-0000-0000-0000-0000000000b4';

-- =============================================================================
--  1. Abonnement réservé à « role.assign »
-- =============================================================================

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b1');
set local role authenticated;

select public.enregistrer_abonnement_push(
    'https://push.example.test/admin-device', 'p256dh-b1', 'auth-b1', 'Test UA');

select moamat_test.expect(
    'push — admin PEUT s''abonner',
    (select count(*) from public.abonnement_push where endpoint = 'https://push.example.test/admin-device') = 1);

-- Idempotent : un second enregistrement ne duplique rien.
select public.enregistrer_abonnement_push(
    'https://push.example.test/admin-device', 'p256dh-b1', 'auth-b1', 'Test UA');

select moamat_test.expect(
    'push — reabonnement idempotent',
    (select count(*) from public.abonnement_push where endpoint = 'https://push.example.test/admin-device') = 1);

-- Endpoint invalide : erreur de paramètre (22023), pas un refus de droit.
do $$
begin
    perform public.enregistrer_abonnement_push('http://push.example.test/x', 'k', 'a');
    raise exception 'FAIL: push — endpoint non https accepte';
exception
    when invalid_parameter_value then
        raise notice 'PASS: push — endpoint non https refuse';
end $$;

reset role;

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b2');
set local role authenticated;
select moamat_test.expect_write_denied(
    'push — lecture NE PEUT PAS s''abonner',
    $q$ select public.enregistrer_abonnement_push('https://push.example.test/b2', 'k', 'a') $q$);
reset role;

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b4');
set local role authenticated;
select moamat_test.expect_write_denied(
    'push — gestion NE PEUT PAS s''abonner',
    $q$ select public.enregistrer_abonnement_push('https://push.example.test/b4', 'k', 'a') $q$);
reset role;

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b5');
set local role authenticated;
select moamat_test.expect_write_denied(
    'push — en_attente NE PEUT PAS s''abonner',
    $q$ select public.enregistrer_abonnement_push('https://push.example.test/b5', 'k', 'a') $q$);
reset role;

-- =============================================================================
--  2. Pas d'écriture directe ; visibilité limitée à ses propres abonnements
-- =============================================================================

-- Abonnement « orphelin » d'un compte lecture, posé en tant que postgres (ex.
-- compte rétrogradé avant l'introduction de la purge).
insert into public.abonnement_push (user_id, endpoint, p256dh, auth)
values ('00000000-0000-0000-0000-0000000000b2', 'https://push.example.test/stale-reader', 'k', 'a');

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b1');
set local role authenticated;

select moamat_test.expect_write_denied(
    'push — INSERT direct refuse (aucune policy d''ecriture)',
    $q$ insert into public.abonnement_push (user_id, endpoint, p256dh, auth)
        values ('00000000-0000-0000-0000-0000000000b1', 'https://push.example.test/direct', 'k', 'a') $q$);

select moamat_test.expect(
    'push — admin ne voit que ses propres abonnements',
    (select count(*) from public.abonnement_push) = 1);

-- supprimer_abonnement_push ne touche pas l'abonnement d'un autre compte.
select public.supprimer_abonnement_push('https://push.example.test/stale-reader');

reset role;

select moamat_test.expect(
    'push — on ne supprime pas l''abonnement d''un autre compte',
    (select count(*) from public.abonnement_push where endpoint = 'https://push.example.test/stale-reader') = 1);

-- =============================================================================
--  4. Destinataires : habilités et actifs uniquement
-- =============================================================================

select moamat_test.expect(
    'push — destinataires : l''admin est inclus',
    exists (select 1 from public.destinataires_push_compte_en_attente()
            where endpoint = 'https://push.example.test/admin-device'));

select moamat_test.expect(
    'push — destinataires : un compte lecture est exclu',
    not exists (select 1 from public.destinataires_push_compte_en_attente()
                where endpoint = 'https://push.example.test/stale-reader'));

select moamat_test.expect(
    'push — destinataires : non executable par authenticated',
    not has_function_privilege('authenticated', 'public.destinataires_push_compte_en_attente()', 'execute'));

select moamat_test.expect(
    'push — destinataires : executable par service_role',
    has_function_privilege('service_role', 'public.destinataires_push_compte_en_attente()', 'execute'));

-- Compte désactivé => exclu.
update auth.users set banned_until = 'infinity' where id = '00000000-0000-0000-0000-0000000000b1';
select moamat_test.expect(
    'push — destinataires : un compte desactive est exclu',
    not exists (select 1 from public.destinataires_push_compte_en_attente()
                where endpoint = 'https://push.example.test/admin-device'));
update auth.users set banned_until = null where id = '00000000-0000-0000-0000-0000000000b1';

-- =============================================================================
--  3. Endpoint rattaché au dernier compte habilité
-- =============================================================================

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b3');
set local role authenticated;
select public.enregistrer_abonnement_push(
    'https://push.example.test/admin-device', 'p256dh-b3', 'auth-b3', 'Test UA');
reset role;

select moamat_test.expect(
    'push — un endpoint reenregistre est rattache au dernier compte habilite',
    (select user_id from public.abonnement_push
     where endpoint = 'https://push.example.test/admin-device') = '00000000-0000-0000-0000-0000000000b3');

-- =============================================================================
--  5. Purge à la rétrogradation
-- =============================================================================

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b1');
set local role authenticated;
select public.enregistrer_abonnement_push(
    'https://push.example.test/admin-second-device', 'p256dh-b1', 'auth-b1');
reset role;

update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000b1';

select moamat_test.expect(
    'push — retrograder un compte purge ses abonnements',
    (select count(*) from public.abonnement_push where user_id = '00000000-0000-0000-0000-0000000000b1') = 0);

select moamat_test.expect(
    'push — la purge ne touche pas les abonnements des autres comptes',
    (select count(*) from public.abonnement_push where user_id = '00000000-0000-0000-0000-0000000000b3') = 1);

-- Un changement de rôle qui CONSERVE le droit ne purge rien.
update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000b1';

select moamat_test.as_user('00000000-0000-0000-0000-0000000000b1');
set local role authenticated;
select public.enregistrer_abonnement_push(
    'https://push.example.test/admin-second-device', 'p256dh-b1', 'auth-b1');
reset role;

update public.utilisateur_role set role = 'admin' where user_id = '00000000-0000-0000-0000-0000000000b1';

select moamat_test.expect(
    'push — passer de super-admin a admin (droit conserve) ne purge rien',
    (select count(*) from public.abonnement_push where user_id = '00000000-0000-0000-0000-0000000000b1') = 1);

-- Suppression de la ligne de rôle (compte révoqué) => purge.
delete from public.utilisateur_role where user_id = '00000000-0000-0000-0000-0000000000b1';

select moamat_test.expect(
    'push — supprimer le role d''un compte purge ses abonnements',
    (select count(*) from public.abonnement_push where user_id = '00000000-0000-0000-0000-0000000000b1') = 0);

-- Retrait de « role.assign » dans la matrice => purge au COMMIT (forcée ici
-- par SET CONSTRAINTS ... IMMEDIATE, le script finissant par ROLLBACK).
-- Le retrait puis la remise dans la même transaction ne purgent rien.
delete from public.role_permission where role = 'super-admin' and permission_code = 'role.assign';
insert into public.role_permission (role, permission_code) values ('super-admin', 'role.assign');
set constraints public.purge_abonnement_push_matrice immediate;

select moamat_test.expect(
    'push — retirer puis remettre role.assign dans la meme transaction ne purge rien',
    (select count(*) from public.abonnement_push where user_id = '00000000-0000-0000-0000-0000000000b3') = 1);

set constraints public.purge_abonnement_push_matrice deferred;
delete from public.role_permission where role = 'super-admin' and permission_code = 'role.assign';
set constraints public.purge_abonnement_push_matrice immediate;

select moamat_test.expect(
    'push — retirer role.assign d''un role dans la matrice purge les abonnements de ce role',
    (select count(*) from public.abonnement_push where user_id = '00000000-0000-0000-0000-0000000000b3') = 0);

select moamat_test.expect(
    'push — la purge interne n''est pas executable par authenticated',
    not has_function_privilege('authenticated', 'public.purger_abonnements_push_sans_droit(uuid)', 'execute'));

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES NOTIFICATIONS SONT PASSES =====';
end $$;

rollback;
