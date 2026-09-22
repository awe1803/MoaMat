-- =============================================================================
--  MoaMat — Tests des préférences d'affichage (db/preferences.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--
--  Pré-requis : db/roles.sql, db/permissions.sql, db/rls.sql, db/preferences.sql.
--  Lancer avec un rôle non restreint :
--      psql "$SUPABASE_DB_URL" -f db/tests/preferences_tests.sql
--
--  Fin attendue :
--      NOTICE:  ===== TOUS LES CONTROLES PREFERENCES SONT PASSES =====
--      ROLLBACK
--
--  Couverture :
--    1. La RPC enregistre le choix de l'appelant, sans exiger de permission.
--    2. Rejouer le même choix ne duplique rien ; décocher remet la valeur à faux.
--    3. Chacun ne lit que sa propre ligne.
--    4. Aucune écriture directe sur public.preference_utilisateur.
--    5. La RPC écrit toujours sur le compte appelant, jamais sur un autre.
--    6. La suppression du compte emporte sa préférence.
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
--  Jeu de données : c1 gestion, c2 lecture, c3 en_attente (jamais activé).
-- -----------------------------------------------------------------------------

do $$
declare
    v_i int := 0;
    v_id uuid;
begin
    foreach v_id in array array[
        '00000000-0000-0000-0000-0000000000c1'::uuid,
        '00000000-0000-0000-0000-0000000000c2'::uuid,
        '00000000-0000-0000-0000-0000000000c3'::uuid
    ]
    loop
        v_i := v_i + 1;
        insert into auth.users (instance_id, id, aud, role, email,
                                encrypted_password, email_confirmed_at,
                                created_at, updated_at,
                                raw_app_meta_data, raw_user_meta_data)
        values ('00000000-0000-0000-0000-000000000000', v_id,
                'authenticated', 'authenticated',
                'pref-' || v_i || '@moamat.test',
                '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);
    end loop;
end $$;

update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000c1';
update public.utilisateur_role set role = 'lecture' where user_id = '00000000-0000-0000-0000-0000000000c2';

-- =============================================================================
--  1 & 2. La RPC enregistre le choix de l'appelant, et lui seul
-- =============================================================================

select moamat_test.as_user('00000000-0000-0000-0000-0000000000c1');
set local role authenticated;

select public.definir_invite_installation_masquee(true);

select moamat_test.expect(
    'preferences — le choix « ne plus afficher » est enregistre',
    (select invite_installation_masquee from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c1'));

select public.definir_invite_installation_masquee(true);

select moamat_test.expect(
    'preferences — rejouer le meme choix ne duplique rien',
    (select count(*) from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c1') = 1);

select public.definir_invite_installation_masquee(false);

select moamat_test.expect(
    'preferences — decocher remet la valeur a faux',
    (select not invite_installation_masquee from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c1'));

select public.definir_invite_installation_masquee(true);

-- =============================================================================
--  4. Aucune écriture directe : la table n'a qu'une policy de lecture
-- =============================================================================

select moamat_test.expect_write_denied(
    'preferences — INSERT direct refuse (aucune policy d''ecriture)',
    $q$ insert into public.preference_utilisateur (user_id, invite_installation_masquee)
        values ('00000000-0000-0000-0000-0000000000c2', true) $q$);

select moamat_test.expect_write_denied(
    'preferences — UPDATE direct refuse, meme sur sa propre ligne',
    $q$ update public.preference_utilisateur set invite_installation_masquee = false
        where user_id = '00000000-0000-0000-0000-0000000000c1' $q$);

select moamat_test.expect_write_denied(
    'preferences — DELETE direct refuse',
    $q$ delete from public.preference_utilisateur
        where user_id = '00000000-0000-0000-0000-0000000000c1' $q$);

reset role;

-- =============================================================================
--  3 & 5. Chacun chez soi
-- =============================================================================

select moamat_test.as_user('00000000-0000-0000-0000-0000000000c2');
set local role authenticated;

select moamat_test.expect(
    'preferences — la ligne d''un autre compte est invisible',
    (select count(*) from public.preference_utilisateur) = 0);

-- La RPC ne prend pas d'identifiant : elle écrit TOUJOURS sur auth.uid().
select public.definir_invite_installation_masquee(true);

select moamat_test.expect(
    'preferences — la RPC ecrit sur le compte appelant',
    (select count(*) from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c2'
       and invite_installation_masquee) = 1);

reset role;

select moamat_test.expect(
    'preferences — le choix du premier compte est intact',
    (select invite_installation_masquee from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c1'));

-- Aucune permission n'est exigée : un compte en attente d'activation retient
-- lui aussi son choix — la préférence ne donne accès à rien.
select moamat_test.as_user('00000000-0000-0000-0000-0000000000c3');
set local role authenticated;

select public.definir_invite_installation_masquee(true);

select moamat_test.expect(
    'preferences — un compte en_attente peut enregistrer son choix',
    (select count(*) from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c3') = 1);

reset role;

-- =============================================================================
--  6. Suppression du compte
-- =============================================================================

delete from auth.users where id = '00000000-0000-0000-0000-0000000000c3';

select moamat_test.expect(
    'preferences — la suppression du compte emporte sa preference',
    (select count(*) from public.preference_utilisateur
     where user_id = '00000000-0000-0000-0000-0000000000c3') = 0);

do $$ begin
    raise notice '===== TOUS LES CONTROLES PREFERENCES SONT PASSES =====';
end $$;

rollback;
