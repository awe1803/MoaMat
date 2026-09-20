-- =============================================================================
--  MoaMat — Tests de la chronologie bouteille (db/bouteille_evenement.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/transform_item.sql, db/rls.sql, db/audit.sql,
--      db/item_etat.sql, db/item_bouteille.sql, db/bouteille_evenement.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/bouteille_evenement_tests.sql
--
--  Couverture :
--    1. Reprise : 412 = repris + rejetés, écart 0 ; les 12 événements de l'id
--       orphelin 49 sont dans item_reject (verbatim) ; les ids 24 et 25 sont
--       rattachés aux items de bouteille_sortie_inventaire ; AUCUN événement de
--       l'id 49 n'est rattaché à la bouteille dont le n° PEINT est 49 (piège de
--       la jointure) ; rejouer la reprise ne duplique rien.
--    2. Requalification : "conforme" met à jour le compteur sans jamais
--       reculer ; "echec" ne touche ni compteur ni statut ; résultat, date et
--       coût validés ; refus pour un rôle "lecture".
--    3. Incident : consigné sans changer le statut ; description obligatoire.
--    4. Aucune écriture directe (pas de policy insert).
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
--  1. Reprise (en rôle non restreint, avant tout « set role »)
-- -----------------------------------------------------------------------------

select moamat_test.expect(
    'reprise — 412 evenements source',
    (select count(*) from public.bouteille_requalification) = 412);

select moamat_test.expect(
    'reprise — repris + rejetes = source (ecart 0)',
    (select count(*) from public.bouteille_evenement where origine_id is not null)
  + (select count(distinct origine_id) from public.item_reject where origine_table = 'bouteille_requalification')
  = (select count(*) from public.bouteille_requalification));

select moamat_test.expect(
    'reprise — les 12 evenements de l''id orphelin 49 sont dans item_reject',
    (select count(*) from public.item_reject
      where origine_table = 'bouteille_requalification' and colonne = 'bouteille_id' and valeur_brute = '49') = 12);

select moamat_test.expect(
    'reprise — l''id 49 n''a aucun evenement rattache',
    not exists (
        select 1 from public.bouteille_evenement e
        join public.bouteille_requalification r on r.id = e.origine_id
        where r.bouteille_id = 49));

select moamat_test.expect(
    'reprise — 21 evenements des ids 24/25 rattaches aux items de bouteille_sortie_inventaire',
    (select count(*)
       from public.bouteille_evenement e
       join public.bouteille_requalification r on r.id = e.origine_id
       join public.item i on i.id = e.item_id
      where r.bouteille_id in (24, 25)
        and i.origine_table = 'bouteille_sortie_inventaire'
        and i.origine_id = r.bouteille_id) = 21);

-- Piège de la jointure : la bouteille dont le n° PEINT est 49 (id Access 18)
-- ne doit porter que les événements de l'id 18, jamais ceux de l'id 49.
select moamat_test.expect(
    'reprise — jointure par ID interne : la bouteille au n° peint 49 ne recoit que ses propres evenements',
    not exists (
        select 1
        from public.bouteille_evenement e
        join public.item i on i.id = e.item_id and i.origine_table = 'bouteille'
        join public.bouteille_requalification r on r.id = e.origine_id
        where r.bouteille_id is distinct from i.origine_id));

-- Idempotence : rejouer la reprise (ré-exécution du fichier) ne duplique rien.
do $$
declare
    v_avant bigint;
begin
    select count(*) into v_avant from public.bouteille_evenement;
    insert into public.bouteille_evenement (item_id, type, date_evenement, origine_id)
    select e.item_id, e.type, e.date_evenement, e.origine_id from public.bouteille_evenement e
    where e.origine_id is not null
    on conflict (origine_id) do nothing;
    if (select count(*) from public.bouteille_evenement) <> v_avant then
        raise exception 'FAIL: reprise non idempotente';
    end if;
    raise notice 'PASS: reprise — idempotente (origine_id unique)';
end $$;

-- -----------------------------------------------------------------------------
--  Fixture : un compte "gestion" et un compte "lecture", deux bouteilles.
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000e1',
     'authenticated', 'authenticated', 'evenement-gestion@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000e2',
     'authenticated', 'authenticated', 'evenement-lecture@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000e1';
update public.utilisateur_role set role = 'lecture' where user_id = '00000000-0000-0000-0000-0000000000e2';

insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999300001, 'bouteille', 'en_stock', true), (999300002, 'bouteille', 'en_stock', true),
       (999300003, 'bouteille', 'en_stock', false);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999300001, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
       (999300002, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
       (999300003, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000e1', 'email', 'evenement-gestion@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

-- =============================================================================
--  2. Requalification
-- =============================================================================

select public.enregistrer_requalification_bouteille(
    999300001, 'controle_optique', date '2025-06-10', 'conforme', 'Apragaz', 21.32, 'Lb.134008', null);

select moamat_test.expect(
    'requalification conforme — evenement historise',
    exists (select 1 from public.bouteille_evenement
            where item_id = 999300001 and type = 'controle_optique' and resultat = 'conforme'
              and cout_eur = 21.32 and num_certificat = 'Lb.134008'));

select moamat_test.expect(
    'requalification conforme — compteur optique mis a jour, hydraulique intact',
    (select date_dernier_controle_optique from public.item_bouteille where item_id = 999300001) = date '2025-06-10'
    and (select date_dernier_controle_hydraulique from public.item_bouteille where item_id = 999300001) = date '2024-01-15');

-- Date antérieure au dernier contrôle connu : historisée, mais l'échéance ne recule pas.
select public.enregistrer_requalification_bouteille(
    999300001, 'controle_optique', date '2023-01-01', 'conforme', null, null, null, 'saisie tardive');

select moamat_test.expect(
    'requalification conforme anterieure — compteur jamais recule',
    (select date_dernier_controle_optique from public.item_bouteille where item_id = 999300001) = date '2025-06-10');

-- "echec" : ni compteur ni statut.
select public.enregistrer_requalification_bouteille(
    999300002, 'controle_hydraulique', date '2025-06-10', 'echec', 'Apragaz', 35.82, null, 'filet endommage');

select moamat_test.expect(
    'requalification echec — evenement historise',
    exists (select 1 from public.bouteille_evenement
            where item_id = 999300002 and type = 'controle_hydraulique' and resultat = 'echec'));

select moamat_test.expect(
    'requalification echec — compteur et statut inchanges',
    (select date_dernier_controle_hydraulique from public.item_bouteille where item_id = 999300002) = date '2024-01-15'
    and (select statut_code from public.item where id = 999300002) = 'en_stock');

select moamat_test.expect_raises('requalification — resultat obligatoire',
    $q$ select public.enregistrer_requalification_bouteille(999300001, 'controle_optique', date '2025-06-10', null, null, null, null, null) $q$);
select moamat_test.expect_raises('requalification — type invalide refuse',
    $q$ select public.enregistrer_requalification_bouteille(999300001, 'mise_en_service', date '2025-06-10', 'conforme', null, null, null, null) $q$);
select moamat_test.expect_raises('requalification — date obligatoire',
    $q$ select public.enregistrer_requalification_bouteille(999300001, 'controle_optique', null, 'conforme', null, null, null, null) $q$);
select moamat_test.expect_raises('requalification — date future refusee',
    $q$ select public.enregistrer_requalification_bouteille(999300001, 'controle_optique', current_date + 1, 'conforme', null, null, null, null) $q$);
select moamat_test.expect_raises('requalification — cout negatif refuse',
    $q$ select public.enregistrer_requalification_bouteille(999300001, 'controle_optique', date '2025-06-10', 'conforme', null, -1, null, null) $q$);
select moamat_test.expect_raises('requalification — bouteille inconnue refusee',
    $q$ select public.enregistrer_requalification_bouteille(999399999, 'controle_optique', date '2025-06-10', 'conforme', null, null, null, null) $q$);


-- Bouteille désactivée : requalification refusée, incident toléré.
select moamat_test.expect_raises('requalification — bouteille desactivee refusee',
    $q$ select public.enregistrer_requalification_bouteille(999300003, 'controle_optique', date '2025-06-10', 'conforme', null, null, null, null) $q$);

-- Idempotence : même request_id = un seul événement, compteur et audit intacts.
do $$
declare
    v_a bigint;
    v_b bigint;
    v_req uuid := '00000000-0000-0000-0000-00000000aaaa';
begin
    v_a := public.enregistrer_requalification_bouteille(
        999300002, 'controle_optique', date '2025-07-01', 'conforme', null, null, null, null, v_req);
    v_b := public.enregistrer_requalification_bouteille(
        999300002, 'controle_optique', date '2025-07-01', 'conforme', null, null, null, null, v_req);
    if v_a is distinct from v_b then raise exception 'FAIL: rejeu — ids differents'; end if;
    if (select count(*) from public.bouteille_evenement where request_id = v_req) <> 1 then
        raise exception 'FAIL: rejeu — doublon cree';
    end if;
    raise notice 'PASS: requalification — rejeu du meme request_id = un seul evenement';
end $$;

-- Date « aujourd'hui » à Paris acceptée (jamais refusée à cause de l'UTC).
select public.signaler_incident_bouteille(999300001, (now() at time zone 'Europe/Paris')::date, 'incident du jour');
select moamat_test.expect('incident — date du jour (Paris) acceptee',
    exists (select 1 from public.bouteille_evenement where item_id = 999300001 and remarque = 'incident du jour'));

-- Incident toléré sur bouteille désactivée (régularisation d'historique).
select public.signaler_incident_bouteille(999300003, date '2025-06-11', 'regularisation');
select moamat_test.expect('incident — bouteille desactivee acceptee',
    exists (select 1 from public.bouteille_evenement where item_id = 999300003 and type = 'incident'));
-- =============================================================================
--  3. Incident
-- =============================================================================

select public.signaler_incident_bouteille(999300002, date '2025-06-11', 'Robinet grippé');

select moamat_test.expect(
    'incident — consigne dans la chronologie, statut inchange',
    exists (select 1 from public.bouteille_evenement
            where item_id = 999300002 and type = 'incident' and remarque = 'Robinet grippé')
    and (select statut_code from public.item where id = 999300002) = 'en_stock');

select moamat_test.expect_raises('incident — description obligatoire',
    $q$ select public.signaler_incident_bouteille(999300002, date '2025-06-11', '  ') $q$);

-- =============================================================================
--  4. Pas d'écriture directe ; droits
-- =============================================================================

select moamat_test.expect_raises('ecriture directe — insert refuse (aucune policy)',
    $q$ insert into public.bouteille_evenement (item_id, type, date_evenement) values (999300001, 'incident', current_date) $q$);

reset role;
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000e2', 'email', 'evenement-lecture@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('lecture — voit la chronologie',
    exists (select 1 from public.bouteille_evenement where item_id = 999300001));
select moamat_test.expect_raises('lecture — requalification refusee',
    $q$ select public.enregistrer_requalification_bouteille(999300001, 'controle_optique', date '2025-06-12', 'conforme', null, null, null, null) $q$);
select moamat_test.expect_raises('lecture — incident refuse',
    $q$ select public.signaler_incident_bouteille(999300001, date '2025-06-12', 'x') $q$);

reset role;

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
