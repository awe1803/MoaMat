-- =============================================================================
--  MoaMat — MIGRATION UNIQUE : campagnes de l'ancien modèle de prestation
--  (campagne_ligne.type_prestation + public.ref_tarif_apragaz) vers le
--  catalogue par fournisseur (db/fournisseur_prestation.sql).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  ORDRE d'exécution sur une base EXISTANTE :
--    0. db/fournisseur.sql              (public.ref_fournisseur : coordonnées)
--    1. db/fournisseur_prestation.sql   (tables, RPC, amorçage Apragaz, REPRISE
--                                        de l'ancien catalogue et de ses tarifs)
--    2. CE FICHIER                      (remappage + garde-fous + suppression)
--    3. db/campagne.sql                 (nouvelles fonctions et vue)
--  Entre 2 et 3, les anciennes fonctions de campagne sont supprimées : enchaîner
--  immédiatement. Sur une installation neuve ce fichier est un no-op.
--
--  FAIRE UN DUMP DE LA BASE AVANT : la migration supprime
--  campagne_ligne.type_prestation, public.ref_tarif_apragaz,
--  public.bouteille_tarif_apragaz() ET l'ancien catalogue de db/fournisseur.sql
--  (ref_fournisseur_prestation, ref_fournisseur_tarif, campagne_tarif_prestation,
--  v_tarif_campagne, fournisseur_tarif_en_vigueur) — toutes leurs données sont
--  reprises dans fournisseur_prestation / fournisseur_prestation_tarif et
--  vérifiées avant suppression. Les coordonnées (ref_fournisseur) restent.
--
--  Remappage :
--    'rr'                -> prestation « RR (contrôle optique) »  (optique)
--    'hydraulique_huile' -> prestation « Hydraulique (huile) »    (hydraulique / huile)
--    'hydraulique_eau'   -> prestation « Hydraulique (eau) »      (hydraulique / eau)
--  par fournisseur de la campagne (campagne.fournisseur_id, déduit de
--  campagne.prestataire). Huile et eau ne sont JAMAIS fusionnées.
--
--  Une seule transaction : si un garde-fou échoue (RAISE EXCEPTION), TOUT est
--  annulé et l'ancien modèle reste intact. Les coûts estimés / réels déjà
--  enregistrés ne sont jamais recalculés ni modifiés.
--  Ré-exécutable : sans effet une fois la colonne type_prestation supprimée.
-- =============================================================================

begin;

do $$
declare
    v_n            bigint;
    v_apragaz      bigint;
begin
    if not exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'campagne_ligne'
          and column_name = 'type_prestation') then
        raise notice 'Migration prestations : rien à faire (ancien modèle absent).';
        return;
    end if;

    if to_regclass('public.fournisseur_prestation') is null then
        raise exception 'Exécuter db/fournisseur_prestation.sql avant cette migration.';
    end if;

    v_apragaz := public.resoudre_fournisseur('Apragaz');

    -- 1. Fournisseur de chaque campagne --------------------------------------
    alter table public.campagne
        add column if not exists fournisseur_id bigint references public.ref_fournisseur (id);

    insert into public.ref_fournisseur (nom)
    select distinct on (lower(btrim(prestataire))) btrim(prestataire)
    from public.campagne
    where public.resoudre_fournisseur(prestataire) is null
    order by lower(btrim(prestataire));

    update public.campagne
    set fournisseur_id = public.resoudre_fournisseur(prestataire)
    where fournisseur_id is null;

    select count(*) into v_n from public.campagne where fournisseur_id is null;
    if v_n > 0 then
        raise exception 'Migration prestations : % campagne(s) sans fournisseur résolu.', v_n;
    end if;

    -- 2. Prestations manquantes pour les (fournisseur, code) réellement utilisés
    --    (Apragaz existe déjà via db/fournisseur_prestation.sql ; un autre
    --    prestataire saisi à la main reçoit les mêmes 3 prestations, ET les
    --    mêmes tarifs : l'ancien modèle appliquait le référentiel Apragaz quel
    --    que soit le prestataire, on préserve ce comportement à l'identique).
    insert into public.fournisseur_prestation
        (fournisseur_id, code, libelle, controle_cylindre, methode_hydraulique)
    select u.fournisseur_id, u.type_prestation, m.libelle, m.controle, m.methode
    from (
        select distinct c.fournisseur_id, l.type_prestation
        from public.campagne_ligne l
        join public.campagne c on c.id = l.campagne_id
        where l.type_prestation is not null
    ) u
    join (values
        ('rr',                'RR (contrôle optique)', 'optique',      null::text),
        ('hydraulique_huile', 'Hydraulique (huile)',   'hydraulique', 'huile'),
        ('hydraulique_eau',   'Hydraulique (eau)',     'hydraulique', 'eau')
    ) as m(code, libelle, controle, methode) on m.code = u.type_prestation
    where not exists (
        select 1 from public.fournisseur_prestation p
        where p.fournisseur_id = u.fournisseur_id and p.code = u.type_prestation);

    -- Tarifs de ces prestations créées ici : ceux d'Apragaz (déjà repris par
    -- db/fournisseur_prestation.sql), seule source de prix de l'ancien modèle.
    insert into public.fournisseur_prestation_tarif (prestation_id, prix_eur, date_effet)
    select p.id, t.prix_eur, t.date_effet
    from public.fournisseur_prestation p
    join public.fournisseur_prestation a on a.fournisseur_id = v_apragaz and a.code = p.code
    join public.fournisseur_prestation_tarif t on t.prestation_id = a.id
    where p.fournisseur_id <> v_apragaz
      and p.code in ('rr', 'hydraulique_huile', 'hydraulique_eau')
      and not exists (select 1 from public.fournisseur_prestation_tarif x where x.prestation_id = p.id)
    on conflict (prestation_id, date_effet) do nothing;

    -- 3. Les tarifs des fournisseurs existants (Apragaz inclus) ont été repris
    --    par db/fournisseur_prestation.sql (section 10) ; le garde-fou 5a
    --    vérifie qu'aucun ne manque.

    -- 4. Lien ligne -> prestation ---------------------------------------------
    alter table public.campagne_ligne
        add column if not exists prestation_id bigint references public.fournisseur_prestation (id);

    update public.campagne_ligne l
    set prestation_id = p.id
    from public.campagne c, public.fournisseur_prestation p
    where c.id = l.campagne_id
      and p.fournisseur_id = c.fournisseur_id
      and p.code = l.type_prestation
      and l.prestation_id is null;

    -- 5. GARDE-FOUS — tout écart annule la migration ---------------------------

    -- 5a. Tarifs : chaque ligne de l'ancien modèle existe à l'identique (même
    --     fournisseur/prestation, même date, même prix) dans le nouveau — ancien
    --     catalogue de db/fournisseur.sql (tous fournisseurs) s'il existe, sinon
    --     ancien référentiel Apragaz.
    if to_regclass('public.ref_fournisseur_tarif') is not null then
        select count(*) into v_n
        from public.ref_fournisseur_tarif r
        left join public.fournisseur_prestation p
               on p.fournisseur_id = r.fournisseur_id and p.code = r.type_prestation
        left join public.fournisseur_prestation_tarif t
               on t.prestation_id = p.id and t.date_effet = r.date_effet and t.prix_eur = r.prix_eur
        where t.id is null;
        if v_n > 0 then
            raise exception 'Garde-fou : % tarif(s) de ref_fournisseur_tarif absent(s) ou différent(s) dans fournisseur_prestation_tarif.', v_n;
        end if;
    elsif to_regclass('public.ref_tarif_apragaz') is not null then
        select count(*) into v_n
        from public.ref_tarif_apragaz r
        left join public.fournisseur_prestation p on p.fournisseur_id = v_apragaz and p.code = r.type_prestation
        left join public.fournisseur_prestation_tarif t
               on t.prestation_id = p.id and t.date_effet = r.date_effet and t.prix_eur = r.prix_eur
        where t.id is null;
        if v_n > 0 then
            raise exception 'Garde-fou : % tarif(s) de ref_tarif_apragaz absent(s) ou différent(s) dans fournisseur_prestation_tarif.', v_n;
        end if;
    end if;

    -- 5b. Aucune ligne avec une prestation ancienne non remappée.
    select count(*) into v_n
    from public.campagne_ligne
    where type_prestation is not null and prestation_id is null;
    if v_n > 0 then
        raise exception 'Garde-fou : % ligne(s) de campagne avec une prestation non remappée.', v_n;
    end if;

    -- 5c. Cohérence EXACTE ancien code -> (contrôle, méthode) : optique pour
    --     'rr' seulement ; huile et eau jamais croisées ni fusionnées.
    select count(*) into v_n
    from public.campagne_ligne l
    join public.fournisseur_prestation p on p.id = l.prestation_id
    where l.type_prestation is not null
      and not (
           (l.type_prestation = 'rr'                and p.controle_cylindre = 'optique'
                                                    and p.methode_hydraulique is null)
        or (l.type_prestation = 'hydraulique_huile' and p.controle_cylindre = 'hydraulique'
                                                    and p.methode_hydraulique = 'huile')
        or (l.type_prestation = 'hydraulique_eau'   and p.controle_cylindre = 'hydraulique'
                                                    and p.methode_hydraulique = 'eau'));
    if v_n > 0 then
        raise exception 'Garde-fou : % ligne(s) dont le contrôle/la méthode ne correspond pas à l''ancien code.', v_n;
    end if;

    -- 5d. Les décomptes par code sont identiques avant / après.
    select count(*) into v_n from (
        select l.type_prestation as ancien,
               case when p.controle_cylindre = 'optique' then 'rr'
                    when p.methode_hydraulique = 'huile'  then 'hydraulique_huile'
                    when p.methode_hydraulique = 'eau'    then 'hydraulique_eau' end as nouveau
        from public.campagne_ligne l
        join public.fournisseur_prestation p on p.id = l.prestation_id
        where l.type_prestation is not null
    ) x
    where x.ancien is distinct from x.nouveau;
    if v_n > 0 then
        raise exception 'Garde-fou : % ligne(s) dont le décompte par code diffère avant/après.', v_n;
    end if;

    -- 5e. Le tarif en vigueur aujourd'hui est identique, ligne par ligne.
    --     L'ancien prix est celui de public.campagne_tarif_prestation()
    --     (db/fournisseur.sql, prix du fournisseur Apragaz) s'il existe, sinon
    --     de public.bouteille_tarif_apragaz() (db/item_bouteille.sql).
    if to_regprocedure('public.campagne_tarif_prestation(text,date)') is not null then
        select count(*) into v_n
        from public.campagne_ligne l
        where l.type_prestation is not null
          and public.campagne_tarif_prestation(l.type_prestation, current_date)
              is distinct from public.prestation_tarif(l.prestation_id, current_date);
    else
        select count(*) into v_n
        from public.campagne_ligne l
        where l.type_prestation is not null
          and public.bouteille_tarif_apragaz(l.type_prestation, current_date)
              is distinct from public.prestation_tarif(l.prestation_id, current_date);
    end if;
    if v_n > 0 then
        raise exception 'Garde-fou : % ligne(s) dont le tarif en vigueur diffère entre ancien et nouveau modèle (à examiner à la main avant de migrer).', v_n;
    end if;

    -- 6. Bascule ----------------------------------------------------------------
    alter table public.campagne alter column fournisseur_id set not null;

    drop view if exists public.v_campagne_ligne;
    alter table public.campagne_ligne drop column type_prestation;
    drop function if exists public.definir_prestation_campagne_ligne(bigint, text);
    drop function if exists public.bouteille_tarif_apragaz(text, date);
    drop table if exists public.ref_tarif_apragaz;

    -- Ancien catalogue à 3 prestations figées de db/fournisseur.sql : repris
    -- dans le nouveau (garde-fou 5a), donc supprimé. Les coordonnées
    -- (public.ref_fournisseur) restent.
    drop function if exists public.campagne_tarif_prestation(text, date);
    drop view if exists public.v_tarif_campagne;
    drop function if exists public.fournisseur_tarif_en_vigueur(bigint, text, date);
    drop table if exists public.ref_fournisseur_tarif;
    drop table if exists public.ref_fournisseur_prestation;

    create view public.v_campagne_ligne
        with (security_invoker = true) as
        select
            l.id,
            l.campagne_id,
            l.item_id,
            i.code_club as item_code_club,
            l.prestation_id,
            p.libelle as prestation_libelle,
            p.controle_cylindre,
            p.methode_hydraulique,
            l.cout_estime_eur,
            l.cout_reel_eur,
            l.num_certificat,
            l.resultat,
            l.date_retour_ligne
        from public.campagne_ligne l
        join public.item i on i.id = l.item_id
        left join public.fournisseur_prestation p on p.id = l.prestation_id;

    grant select on public.v_campagne_ligne to authenticated;

    raise notice 'Migration prestations : terminée. Ré-exécuter db/campagne.sql maintenant.';
end $$;

commit;
