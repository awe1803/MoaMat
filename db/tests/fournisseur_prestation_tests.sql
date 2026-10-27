-- =============================================================================
--  MoaMat — Tests du catalogue de prestations par fournisseur
--  (db/fournisseur_prestation.sql) et de son intégration aux campagnes
--  (db/campagne.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/transform_item.sql, db/rls.sql, db/audit.sql,
--      db/item_etat.sql, db/item_bouteille.sql, db/fournisseur.sql, db/fournisseur_prestation.sql,
--      db/campagne.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/fournisseur_prestation_tests.sql
--
--  Couverture :
--    1. Amorçage Apragaz : 3 prestations, huile et eau DISTINCTES (méthode
--       portée par la donnée, tarifs différents).
--    2. Fournisseur (ref_fournisseur) : coordonnées valides (email, site web), doublon refusé, archivage.
--    3. Prestation : libre (sans tarif) ou de contrôle ; huile/eau obligatoire
--       si hydraulique, interdite sinon ; libellé unique par fournisseur.
--    4. Tarifs datés : tarif en vigueur, prestation sans tarif, append-only ; un tarif saisi par erreur se supprime (admin).
--    5. Droits : lecture seule pour "lecture", pas d'écriture directe,
--       suppression réservée à "fournisseur.delete".
--    6. Campagne : prestation d'un autre fournisseur / libre / désactivée
--       refusée, fournisseur inconnu refusé, coût estimé NULL sans tarif,
--       type de contrôle figé et suppression refusée une fois utilisée.
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

create function moamat_test.presta(p_fournisseur text, p_libelle text)
returns bigint language sql stable as $$
    select p.id from public.fournisseur_prestation p
    where p.fournisseur_id = public.resoudre_fournisseur(p_fournisseur)
      and lower(p.libelle) = lower(p_libelle)
$$;

grant execute on all functions in schema moamat_test to authenticated, anon;

create table moamat_test.t_ids (k text primary key, id bigint);
grant all on all tables in schema moamat_test to authenticated, anon;

-- -----------------------------------------------------------------------------
--  Fixture : trois comptes (lecture / gestion / admin), deux bouteilles.
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f1',
     'authenticated', 'authenticated', 'fp-lecture@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f2',
     'authenticated', 'authenticated', 'fp-gestion@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000f3',
     'authenticated', 'authenticated', 'fp-admin@moamat.test', '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'lecture' where user_id = '00000000-0000-0000-0000-0000000000f1';
update public.utilisateur_role set role = 'gestion' where user_id = '00000000-0000-0000-0000-0000000000f2';
update public.utilisateur_role set role = 'admin'   where user_id = '00000000-0000-0000-0000-0000000000f3';

insert into public.item (id, famille, statut_code, actif)
overriding system value
values
    (999300001, 'bouteille', 'en_stock', true),
    (999300002, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values
    (999300001, 'plongee', 'acier', date '2024-01-15', date '2024-01-15'),
    (999300002, 'plongee', 'acier', date '2024-01-15', date '2024-01-15');

-- =============================================================================
--  1. Amorçage Apragaz (rôle non restreint)
-- =============================================================================

select moamat_test.expect(
    'amorcage — Apragaz a ses 3 prestations de controle',
    (select count(*) from public.fournisseur_prestation
     where fournisseur_id = public.resoudre_fournisseur('Apragaz')
       and code in ('rr', 'hydraulique_huile', 'hydraulique_eau')) = 3);

select moamat_test.expect(
    'amorcage — rr = optique, sans methode',
    (select controle_cylindre = 'optique' and methode_hydraulique is null
     from public.fournisseur_prestation
     where fournisseur_id = public.resoudre_fournisseur('Apragaz') and code = 'rr'));

select moamat_test.expect(
    'amorcage — huile et eau sont deux prestations hydrauliques distinctes, methode portee par la donnee',
    (select count(*) from public.fournisseur_prestation
     where fournisseur_id = public.resoudre_fournisseur('Apragaz')
       and ((code = 'hydraulique_huile' and controle_cylindre = 'hydraulique' and methode_hydraulique = 'huile')
         or (code = 'hydraulique_eau'   and controle_cylindre = 'hydraulique' and methode_hydraulique = 'eau'))) = 2);

select moamat_test.expect(
    'amorcage — huile et eau gardent des tarifs distincts (ecart reel, jamais fusionnes)',
    public.prestation_tarif(moamat_test.presta('Apragaz', 'Hydraulique (huile)'), date '2023-06-01') = 32.61
    and public.prestation_tarif(moamat_test.presta('Apragaz', 'Hydraulique (eau)'), date '2023-06-01') = 45.92);

select moamat_test.expect(
    'amorcage — tarif rr 2026',
    public.prestation_tarif(moamat_test.presta('Apragaz', 'RR (contrôle optique)'), date '2026-06-01') = 21.32);

-- Contrainte de base : hydraulique sans méthode, méthode sans hydraulique.
select moamat_test.expect_raises(
    'contrainte — hydraulique sans methode refusee',
    $q$ insert into public.fournisseur_prestation (fournisseur_id, libelle, controle_cylindre)
        values (public.resoudre_fournisseur('Apragaz'), 'Hydraulique sans methode', 'hydraulique') $q$);

select moamat_test.expect_raises(
    'contrainte — methode huile/eau sur une prestation non hydraulique refusee',
    $q$ insert into public.fournisseur_prestation (fournisseur_id, libelle, controle_cylindre, methode_hydraulique)
        values (public.resoudre_fournisseur('Apragaz'), 'Optique avec methode', 'optique', 'huile') $q$);

-- =============================================================================
--  2. Fournisseur — gestion
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f2', 'email', 'fp-gestion@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('setup — role courant = gestion', public.moamat_current_role() = 'gestion');

-- Coordonnées : table public.ref_fournisseur (db/fournisseur.sql), écriture
-- directe sous RLS (fournisseur.create / update / delete).
with i as (
    insert into public.ref_fournisseur (nom, adresse, telephone, email, personne_contact, site_web)
    values ('Fournisseur Test', '1 rue du Test, 1000 Bruxelles', '+32 2 000 00 00',
            'contact@test.example', 'Jeanne Test', 'https://test.example')
    returning id
)
insert into moamat_test.t_ids select 'fournisseur', id from i;

select moamat_test.expect(
    'fournisseur — complet cree (contact, telephone, email, site web, adresse)',
    (select count(*) from public.ref_fournisseur where nom = 'Fournisseur Test' and actif) = 1);

select moamat_test.expect_raises(
    'fournisseur — doublon (casse/espaces) refuse',
    $q$ insert into public.ref_fournisseur (nom) values ('  fournisseur TEST ') $q$);

select moamat_test.expect_raises(
    'fournisseur — nom vide refuse',
    $q$ insert into public.ref_fournisseur (nom) values ('   ') $q$);

select moamat_test.expect_raises(
    'fournisseur — email invalide refuse',
    $q$ insert into public.ref_fournisseur (nom, email) values ('Test email', 'pas-un-email') $q$);

select moamat_test.expect_raises(
    'fournisseur — site web sans http(s) refuse',
    $q$ insert into public.ref_fournisseur (nom, site_web) values ('Test web', 'javascript:alert(1)') $q$);

update public.ref_fournisseur set telephone = '+32 2 111 11 11'
where id = (select id from moamat_test.t_ids where k = 'fournisseur');

select moamat_test.expect(
    'fournisseur — modifie',
    (select telephone from public.ref_fournisseur where id = (select id from moamat_test.t_ids where k = 'fournisseur')) = '+32 2 111 11 11');

-- Archivage : le fournisseur n'est plus proposé aux campagnes, l'historique reste.
update public.ref_fournisseur set actif = false
where id = (select id from moamat_test.t_ids where k = 'fournisseur');

select moamat_test.expect_raises(
    'fournisseur archive — refuse pour une nouvelle campagne',
    $q$ select public.creer_campagne('Fournisseur Test', array[999300001]) $q$);

update public.ref_fournisseur set actif = true
where id = (select id from moamat_test.t_ids where k = 'fournisseur');

-- =============================================================================
--  3. Prestations
-- =============================================================================

do $$
declare
    v_f bigint := (select id from moamat_test.t_ids where k = 'fournisseur');
begin
    insert into moamat_test.t_ids values
        ('libre',   public.enregistrer_prestation_fournisseur(null, v_f, 'Entretien compresseur', null, null, true, 'Forfait annuel')),
        ('optique', public.enregistrer_prestation_fournisseur(null, v_f, 'RR test',               'optique', null, true, null)),
        ('huile',   public.enregistrer_prestation_fournisseur(null, v_f, 'Hydro huile test',     'hydraulique', 'huile', true, null)),
        ('eau',     public.enregistrer_prestation_fournisseur(null, v_f, 'Hydro eau test',       'hydraulique', 'eau', true, null));
end $$;

select moamat_test.expect(
    'prestation — libre creee sans controle ni tarif',
    (select controle_cylindre is null and methode_hydraulique is null
     from public.fournisseur_prestation where id = (select id from moamat_test.t_ids where k = 'libre'))
    and public.prestation_tarif((select id from moamat_test.t_ids where k = 'libre')) is null);

select moamat_test.expect(
    'prestation — huile et eau enregistrees comme deux prestations distinctes',
    (select methode_hydraulique from public.fournisseur_prestation where id = (select id from moamat_test.t_ids where k = 'huile')) = 'huile'
    and (select methode_hydraulique from public.fournisseur_prestation where id = (select id from moamat_test.t_ids where k = 'eau')) = 'eau');

select moamat_test.expect_raises(
    'prestation — hydraulique sans methode refusee (jamais de defaut huile/eau)',
    format($q$ select public.enregistrer_prestation_fournisseur(null, %s, 'Hydro sans methode', 'hydraulique', null, true, null) $q$,
           (select id from moamat_test.t_ids where k = 'fournisseur')));

select moamat_test.expect_raises(
    'prestation — methode sur une prestation optique refusee',
    format($q$ select public.enregistrer_prestation_fournisseur(null, %s, 'Optique huile', 'optique', 'huile', true, null) $q$,
           (select id from moamat_test.t_ids where k = 'fournisseur')));

select moamat_test.expect_raises(
    'prestation — methode invalide refusee',
    format($q$ select public.enregistrer_prestation_fournisseur(null, %s, 'Hydro vapeur', 'hydraulique', 'vapeur', true, null) $q$,
           (select id from moamat_test.t_ids where k = 'fournisseur')));

select moamat_test.expect_raises(
    'prestation — libelle en doublon (casse) refuse pour le meme fournisseur',
    format($q$ select public.enregistrer_prestation_fournisseur(null, %s, 'rr TEST', 'optique', null, true, null) $q$,
           (select id from moamat_test.t_ids where k = 'fournisseur')));

select moamat_test.expect_raises(
    'prestation — libelle vide refuse',
    format($q$ select public.enregistrer_prestation_fournisseur(null, %s, '  ', null, null, true, null) $q$,
           (select id from moamat_test.t_ids where k = 'fournisseur')));

-- Le même libellé est permis chez un AUTRE fournisseur.
select moamat_test.expect(
    'prestation — meme libelle autorise chez un autre fournisseur',
    public.enregistrer_prestation_fournisseur(null, public.resoudre_fournisseur('Apragaz'), 'RR test', 'optique', null, true, null) is not null);

-- Modification d'une prestation libre (libellé, commentaire).
select public.enregistrer_prestation_fournisseur(
    (select id from moamat_test.t_ids where k = 'libre'), null, 'Entretien compresseur annuel', null, null, true, 'Maj');

select moamat_test.expect(
    'prestation — modifiee',
    (select libelle from public.fournisseur_prestation where id = (select id from moamat_test.t_ids where k = 'libre')) = 'Entretien compresseur annuel');

-- =============================================================================
--  4. Tarifs datés
-- =============================================================================

select public.definir_tarif_prestation((select id from moamat_test.t_ids where k = 'huile'), 30.00, date '2025-01-01');
select public.definir_tarif_prestation((select id from moamat_test.t_ids where k = 'huile'), 33.50, date '2026-01-01');
select public.definir_tarif_prestation((select id from moamat_test.t_ids where k = 'eau'),   44.00, date '2025-01-01');

select moamat_test.expect(
    'tarif — le plus recent a la date demandee est retenu',
    public.prestation_tarif((select id from moamat_test.t_ids where k = 'huile'), date '2025-06-01') = 30.00
    and public.prestation_tarif((select id from moamat_test.t_ids where k = 'huile'), date '2026-06-01') = 33.50);

select moamat_test.expect(
    'tarif — NULL avant la premiere date d''effet',
    public.prestation_tarif((select id from moamat_test.t_ids where k = 'huile'), date '2024-12-31') is null);

select moamat_test.expect(
    'tarif — huile et eau restent distincts',
    public.prestation_tarif((select id from moamat_test.t_ids where k = 'huile'), date '2025-06-01')
    <> public.prestation_tarif((select id from moamat_test.t_ids where k = 'eau'), date '2025-06-01'));

select moamat_test.expect_raises(
    'tarif — date d''effet deja tarifiee refusee (historique jamais modifie)',
    format($q$ select public.definir_tarif_prestation(%s, 99, date '2025-01-01') $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

select moamat_test.expect_raises(
    'tarif — negatif refuse',
    format($q$ select public.definir_tarif_prestation(%s, -1, date '2027-01-01') $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

select moamat_test.expect_raises(
    'tarif — date manquante refusee',
    format($q$ select public.definir_tarif_prestation(%s, 10, null) $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

-- =============================================================================
--  5. Droits
-- =============================================================================

-- Aucune écriture directe, même pour un rôle qui peut passer par les RPC.
select moamat_test.expect_raises(
    'droits — INSERT direct sur fournisseur_prestation refuse',
    $q$ insert into public.fournisseur_prestation (fournisseur_id, libelle) values (public.resoudre_fournisseur('Apragaz'), 'Direct') $q$);

select moamat_test.expect_raises(
    'droits — INSERT direct sur fournisseur_prestation_tarif refuse',
    format($q$ insert into public.fournisseur_prestation_tarif (prestation_id, prix_eur, date_effet) values (%s, 1, date '2030-01-01') $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

-- gestion n'a pas fournisseur.delete : ni fournisseur, ni tarif, ni prestation.
select moamat_test.expect_raises(
    'droits — gestion ne peut pas supprimer un tarif',
    format($q$ select public.supprimer_tarif_prestation(%s) $q$,
           (select id from public.fournisseur_prestation_tarif
            where prestation_id = (select id from moamat_test.t_ids where k = 'huile') limit 1)));

select moamat_test.expect(
    'droits — gestion ne peut pas supprimer un fournisseur en direct (RLS : 0 ligne)',
    (with d as (delete from public.ref_fournisseur
                where id = (select id from moamat_test.t_ids where k = 'fournisseur') returning 1)
     select count(*) from d) = 0);

select moamat_test.expect_raises(
    'droits — gestion ne peut pas supprimer une prestation',
    format($q$ select public.supprimer_prestation_fournisseur(%s) $q$, (select id from moamat_test.t_ids where k = 'libre')));

reset role;

-- lecture : peut lire, ne peut rien écrire.
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f1', 'email', 'fp-lecture@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect(
    'droits — lecture voit les prestations',
    (select count(*) from public.fournisseur_prestation) > 0);

select moamat_test.expect(
    'droits — lecture voit les tarifs',
    (select count(*) from public.fournisseur_prestation_tarif) > 0);

select moamat_test.expect_raises(
    'droits — lecture ne peut pas creer de fournisseur',
    $q$ insert into public.ref_fournisseur (nom) values ('Interdit') $q$);

select moamat_test.expect_raises(
    'droits — lecture ne peut pas creer de prestation',
    format($q$ select public.enregistrer_prestation_fournisseur(null, %s, 'Interdit', null, null, true, null) $q$,
           (select id from moamat_test.t_ids where k = 'fournisseur')));

select moamat_test.expect_raises(
    'droits — lecture ne peut pas definir de tarif',
    format($q$ select public.definir_tarif_prestation(%s, 5, date '2031-01-01') $q$,
           (select id from moamat_test.t_ids where k = 'libre')));

reset role;

-- =============================================================================
--  6. Campagne — prestations du fournisseur de la campagne uniquement
-- =============================================================================

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f2', 'email', 'fp-gestion@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect_raises(
    'campagne — fournisseur inconnu refuse (jamais cree implicitement)',
    $q$ select public.creer_campagne('Fournisseur Fantome', array[999300001]) $q$);

do $$
declare
    v_id bigint;
begin
    v_id := public.creer_campagne(' fournisseur TEST ', array[999300001, 999300002]);
    insert into moamat_test.t_ids values ('campagne', v_id);
end $$;

select moamat_test.expect(
    'campagne — fournisseur resolu (casse/espaces normalises)',
    (select fournisseur_id from public.campagne where id = (select id from moamat_test.t_ids where k = 'campagne'))
    = (select id from moamat_test.t_ids where k = 'fournisseur'));

select moamat_test.expect_raises(
    'campagne — prestation d''un autre fournisseur refusee',
    format($q$ select public.definir_prestation_campagne_ligne(%s, %s) $q$,
           (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001),
           moamat_test.presta('Apragaz', 'RR (contrôle optique)')));

select moamat_test.expect_raises(
    'campagne — prestation libre (sans type de controle) refusee',
    format($q$ select public.definir_prestation_campagne_ligne(%s, %s) $q$,
           (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001),
           (select id from moamat_test.t_ids where k = 'libre')));

-- Prestation désactivée refusée.
select public.enregistrer_prestation_fournisseur(
    (select id from moamat_test.t_ids where k = 'eau'), null, 'Hydro eau test', 'hydraulique', 'eau', false, null);

select moamat_test.expect_raises(
    'campagne — prestation desactivee refusee',
    format($q$ select public.definir_prestation_campagne_ligne(%s, %s) $q$,
           (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001),
           (select id from moamat_test.t_ids where k = 'eau')));

-- Prestation huile tarifée : coût estimé = tarif en vigueur.
select public.definir_prestation_campagne_ligne(
    (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001),
    (select id from moamat_test.t_ids where k = 'huile'));

select moamat_test.expect(
    'campagne — cout estime = tarif en vigueur de la prestation',
    (select cout_estime_eur from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001)
    = public.prestation_tarif((select id from moamat_test.t_ids where k = 'huile'), current_date));

-- Prestation de contrôle SANS tarif : acceptée, coût estimé NULL.
select public.definir_prestation_campagne_ligne(
    (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300002),
    (select id from moamat_test.t_ids where k = 'optique'));

select moamat_test.expect(
    'campagne — prestation sans tarif acceptee, cout estime NULL',
    (select cout_estime_eur from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300002) is null
    and (select prestation_id from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300002) is not null);

select moamat_test.expect(
    'campagne — la vue expose controle et methode (huile) de la ligne',
    (select controle_cylindre = 'hydraulique' and methode_hydraulique = 'huile'
     from public.v_campagne_ligne
     where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001));

-- Type de contrôle / méthode figés une fois la prestation utilisée.
select moamat_test.expect_raises(
    'prestation utilisee — changement huile -> eau refuse',
    format($q$ select public.enregistrer_prestation_fournisseur(%s, null, 'Hydro huile test', 'hydraulique', 'eau', true, null) $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

select moamat_test.expect_raises(
    'prestation utilisee — changement hydraulique -> optique refuse',
    format($q$ select public.enregistrer_prestation_fournisseur(%s, null, 'Hydro huile test', 'optique', null, true, null) $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

-- Libellé / commentaire / activation restent modifiables.
select public.enregistrer_prestation_fournisseur(
    (select id from moamat_test.t_ids where k = 'huile'), null, 'Hydro huile test (maj)', 'hydraulique', 'huile', true, 'ok');

select moamat_test.expect(
    'prestation utilisee — libelle modifiable',
    (select libelle from public.fournisseur_prestation where id = (select id from moamat_test.t_ids where k = 'huile')) = 'Hydro huile test (maj)');

-- Changement de fournisseur d'une campagne en préparation : les prestations
-- (propres à chaque fournisseur) sont remises à « à choisir ».
with i as (insert into public.ref_fournisseur (nom) values ('Autre Fournisseur') returning id)
insert into moamat_test.t_ids select 'autre', id from i;

select public.definir_fournisseur_campagne(
    (select id from moamat_test.t_ids where k = 'campagne'),
    (select id from moamat_test.t_ids where k = 'autre'));

select moamat_test.expect(
    'campagne — changement de fournisseur : fournisseur et libelle mis a jour',
    (select fournisseur_id = (select id from moamat_test.t_ids where k = 'autre') and prestataire = 'Autre Fournisseur'
     from public.campagne where id = (select id from moamat_test.t_ids where k = 'campagne')));

select moamat_test.expect(
    'campagne — changement de fournisseur : prestations et couts estimes remis a NULL',
    (select count(*) from public.campagne_ligne
     where campagne_id = (select id from moamat_test.t_ids where k = 'campagne')
       and (prestation_id is not null or cout_estime_eur is not null)) = 0);

select moamat_test.expect_raises(
    'campagne — apres changement, une prestation de l''ancien fournisseur est refusee',
    format($q$ select public.definir_prestation_campagne_ligne(%s, %s) $q$,
           (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001),
           (select id from moamat_test.t_ids where k = 'huile')));

select moamat_test.expect_raises(
    'campagne — fournisseur inexistant refuse',
    format($q$ select public.definir_fournisseur_campagne(%s, 999999999) $q$,
           (select id from moamat_test.t_ids where k = 'campagne')));

-- Retour au fournisseur d'origine : la campagne reste exploitable pour la suite.
select public.definir_fournisseur_campagne(
    (select id from moamat_test.t_ids where k = 'campagne'),
    (select id from moamat_test.t_ids where k = 'fournisseur'));

select public.definir_prestation_campagne_ligne(
    (select id from public.campagne_ligne where campagne_id = (select id from moamat_test.t_ids where k = 'campagne') and item_id = 999300001),
    (select id from moamat_test.t_ids where k = 'huile'));

-- gestion n'a pas fournisseur.delete.
select moamat_test.expect_raises(
    'droits — gestion ne peut pas supprimer un fournisseur',
    format($q$ select public.supprimer_fournisseur(%s) $q$, (select id from moamat_test.t_ids where k = 'autre')));

reset role;

-- Suppression : refusée si utilisée (même admin) ; autorisée sinon ; tarifs purgés.
select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000f3', 'email', 'fp-admin@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('setup — role courant = admin', public.moamat_current_role() = 'admin');

select moamat_test.expect_raises(
    'suppression — prestation utilisee en campagne refusee (meme admin)',
    format($q$ select public.supprimer_prestation_fournisseur(%s) $q$, (select id from moamat_test.t_ids where k = 'huile')));

-- L'historique des prix ne disparait jamais par effet de bord : une prestation
-- qui a des tarifs ne se supprime pas tant qu'ils existent.
select moamat_test.expect_raises(
    'suppression — prestation avec tarifs refusee (historique protege)',
    format($q$ select public.supprimer_prestation_fournisseur(%s) $q$, (select id from moamat_test.t_ids where k = 'eau')));

-- Tarif saisi par erreur : supprimable par admin (fournisseur.delete) seulement.
select public.supprimer_tarif_prestation(
    (select id from public.fournisseur_prestation_tarif
     where prestation_id = (select id from moamat_test.t_ids where k = 'eau')));

select moamat_test.expect(
    'suppression tarif — admin supprime un tarif saisi par erreur',
    (select count(*) from public.fournisseur_prestation_tarif where prestation_id = (select id from moamat_test.t_ids where k = 'eau')) = 0);

select public.supprimer_prestation_fournisseur((select id from moamat_test.t_ids where k = 'eau'));

select moamat_test.expect(
    'suppression — prestation sans tarif ni usage supprimee',
    (select count(*) from public.fournisseur_prestation where id = (select id from moamat_test.t_ids where k = 'eau')) = 0);

-- Suppression d'un fournisseur : refusée s'il a servi en campagne, sinon
-- supprimé avec ses prestations.
select moamat_test.expect_raises(
    'suppression fournisseur — refusee si une campagne le reference',
    format($q$ select public.supprimer_fournisseur(%s) $q$, (select id from moamat_test.t_ids where k = 'fournisseur')));

select public.supprimer_fournisseur((select id from moamat_test.t_ids where k = 'autre'));

select moamat_test.expect(
    'suppression fournisseur — fournisseur inutilise supprime',
    (select count(*) from public.ref_fournisseur where id = (select id from moamat_test.t_ids where k = 'autre')) = 0);

reset role;

-- Deuxième ligne de défense : le trigger refuse UPDATE/DELETE direct des
-- tarifs, y compris pour "postgres" (hors suppression de prestation).
select moamat_test.expect_raises(
    'tarif — trigger append-only refuse l''UPDATE meme pour "postgres"',
    format($q$ update public.fournisseur_prestation_tarif set prix_eur = 0 where prestation_id = %s $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

select moamat_test.expect_raises(
    'tarif — trigger append-only refuse le DELETE meme pour "postgres"',
    format($q$ delete from public.fournisseur_prestation_tarif where prestation_id = %s $q$,
           (select id from moamat_test.t_ids where k = 'huile')));

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
