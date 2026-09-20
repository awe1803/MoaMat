-- =============================================================================
--  MoaMat — Tests de db/bouteille_correction.sql (correction super-admin).
--  Script AUTONOME et NON DESTRUCTIF (begin ... rollback). À lancer après
--  db/bouteille_correction.sql avec un rôle non restreint (« postgres »).
-- =============================================================================

begin;

set client_min_messages to notice;

create schema moamat_test;
grant usage on schema moamat_test to authenticated;

create function moamat_test.expect(p_label text, p_got boolean)
returns void language plpgsql as $$
begin
    if p_got then raise notice 'PASS: %', p_label;
    else raise exception 'FAIL: %', p_label; end if;
end $$;

create function moamat_test.expect_raises(p_label text, p_sql text)
returns void language plpgsql as $$
begin
    execute p_sql;
    raise exception 'FAIL: % — aucune erreur levee', p_label;
exception
    when others then
        if sqlerrm like 'FAIL:%' then raise; end if;
        raise notice 'PASS: % (erreur levee: %)', p_label, sqlerrm;
end $$;

grant execute on all functions in schema moamat_test to authenticated;

insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
                        created_at, updated_at, raw_app_meta_data, raw_user_meta_data)
values
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f1', 'authenticated', 'authenticated',
     'correction-super@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f2', 'authenticated', 'authenticated',
     'correction-admin@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'super-admin' where user_id = '00000000-0000-0000-0000-0000000000f1';
update public.utilisateur_role set role = 'admin'       where user_id = '00000000-0000-0000-0000-0000000000f2';

insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999400001, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999400001, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

-- Un admin ne peut pas corriger.
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f2', 'email', 'correction-admin@moamat.test', 'role', 'authenticated')::text, true);
set local role authenticated;

select moamat_test.expect_raises('admin — correction refusee',
    $q$ select public.corriger_bouteille(999400001, 'deco_o2', 'acier', null, date '2024-01-15', date '2024-01-15', 'x', null, null) $q$);

reset role;
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f1', 'email', 'correction-super@moamat.test', 'role', 'authenticated')::text, true);
set local role authenticated;

-- Correction famille + matière + compteur reculé.
select public.corriger_bouteille(999400001, 'deco_o2', 'alu', null, date '2023-05-01', date '2024-01-15', 'erreur de reprise', null, null);

select moamat_test.expect('super-admin — famille, matiere et compteur corriges (recul autorise)',
    (select famille from public.item_bouteille where item_id = 999400001) = 'deco_o2'
    and (select matiere from public.item_bouteille where item_id = 999400001) = 'alu'
    and (select date_dernier_controle_optique from public.item_bouteille where item_id = 999400001) = date '2023-05-01');

reset role;
select moamat_test.expect('super-admin — audite',
    exists (select 1 from public.audit_log
            where action = 'bouteille.correction' and entity_id = '999400001'));
set local role authenticated;

-- Changement de statut : transition journalisée.
select public.corriger_bouteille(999400001, 'deco_o2', 'alu', 'prete', date '2023-05-01', date '2024-01-15', 'statut reel', null, null);

select moamat_test.expect('super-admin — statut change via la transition normale',
    (select statut_code from public.item where id = 999400001) = 'prete'
    and exists (select 1 from public.item_transition where item_id = 999400001 and nouveau_statut = 'prete'));

select moamat_test.expect_raises('motif obligatoire',
    $q$ select public.corriger_bouteille(999400001, 'plongee', 'alu', null, date '2023-05-01', date '2024-01-15', '  ', null, null) $q$);
select moamat_test.expect_raises('aucune modification refusee',
    $q$ select public.corriger_bouteille(999400001, 'deco_o2', 'alu', null, date '2023-05-01', date '2024-01-15', 'rien', null, null) $q$);
select moamat_test.expect_raises('date future refusee',
    $q$ select public.corriger_bouteille(999400001, 'deco_o2', 'alu', null, current_date + 2, date '2024-01-15', 'x', null, null) $q$);
select moamat_test.expect_raises('famille invalide refusee',
    $q$ select public.corriger_bouteille(999400001, 'helium', 'alu', null, date '2023-05-01', date '2024-01-15', 'x', null, null) $q$);

reset role;

do $$ begin raise notice '===== TOUS LES CONTROLES SONT PASSES ====='; end $$;

rollback;
