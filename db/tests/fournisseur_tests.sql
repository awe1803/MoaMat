-- =============================================================================
--  MoaMat — Tests des fournisseurs (db/fournisseur.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--
--  Pré-requis : avoir exécuté, dans l'ordre, db/schema.sql, db/initial_load.sql,
--      db/roles.sql, db/permissions.sql, db/model_item.sql, db/transform_item.sql,
--      db/rls.sql, db/audit.sql, db/item_etat.sql, db/item_bouteille.sql,
--      db/fournisseur.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/fournisseur_tests.sql
--
--  Couverture :
--    1. Amorçage Apragaz : 3 prestations, historique de tarifs repris.
--    2. Contraintes : nom obligatoire/unique (casse), email et site web valides.
--    3. Un tarif exige une prestation proposée ; résolveur de prix daté.
--       Tarification des campagnes branchée sur les prix Apragaz (§7).
--    4. Droits : lecture seule (lecture), écriture (gestion), suppression
--       d'un tarif réservée à admin.
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

grant execute on all functions in schema moamat_test to authenticated, anon;

-- -----------------------------------------------------------------------------
--  1. Amorçage Apragaz
-- -----------------------------------------------------------------------------

select moamat_test.expect(
    'amorcage — Apragaz existe',
    (select count(*) from public.ref_fournisseur where nom = 'Apragaz') = 1);

select moamat_test.expect(
    'amorcage — Apragaz propose les 3 prestations',
    (select count(*) from public.ref_fournisseur_prestation p
       join public.ref_fournisseur f on f.id = p.fournisseur_id
      where f.nom = 'Apragaz') = 3);

select moamat_test.expect(
    'amorcage — historique de tarifs repris de ref_tarif_apragaz',
    (select count(*) from public.ref_fournisseur_tarif) >= (select count(*) from public.ref_tarif_apragaz));

select moamat_test.expect(
    'amorcage — prix en vigueur RR au 2025-06-01 = 21.32',
    public.fournisseur_tarif_en_vigueur(
        (select id from public.ref_fournisseur where nom = 'Apragaz'), 'rr', date '2025-06-01') = 21.32);

-- Tarification des campagnes (§7) : le prix en vigueur vient du fournisseur Apragaz.
select moamat_test.expect(
    'campagnes — prix RR au 2025-06-01 identique a l''ancien referentiel (amorcage)',
    public.campagne_tarif_prestation('rr', date '2025-06-01') = public.bouteille_tarif_apragaz('rr', date '2025-06-01'));

insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
select id, 'rr', 99.00, date '2030-01-01' from public.ref_fournisseur where nom = 'Apragaz';

select moamat_test.expect(
    'campagnes — un nouveau prix du fournisseur Apragaz est applique',
    public.campagne_tarif_prestation('rr', date '2030-06-01') = 99.00);

select moamat_test.expect(
    'campagnes — aucun repli sur l''ancien referentiel (il ne voit pas le nouveau prix)',
    public.bouteille_tarif_apragaz('rr', date '2030-06-01') <> 99.00);

select moamat_test.expect(
    'campagnes — sans prix effectif, le resultat est NULL (pas de valeur perimee)',
    public.campagne_tarif_prestation('rr', date '2000-01-01') is null);

select moamat_test.expect(
    'campagnes — la vue v_tarif_campagne expose le nouveau prix',
    (select count(*) from public.v_tarif_campagne
      where type_prestation = 'rr' and prix_eur = 99.00 and date_effet = date '2030-01-01') = 1);

-- -----------------------------------------------------------------------------
--  2. Contraintes
-- -----------------------------------------------------------------------------

select moamat_test.expect_raises(
    'nom vide refuse',
    $q$ insert into public.ref_fournisseur (nom) values ('   ') $q$);

select moamat_test.expect_raises(
    'nom en double refuse (insensible a la casse)',
    $q$ insert into public.ref_fournisseur (nom) values ('  APRAGAZ ') $q$);

select moamat_test.expect_raises(
    'email invalide refuse',
    $q$ insert into public.ref_fournisseur (nom, email) values ('Test email', 'pas-un-email') $q$);

select moamat_test.expect_raises(
    'site web sans http(s) refuse',
    $q$ insert into public.ref_fournisseur (nom, site_web) values ('Test web', 'javascript:alert(1)') $q$);

insert into public.ref_fournisseur (nom, adresse, telephone, email, personne_contact, site_web)
values ('Fournisseur test', '1 rue du Test, 1000 Bruxelles', '+32 2 000 00 00',
        'contact@test.example', 'Jeanne Test', 'https://test.example');

select moamat_test.expect(
    'fournisseur complet cree',
    (select count(*) from public.ref_fournisseur where nom = 'Fournisseur test') = 1);

-- -----------------------------------------------------------------------------
--  3. Tarifs
-- -----------------------------------------------------------------------------

select moamat_test.expect_raises(
    'tarif refuse pour une prestation non proposee',
    $q$ insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
        values ((select id from public.ref_fournisseur where nom = 'Fournisseur test'), 'rr', 10, date '2026-01-01') $q$);

insert into public.ref_fournisseur_prestation (fournisseur_id, type_prestation)
values ((select id from public.ref_fournisseur where nom = 'Fournisseur test'), 'rr');

insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
select id, 'rr', 10.00, date '2026-01-01' from public.ref_fournisseur where nom = 'Fournisseur test';
insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
select id, 'rr', 12.50, date '2027-01-01' from public.ref_fournisseur where nom = 'Fournisseur test';

select moamat_test.expect(
    'resolveur — avant le premier tarif => NULL',
    public.fournisseur_tarif_en_vigueur(
        (select id from public.ref_fournisseur where nom = 'Fournisseur test'), 'rr', date '2025-12-31') is null);

select moamat_test.expect(
    'resolveur — ancien prix encore en vigueur avant la nouvelle date',
    public.fournisseur_tarif_en_vigueur(
        (select id from public.ref_fournisseur where nom = 'Fournisseur test'), 'rr', date '2026-12-31') = 10.00);

select moamat_test.expect(
    'resolveur — nouveau prix a partir de sa date d''effet',
    public.fournisseur_tarif_en_vigueur(
        (select id from public.ref_fournisseur where nom = 'Fournisseur test'), 'rr', date '2027-01-01') = 12.50);

select moamat_test.expect_raises(
    'meme date d''effet refusee (unique)',
    $q$ insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
        select id, 'rr', 99, date '2027-01-01' from public.ref_fournisseur where nom = 'Fournisseur test' $q$);

select moamat_test.expect_raises(
    'prix negatif refuse',
    $q$ insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
        select id, 'rr', -1, date '2028-01-01' from public.ref_fournisseur where nom = 'Fournisseur test' $q$);

-- -----------------------------------------------------------------------------
--  4. Droits — trois comptes : lecture, gestion, admin
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f1',
     'authenticated', 'authenticated', 'fournisseur-test-lecture@moamat.test',
     '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f2',
     'authenticated', 'authenticated', 'fournisseur-test-gestion@moamat.test',
     '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f3',
     'authenticated', 'authenticated', 'fournisseur-test-admin@moamat.test',
     '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'lecture' where user_id = '00000000-0000-0000-0000-0000000000f1';
update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000f2';
update public.utilisateur_role set role = 'admin'   where user_id = '00000000-0000-0000-0000-0000000000f3';

-- --- lecture : voit, n'écrit pas
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f1', 'email', 'fournisseur-test-lecture@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('lecture — role courant', public.moamat_current_role() = 'lecture');

select moamat_test.expect(
    'lecture — voit les fournisseurs',
    (select count(*) from public.ref_fournisseur) >= 2);

select moamat_test.expect_raises(
    'lecture — ne peut pas creer un fournisseur',
    $q$ insert into public.ref_fournisseur (nom) values ('Interdit') $q$);

select moamat_test.expect_write_denied(
    'lecture — ne peut pas modifier un fournisseur',
    $q$ update public.ref_fournisseur set telephone = '0' where nom = 'Fournisseur test' $q$);

reset role;

-- --- gestion : crée, modifie, ajoute un tarif ; ne supprime pas
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f2', 'email', 'fournisseur-test-gestion@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('gestion — role courant', public.moamat_current_role() = 'gestion');

insert into public.ref_fournisseur (nom) values ('Cree par gestion');

select moamat_test.expect(
    'gestion — peut creer un fournisseur',
    (select count(*) from public.ref_fournisseur where nom = 'Cree par gestion') = 1);

update public.ref_fournisseur set telephone = '+32 2 111 11 11' where nom = 'Fournisseur test';

select moamat_test.expect(
    'gestion — peut modifier un fournisseur',
    (select telephone from public.ref_fournisseur where nom = 'Fournisseur test') = '+32 2 111 11 11');

insert into public.ref_fournisseur_tarif (fournisseur_id, type_prestation, prix_eur, date_effet)
select id, 'rr', 13.00, date '2028-01-01' from public.ref_fournisseur where nom = 'Fournisseur test';

select moamat_test.expect(
    'gestion — peut ajouter un nouveau prix',
    (select count(*) from public.ref_fournisseur_tarif t
       join public.ref_fournisseur f on f.id = t.fournisseur_id
      where f.nom = 'Fournisseur test' and t.date_effet = date '2028-01-01') = 1);

select moamat_test.expect_raises(
    'gestion — ne peut pas retirer une prestation qui a des prix (historique protege)',
    $q$ delete from public.ref_fournisseur_prestation
        where type_prestation = 'rr'
          and fournisseur_id = (select id from public.ref_fournisseur where nom = 'Fournisseur test') $q$);

select moamat_test.expect_write_denied(
    'gestion — ne peut pas modifier un tarif existant (aucune policy UPDATE)',
    $q$ update public.ref_fournisseur_tarif set prix_eur = 0 where date_effet = date '2028-01-01' $q$);

select moamat_test.expect_write_denied(
    'gestion — ne peut pas supprimer un tarif',
    $q$ delete from public.ref_fournisseur_tarif where date_effet = date '2028-01-01' $q$);

select moamat_test.expect_write_denied(
    'gestion — ne peut pas supprimer un fournisseur',
    $q$ delete from public.ref_fournisseur where nom = 'Cree par gestion' $q$);

reset role;

-- --- admin : supprime un tarif saisi par erreur, puis un fournisseur
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f3', 'email', 'fournisseur-test-admin@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('admin — role courant', public.moamat_current_role() = 'admin');

delete from public.ref_fournisseur_tarif where date_effet = date '2028-01-01';

select moamat_test.expect(
    'admin — peut supprimer un tarif saisi par erreur',
    (select count(*) from public.ref_fournisseur_tarif where date_effet = date '2028-01-01') = 0);

delete from public.ref_fournisseur where nom = 'Fournisseur test';

select moamat_test.expect(
    'admin — suppression du fournisseur en cascade (prestations et tarifs)',
    (select count(*) from public.ref_fournisseur_prestation p
       left join public.ref_fournisseur f on f.id = p.fournisseur_id
      where f.id is null) = 0);

reset role;

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
