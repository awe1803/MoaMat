-- =============================================================================
--  MoaMat — Tests du moteur métier Bouteilles (db/item_bouteille.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Script AUTONOME et NON DESTRUCTIF : tout est encadré par begin ... rollback.
--  Rien n'est conservé (comptes de test, items de test, référentiels insérés).
--
--  Pré-requis : avoir exécuté, dans l'ordre,
--      db/schema.sql, db/initial_load.sql, db/roles.sql, db/permissions.sql,
--      db/model_item.sql, db/transform_item.sql, db/rls.sql, db/audit.sql,
--      db/item_etat.sql, db/item_bouteille.sql
--  puis lancer CE fichier avec un rôle non restreint (« postgres ») :
--      psql "$SUPABASE_DB_URL" -f db/tests/item_bouteille_tests.sql
--
--  Couverture :
--    1. Les 6 profils réglementaires produisent les échéances attendues.
--    2. Un contrôle non applicable à un profil (ex. carbone + optique) donne
--       une échéance NULL, jamais une valeur par défaut.
--    3. Les deux compteurs évoluent indépendamment l'un de l'autre.
--    4. Bascule automatique en hors_validite dès dépassement d'échéance
--       (R2.3), journalisée comme une transition normale ; en_maintenance et
--       en_controle sont exclus (workflow actif non interrompu).
--    5. Référentiels réglementaire/tarifaire : admin peut INSÉRER une valeur
--       datée, mais UPDATE/DELETE sont refusés même pour un admin
--       (historisation append-only).
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
--  1. Les 6 profils réglementaires — echeances attendues
--     (public.ref_periodicite_bouteille, valeurs reprises de
--     public.ref_regle_requalification : Plongée ACIER/ALU/Carbonne,
--     Deco O², O² Secourisme, Tampons).
-- -----------------------------------------------------------------------------

select moamat_test.expect(
    'profil plongee_acier — optique = dernier controle + 30 mois',
    public.bouteille_echeance('plongee_acier', 'optique', date '2024-01-15') = date '2026-07-15');

select moamat_test.expect(
    'profil plongee_acier — hydraulique = dernier controle + 60 mois',
    public.bouteille_echeance('plongee_acier', 'hydraulique', date '2024-01-15') = date '2029-01-15');

select moamat_test.expect(
    'profil plongee_alu — optique = dernier controle + 30 mois',
    public.bouteille_echeance('plongee_alu', 'optique', date '2024-01-15') = date '2026-07-15');

select moamat_test.expect(
    'profil plongee_alu — hydraulique = dernier controle + 60 mois',
    public.bouteille_echeance('plongee_alu', 'hydraulique', date '2024-01-15') = date '2029-01-15');

select moamat_test.expect(
    'profil plongee_carbone — hydraulique = dernier controle + 36 mois',
    public.bouteille_echeance('plongee_carbone', 'hydraulique', date '2024-01-15') = date '2027-01-15');

select moamat_test.expect(
    'profil plongee_carbone — optique NON applicable => NULL ("JAMAIS" dans le miroir)',
    public.bouteille_echeance('plongee_carbone', 'optique', date '2024-01-15') is null);

select moamat_test.expect(
    'profil deco_o2 — optique = dernier controle + 30 mois',
    public.bouteille_echeance('deco_o2', 'optique', date '2024-01-15') = date '2026-07-15');

select moamat_test.expect(
    'profil deco_o2 — hydraulique = dernier controle + 60 mois',
    public.bouteille_echeance('deco_o2', 'hydraulique', date '2024-01-15') = date '2029-01-15');

select moamat_test.expect(
    'profil o2_secourisme — optique = dernier controle + 60 mois',
    public.bouteille_echeance('o2_secourisme', 'optique', date '2024-01-15') = date '2029-01-15');

select moamat_test.expect(
    'profil o2_secourisme — hydraulique NON applicable => NULL',
    public.bouteille_echeance('o2_secourisme', 'hydraulique', date '2024-01-15') is null);

select moamat_test.expect(
    'profil bloc_tampon — hydraulique = dernier controle + 120 mois',
    public.bouteille_echeance('bloc_tampon', 'hydraulique', date '2024-01-15') = date '2034-01-15');

select moamat_test.expect(
    'profil bloc_tampon — optique NON applicable => NULL',
    public.bouteille_echeance('bloc_tampon', 'optique', date '2024-01-15') is null);

-- Résolveur (famille, matière) -> type réglementaire, pour les 6 profils.
select moamat_test.expect(
    'resolveur — plongee + acier => plongee_acier',
    public.bouteille_type_referentiel('plongee', 'acier') = 'plongee_acier');

select moamat_test.expect(
    'resolveur — deco_o2 (matiere ignoree) => deco_o2',
    public.bouteille_type_referentiel('deco_o2', null) = 'deco_o2');

select moamat_test.expect(
    'resolveur — o2_secourisme => o2_secourisme',
    public.bouteille_type_referentiel('o2_secourisme', null) = 'o2_secourisme');

select moamat_test.expect(
    'resolveur — bloc_tampon => bloc_tampon',
    public.bouteille_type_referentiel('bloc_tampon', null) = 'bloc_tampon');

-- -----------------------------------------------------------------------------
--  2. Compteurs indépendants + synchronisation de item.date_echeance
--     (jeu de données créé en tant que "postgres", RLS contournée)
-- -----------------------------------------------------------------------------

insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200001, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere)
values (999200001, 'plongee', 'acier');

-- Seul le compteur optique est renseigné : le compteur hydraulique doit
-- rester NULL (aucun couplage entre les deux).
update public.item_bouteille
set date_dernier_controle_optique = current_date - 1
where item_id = 999200001;

select moamat_test.expect(
    'compteurs independants — hydraulique reste NULL quand seul optique est renseigne',
    (select echeance_hydraulique from public.v_item_bouteille where item_id = 999200001) is null);

select moamat_test.expect(
    'compteurs independants — optique calcule malgre hydraulique NULL',
    (select echeance_optique from public.v_item_bouteille where item_id = 999200001) is not null);

select moamat_test.expect(
    'sync — item.date_echeance reprend echeance_min (ici : optique, seul compteur renseigne)',
    (select date_echeance from public.item where id = 999200001)
    = (select echeance_optique from public.v_item_bouteille where item_id = 999200001));

-- On renseigne maintenant l'hydraulique avec une échéance plus proche que
-- l'optique déjà en place : echeance_min doit basculer sur l'hydraulique
-- sans que le compteur optique n'ait bougé (indépendance dans les deux sens).
update public.item_bouteille
set date_dernier_controle_hydraulique = current_date - (60 * 30 + 1)  -- tres ancien -> echeance hydraulique tres proche/depassee
where item_id = 999200001;

select moamat_test.expect(
    'compteurs independants — optique inchange apres mise a jour de hydraulique',
    (select date_dernier_controle_optique from public.item_bouteille where item_id = 999200001) = current_date - 1);

select moamat_test.expect(
    'sync — item.date_echeance reprend le plus proche des deux (hydraulique ici)',
    (select date_echeance from public.item where id = 999200001)
    = (select echeance_hydraulique from public.v_item_bouteille where item_id = 999200001));

-- -----------------------------------------------------------------------------
--  3. Bascule automatique en hors_validite dès dépassement d'échéance (R2.3)
-- -----------------------------------------------------------------------------

insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200002, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_hydraulique)
values (999200002, 'plongee', 'acier', current_date - interval '6 years');

select moamat_test.expect(
    'R2.3 — echeance bien calculee comme depassee avant bascule',
    (select echeance_hydraulique from public.v_item_bouteille where item_id = 999200002) < current_date);

select moamat_test.expect(
    'R2.3 — bascule automatique : 1 bouteille basculee',
    public.item_bouteille_appliquer_hors_validite() >= 1);

select moamat_test.expect(
    'R2.3 — statut passe a hors_validite sans action manuelle',
    (select statut_code from public.item where id = 999200002) = 'hors_validite');

select moamat_test.expect(
    'R2.3 — transition journalisee (item_transition)',
    (select count(*) from public.item_transition
     where item_id = 999200002 and nouveau_statut = 'hors_validite') = 1);

select moamat_test.expect(
    'R2.3 — colonnes transitoires remises a NULL apres la bascule',
    (select statut_motif is null and statut_date_effet is null from public.item where id = 999200002));

select moamat_test.expect(
    'R2.3 — un second appel est idempotent (deja hors_validite, plus candidate)',
    public.item_bouteille_appliquer_hors_validite() = 0);

select moamat_test.expect(
    'R2.3 — motif de la transition identifie le compteur en cause (hydraulique seul depasse)',
    (select motif from public.item_transition
     where item_id = 999200002 and nouveau_statut = 'hors_validite') = 'Échéance de contrôle hydraulique dépassée (bascule automatique)');

-- Les deux compteurs depasses simultanement : le motif doit mentionner les
-- deux controles plutot que d'en choisir un arbitrairement.
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200006, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_optique, date_dernier_controle_hydraulique)
values (999200006, 'plongee', 'acier', current_date - interval '6 years', current_date - interval '6 years');

select moamat_test.expect(
    'motif — deux compteurs depasses simultanement : 1 bouteille basculee',
    public.item_bouteille_appliquer_hors_validite() = 1);

select moamat_test.expect(
    'motif — deux compteurs depasses simultanement : motif mentionne optique et hydraulique',
    (select motif from public.item_transition
     where item_id = 999200006 and nouveau_statut = 'hors_validite') = 'Échéance de contrôle optique et hydraulique dépassée (bascule automatique)');

-- Témoin : échéance non dépassée -> pas de bascule.
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200003, 'bouteille', 'en_stock', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_hydraulique)
values (999200003, 'plongee', 'acier', current_date);

select moamat_test.expect(
    'temoin — echeance valide => aucune bascule',
    public.item_bouteille_appliquer_hors_validite() = 0);

select moamat_test.expect(
    'temoin — statut inchange',
    (select statut_code from public.item where id = 999200003) = 'en_stock');

-- Une bouteille deja prise en charge par un workflow actif (en_maintenance /
-- en_controle) n'est PAS basculee, meme avec une echeance depassee : la
-- bascule automatique ne doit pas ecraser un statut de travail en cours.
insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200004, 'bouteille', 'en_maintenance', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_hydraulique)
values (999200004, 'plongee', 'acier', current_date - interval '6 years');

insert into public.item (id, famille, statut_code, actif)
overriding system value
values (999200005, 'bouteille', 'en_controle', true);

insert into public.item_bouteille (item_id, famille, matiere, date_dernier_controle_hydraulique)
values (999200005, 'plongee', 'acier', current_date - interval '6 years');

select moamat_test.expect(
    'exclusion — en_maintenance/en_controle non candidats malgre echeance depassee',
    public.item_bouteille_appliquer_hors_validite() = 0);

select moamat_test.expect(
    'exclusion — en_maintenance reste en_maintenance',
    (select statut_code from public.item where id = 999200004) = 'en_maintenance');

select moamat_test.expect(
    'exclusion — en_controle reste en_controle',
    (select statut_code from public.item where id = 999200005) = 'en_controle');

-- -----------------------------------------------------------------------------
--  4. Référentiels administrables et datés — INSERT admin OK, UPDATE/DELETE
--     refusés même pour un admin (historisation append-only)
-- -----------------------------------------------------------------------------

insert into auth.users (instance_id, id, aud, role, email,
                        encrypted_password, email_confirmed_at,
                        created_at, updated_at,
                        raw_app_meta_data, raw_user_meta_data)
values ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-0000000000c1',
        'authenticated', 'authenticated', 'item-bouteille-test-admin@moamat.test',
        '', now(), now(), now(), '{}'::jsonb, '{}'::jsonb);

update public.utilisateur_role set role = 'admin' where user_id = '00000000-0000-0000-0000-0000000000c1';

select set_config('request.jwt.claims',
    json_build_object('sub', '00000000-0000-0000-0000-0000000000c1', 'email', 'item-bouteille-test-admin@moamat.test', 'role', 'authenticated')::text,
    true);
set local role authenticated;

select moamat_test.expect('setup — role courant = admin', public.moamat_current_role() = 'admin');

insert into public.ref_periodicite_bouteille (type_bouteille, type_controle, periodicite_mois, date_effet)
values ('plongee_acier', 'hydraulique', 48, current_date + 1);

select moamat_test.expect(
    'referentiel reglementaire — admin peut inserer une nouvelle valeur datee',
    (select count(*) from public.ref_periodicite_bouteille
     where type_bouteille = 'plongee_acier' and type_controle = 'hydraulique' and date_effet = current_date + 1) = 1);

select moamat_test.expect(
    'referentiel reglementaire — l''ancienne valeur reste en base (historique conserve)',
    (select count(*) from public.ref_periodicite_bouteille
     where type_bouteille = 'plongee_acier' and type_controle = 'hydraulique' and periodicite_mois = 60) = 1);

-- Pas de policy UPDATE/DELETE du tout sur ces deux tables (RLS deny-by-
-- default) : pour un rôle authentifié, meme un admin, la ligne ciblée n'est
-- simplement jamais visible en écriture -> 0 ligne affectée, sans exception
-- (RLS filtre AVANT que le trigger append-only ne soit atteint).
select moamat_test.expect_write_denied(
    'referentiel reglementaire — UPDATE sans effet pour un admin (aucune policy UPDATE)',
    $q$ update public.ref_periodicite_bouteille set periodicite_mois = 12 where type_bouteille = 'plongee_acier' and type_controle = 'optique' $q$);

select moamat_test.expect_write_denied(
    'referentiel reglementaire — DELETE sans effet pour un admin (aucune policy DELETE)',
    $q$ delete from public.ref_periodicite_bouteille where type_bouteille = 'plongee_acier' and type_controle = 'optique' $q$);

insert into public.ref_tarif_apragaz (type_prestation, prix_eur, date_effet)
values ('rr', 22.00, date '2027-01-01');

select moamat_test.expect(
    'referentiel tarifaire — admin peut inserer un tarif date',
    (select count(*) from public.ref_tarif_apragaz where type_prestation = 'rr' and date_effet = date '2027-01-01') = 1);

select moamat_test.expect(
    'referentiel tarifaire — huile et eau restent deux lignes distinctes (ecart constate, jamais fusionnees)',
    (select count(distinct prix_eur) from public.ref_tarif_apragaz
     where type_prestation in ('hydraulique_huile', 'hydraulique_eau') and date_effet = date '2023-01-01') = 2);

select moamat_test.expect_write_denied(
    'referentiel tarifaire — UPDATE sans effet pour un admin (aucune policy UPDATE)',
    $q$ update public.ref_tarif_apragaz set prix_eur = 0 where type_prestation = 'rr' and date_effet = date '2023-01-01' $q$);

select moamat_test.expect_write_denied(
    'referentiel tarifaire — DELETE sans effet pour un admin (aucune policy DELETE)',
    $q$ delete from public.ref_tarif_apragaz where type_prestation = 'rr' and date_effet = date '2023-01-01' $q$);

reset role;

-- Deuxième ligne de défense, indépendante de la RLS : le trigger append-only
-- lui-même refuse tout UPDATE/DELETE, y compris pour "postgres" (propriétaire
-- de la table, qui contourne la RLS) — exactement comme public.item_transition
-- (db/item_etat.sql). Vérifié ici en contournant volontairement la RLS pour
-- isoler le comportement du trigger de celui des policies testé ci-dessus.
select moamat_test.expect_raises(
    'referentiel reglementaire — trigger append-only refuse l''UPDATE meme pour "postgres"',
    $q$ update public.ref_periodicite_bouteille set periodicite_mois = 12 where type_bouteille = 'plongee_acier' and type_controle = 'optique' $q$);

select moamat_test.expect_raises(
    'referentiel reglementaire — trigger append-only refuse le DELETE meme pour "postgres"',
    $q$ delete from public.ref_periodicite_bouteille where type_bouteille = 'plongee_acier' and type_controle = 'optique' $q$);

select moamat_test.expect_raises(
    'referentiel tarifaire — trigger append-only refuse l''UPDATE meme pour "postgres"',
    $q$ update public.ref_tarif_apragaz set prix_eur = 0 where type_prestation = 'rr' and date_effet = date '2023-01-01' $q$);

select moamat_test.expect_raises(
    'referentiel tarifaire — trigger append-only refuse le DELETE meme pour "postgres"',
    $q$ delete from public.ref_tarif_apragaz where type_prestation = 'rr' and date_effet = date '2023-01-01' $q$);

-- =============================================================================
--  Fin
-- =============================================================================

do $$
begin
    raise notice '===== TOUS LES CONTROLES SONT PASSES =====';
end $$;

rollback;
