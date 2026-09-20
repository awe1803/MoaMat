-- =============================================================================
--  MoaMat — Historique des événements d'une bouteille : reprise des 412
--  réépreuves Access (Tbl_B2_Réépreuves), requalification et signalement
--  d'incident saisis depuis la fiche bouteille.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/transform_item.sql, db/rls.sql, db/audit.sql et
--  db/item_bouteille.sql (a besoin de public.item, public.item_bouteille,
--  public.item_reject, public.has_permission, public.audit_write et de
--  public.bouteille_requalification, le miroir Access). Ré-exécutable sans
--  erreur : la reprise est idempotente (clé d'origine unique).
--
--  POINT DE VIGILANCE — la jointure réelle (audit des données) :
--    Le champ Access « N° Identbout » de Tbl_B2_Réépreuves (miroir :
--    bouteille_requalification.bouteille_id) ne contient PAS le n° peint sur la
--    bouteille : il contient l'ID INTERNE Access de la bouteille (bouteille.id).
--    Le n° peint est bouteille.num_peint (Access : « N° identbout » de la
--    table bouteille). Les joindre serait silencieusement faux : par exemple
--    la bouteille d'id 18 porte le n° peint 49, alors que « 49 » dans les
--    réépreuves désigne l'id 49 (qui n'existe pas). La reprise ne joint donc
--    QUE sur item.origine_id (ID interne), jamais sur item_bouteille.num_peint.
--
--  Les 3 valeurs orphelines (ids présents dans les réépreuves mais absents de
--  la table bouteille) : 24, 25 et 49 — 33 événements au total.
--    - 24 et 25 : ces ids existent dans bouteille_sortie_inventaire
--      (BOUT-S-62 et BOUT-S-66, bouteilles sorties de l'inventaire actif). Les
--      espaces d'ID des deux tables ne se recoupent pas (aucun id commun), la
--      résolution est donc sans ambiguïté : leurs 21 événements sont rattachés
--      à ces items (origine_table = 'bouteille_sortie_inventaire').
--    - 49 : n'existe dans AUCUNE des deux tables. Ses 12 événements ne sont ni
--      rattachés à une bouteille devinée, ni perdus : ils sont consignés
--      VERBATIM dans public.item_reject (origine_table =
--      'bouteille_requalification', colonne = 'bouteille_id',
--      valeur_brute = '49') pour arbitrage manuel du gestionnaire.
--    Bilan : 412 lignes = 400 rattachées + 12 rejetées (id 49) ; voir le
--    contrôle de volume en fin de fichier.
--  Un type d'opération inconnu (aucun aujourd'hui) est consigné dans
--  item_reject de la même façon.
--
--  Ce fichier pose :
--    1. public.bouteille_evenement — table d'événements (append-only côté
--       client : écritures par fonctions SECURITY DEFINER uniquement).
--    2. RLS lecture (item.read).
--    3. Reprise idempotente des 412 événements depuis le miroir.
--    4. public.enregistrer_requalification_bouteille() (idempotent sur p_request_id ;
--       dates « futures » lues à l'heure de Paris, pas en UTC serveur ; refusée sur
--       une bouteille désactivée ou terminale)
--    5. public.signaler_incident_bouteille()
--    6. Contrôle de volume (NOTICE).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Table d'événements
-- -----------------------------------------------------------------------------

create table if not exists public.bouteille_evenement (
    id                     bigint generated always as identity primary key,
    item_id                bigint not null references public.item (id) on delete cascade,
    type                   text not null
                           check (type in ('mise_en_service', 'controle_optique', 'controle_hydraulique',
                                           'ecartee', 'rebut', 'incident')),
    -- NULL pour l'historique repris d'Access (le résultat n'y était pas saisi
    -- de façon exploitable) ; obligatoire pour une requalification saisie dans
    -- l'appli (public.enregistrer_requalification_bouteille).
    resultat               text check (resultat in ('conforme', 'echec')),
    -- NULL toléré pour l'historique repris (1 ligne Access sans date) ; les
    -- fonctions d'écriture ci-dessous exigent toujours une date.
    date_evenement         date,
    prestataire            text,
    cout_eur               numeric(12,2) check (cout_eur is null or cout_eur >= 0),
    num_certificat         text,
    date_echeance_suivante date,
    remarque               text,
    -- id de la ligne du miroir (bouteille_requalification.id) — NULL pour un
    -- événement saisi dans l'appli. UNIQUE : rend la reprise idempotente.
    origine_id             bigint unique,
    -- Clé d'idempotence générée par le formulaire : un rejeu (timeout réseau puis
    -- nouvel envoi) ne crée pas de doublon. NULL pour l'historique repris.
    request_id             uuid unique,
    cree_le                timestamptz not null default now(),
    cree_par               uuid default auth.uid()
);

-- Rattrapage : base où la table existe déjà sans request_id.
alter table public.bouteille_evenement add column if not exists request_id uuid unique;

-- Anciennes signatures (avant p_request_id) : évite une surcharge ambiguë.
drop function if exists public.enregistrer_requalification_bouteille(bigint, text, date, text, text, numeric, text, text);
drop function if exists public.signaler_incident_bouteille(bigint, date, text);

comment on table public.bouteille_evenement is
    'Chronologie d''une bouteille : mise en service, contrôles optique/hydraulique, écartement/rebut, incidents. Repris de Tbl_B2_Réépreuves via l''ID INTERNE (item.origine_id), jamais via le n° peint. Écritures exclusivement via public.enregistrer_requalification_bouteille / signaler_incident_bouteille.';

create index if not exists ix_bouteille_evenement_item
    on public.bouteille_evenement (item_id, date_evenement desc nulls last, id desc);

-- -----------------------------------------------------------------------------
--  2. RLS — lecture seule pour les clients (aucune policy insert/update/delete).
-- -----------------------------------------------------------------------------

alter table public.bouteille_evenement enable row level security;

drop policy if exists bouteille_evenement_sel on public.bouteille_evenement;
create policy bouteille_evenement_sel on public.bouteille_evenement
    for select to authenticated
    using (public.has_permission('item.read'));

-- -----------------------------------------------------------------------------
--  3. Reprise des 412 événements historiques (idempotente)
--     Jointure sur l'ID INTERNE uniquement. « bouteille » est prioritaire ;
--     bouteille_sortie_inventaire n'est consultée que pour un id introuvable
--     dans « bouteille » (espaces d'ID disjoints, cf. en-tête).
-- -----------------------------------------------------------------------------

with source as (
    select
        r.id                                        as origine_id,
        r.bouteille_id,
        r.date_operation,
        r.type_operation,
        r.cout_eur,
        r.date_echeance_suivante,
        nullif(btrim(r.remarque), '')               as remarque,
        nullif(btrim(r.num_certificat), '')         as num_certificat,
        case btrim(r.type_operation)
            when 'New'                              then 'mise_en_service'
            when 'RR - Optique'                     then 'controle_optique'
            when 'R - Hydraulique'                  then 'controle_hydraulique'
            when 'Ecartée suite à une défectuosité' then 'ecartee'
            when '-REBUTEE-'                        then 'rebut'
        end                                         as type,
        coalesce(
            (select i.id from public.item i
              where i.origine_table = 'bouteille' and i.origine_id = r.bouteille_id),
            (select i.id from public.item i
              where i.origine_table = 'bouteille_sortie_inventaire' and i.origine_id = r.bouteille_id)
        )                                           as item_id
    from public.bouteille_requalification r
)
insert into public.bouteille_evenement
    (item_id, type, date_evenement, prestataire, cout_eur, num_certificat,
     date_echeance_suivante, remarque, origine_id)
select
    s.item_id,
    s.type,
    s.date_operation,
    -- Prestataire jamais saisi dans Access : NULL plutôt qu'une valeur devinée.
    null,
    -- 0.0 = « non renseigné » dans Access (67 lignes seulement portent un coût).
    nullif(s.cout_eur, 0),
    s.num_certificat,
    s.date_echeance_suivante,
    s.remarque,
    s.origine_id
from source s
where s.item_id is not null
  and s.type is not null
on conflict (origine_id) do nothing;

-- Lignes NON rattachées (id orphelin) ou de type inconnu : consignées
-- verbatim, jamais jetées, jamais rattachées « au plus proche ».
insert into public.item_reject (origine_table, origine_id, colonne, valeur_brute, raison)
select
    'bouteille_requalification',
    r.id,
    'bouteille_id',
    coalesce(r.bouteille_id::text, ''),
    'id de bouteille introuvable dans bouteille et bouteille_sortie_inventaire (id orphelin) — événement ' ||
        coalesce(r.type_operation, '?') || ' du ' || coalesce(r.date_operation::text, 'date inconnue') ||
        ' non rattaché ; arbitrage manuel'
from public.bouteille_requalification r
where not exists (select 1 from public.item i
                   where (i.origine_table = 'bouteille' and i.origine_id = r.bouteille_id)
                      or (i.origine_table = 'bouteille_sortie_inventaire' and i.origine_id = r.bouteille_id))
  and not exists (select 1 from public.item_reject j
                   where j.origine_table = 'bouteille_requalification'
                     and j.origine_id = r.id and j.colonne = 'bouteille_id');

insert into public.item_reject (origine_table, origine_id, colonne, valeur_brute, raison)
select 'bouteille_requalification', r.id, 'type_operation', coalesce(r.type_operation, ''),
       'type d''opération inconnu — événement non repris'
from public.bouteille_requalification r
where btrim(coalesce(r.type_operation, '')) not in
      ('New', 'RR - Optique', 'R - Hydraulique', 'Ecartée suite à une défectuosité', '-REBUTEE-')
  and not exists (select 1 from public.item_reject j
                   where j.origine_table = 'bouteille_requalification'
                     and j.origine_id = r.id and j.colonne = 'type_operation');

-- -----------------------------------------------------------------------------
--  4. Enregistrer une requalification depuis la fiche bouteille.
--     « conforme » : met à jour le compteur de contrôle du type concerné
--     (jamais en arrière : un enregistrement antérieur au dernier contrôle
--     connu est historisé mais ne fait pas régresser l'échéance) ; le trigger
--     public.tg_item_bouteille_sync_echeance recalcule l'échéance.
--     « echec » : l'événement est historisé, ni compteur ni statut touchés —
--     déclassement manuel via l'écran de statut, comme pour une campagne.
-- -----------------------------------------------------------------------------

create or replace function public.enregistrer_requalification_bouteille(
    p_item_id        bigint,
    p_type           text,
    p_date           date,
    p_resultat       text,
    p_prestataire    text,
    p_cout_eur       numeric,
    p_num_certificat text,
    p_remarque       text,
    p_request_id     uuid default null)
returns bigint
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_id   bigint;
    v_cout numeric(12,2) := round(p_cout_eur, 2);
begin
    if not public.has_permission('item.update') then
        raise exception 'Droit insuffisant : permission « item.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_type is null or p_type not in ('controle_optique', 'controle_hydraulique') then
        raise exception 'Type de requalification invalide (attendu controle_optique / controle_hydraulique).'
            using errcode = 'check_violation';
    end if;

    if p_resultat is null or p_resultat not in ('conforme', 'echec') then
        raise exception 'Résultat obligatoire (conforme / echec).' using errcode = 'check_violation';
    end if;

    if p_date is null then
        raise exception 'Date de la requalification obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date > (now() at time zone 'Europe/Paris')::date then
        raise exception 'Date de la requalification dans le futur : refusée.' using errcode = 'check_violation';
    end if;

    if v_cout is not null and v_cout < 0 then
        raise exception 'Coût négatif : refusé.' using errcode = 'check_violation';
    end if;

    -- Verrou sur la bouteille : sérialise deux saisies concurrentes (le calcul
    -- « jamais en arrière » ci-dessous lit puis écrit le compteur).
    perform 1 from public.item_bouteille where item_id = p_item_id for update;
    if not found then
        raise exception 'Bouteille introuvable : %.', p_item_id using errcode = 'no_data_found';
    end if;

    -- Une requalification « conforme » renouvelle l'échéance : elle n'a pas de
    -- sens sur une bouteille désactivée ou en statut terminal (rebutée,
    -- perdue…). Un incident, lui, reste possible (voir signaler_incident_bouteille).
    if exists (
        select 1 from public.item i
        join public.ref_statut s on s.code = i.statut_code
        where i.id = p_item_id and (not i.actif or s.est_terminal)
    ) then
        raise exception 'Requalification refusée : bouteille désactivée ou en statut terminal.'
            using errcode = 'check_violation';
    end if;

    -- Idempotence : un rejeu (même p_request_id) renvoie l'événement déjà créé,
    -- sans toucher au compteur, ni au statut, ni à l'audit.
    insert into public.bouteille_evenement
        (item_id, type, resultat, date_evenement, prestataire, cout_eur, num_certificat, remarque, request_id)
    values
        (p_item_id, p_type, p_resultat, p_date,
         nullif(btrim(p_prestataire), ''), v_cout, nullif(btrim(p_num_certificat), ''),
         nullif(btrim(p_remarque), ''), p_request_id)
    on conflict (request_id) do nothing
    returning id into v_id;

    if v_id is null then
        select id into v_id from public.bouteille_evenement where request_id = p_request_id;
        return v_id;
    end if;

    if p_resultat = 'conforme' then
        if p_type = 'controle_optique' then
            update public.item_bouteille
            set date_dernier_controle_optique = p_date
            where item_id = p_item_id
              and (date_dernier_controle_optique is null or date_dernier_controle_optique < p_date);
        else
            update public.item_bouteille
            set date_dernier_controle_hydraulique = p_date
            where item_id = p_item_id
              and (date_dernier_controle_hydraulique is null or date_dernier_controle_hydraulique < p_date);
        end if;
    end if;

    perform public.audit_write(
        'bouteille.requalification',
        'item',
        p_item_id::text,
        null,
        jsonb_build_object('evenement_id', v_id, 'type', p_type, 'resultat', p_resultat, 'date', p_date,
                           'cout_eur', v_cout, 'num_certificat', nullif(btrim(p_num_certificat), '')),
        '{}'::jsonb);

    return v_id;
end $$;

comment on function public.enregistrer_requalification_bouteille(bigint, text, date, text, text, numeric, text, text, uuid) is
    'Historise une requalification (optique / hydraulique) d''une bouteille. « conforme » : compteur de contrôle mis à jour sans jamais reculer (échéance recalculée par trigger). « echec » : événement seul, ni compteur ni statut touchés (déclassement manuel). Exige « item.update ». Journalisé (bouteille.requalification).';

revoke execute on function public.enregistrer_requalification_bouteille(bigint, text, date, text, text, numeric, text, text, uuid) from public;
grant execute on function public.enregistrer_requalification_bouteille(bigint, text, date, text, text, numeric, text, text, uuid) to authenticated;

-- -----------------------------------------------------------------------------
--  5. Signaler un incident — trace datée et journalisée ; ne change JAMAIS le
--     statut (la décision, avec motif et autorité, reste l'écran de statut).
-- -----------------------------------------------------------------------------

create or replace function public.signaler_incident_bouteille(
    p_item_id     bigint,
    p_date        date,
    p_description text,
    p_request_id  uuid default null)
returns bigint
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_id bigint;
begin
    if not public.has_permission('item.update') then
        raise exception 'Droit insuffisant : permission « item.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_description is null or btrim(p_description) = '' then
        raise exception 'Description de l''incident obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date is null then
        raise exception 'Date de l''incident obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date > (now() at time zone 'Europe/Paris')::date then
        raise exception 'Date de l''incident dans le futur : refusée.' using errcode = 'check_violation';
    end if;

    if not exists (select 1 from public.item_bouteille where item_id = p_item_id) then
        raise exception 'Bouteille introuvable : %.', p_item_id using errcode = 'no_data_found';
    end if;

    -- Un incident reste consignable sur une bouteille rebutée ou désactivée
    -- (régularisation d'historique). Idempotent sur p_request_id.
    insert into public.bouteille_evenement (item_id, type, date_evenement, remarque, request_id)
    values (p_item_id, 'incident', p_date, btrim(p_description), p_request_id)
    on conflict (request_id) do nothing
    returning id into v_id;

    if v_id is null then
        select id into v_id from public.bouteille_evenement where request_id = p_request_id;
        return v_id;
    end if;

    perform public.audit_write(
        'bouteille.incident',
        'item',
        p_item_id::text,
        null,
        jsonb_build_object('evenement_id', v_id, 'date', p_date, 'description', btrim(p_description)),
        '{}'::jsonb);

    return v_id;
end $$;

comment on function public.signaler_incident_bouteille(bigint, date, text, uuid) is
    'Consigne un incident daté sur une bouteille (chronologie + audit). Ne modifie jamais le statut. Exige « item.update ». Journalisé (bouteille.incident).';

revoke execute on function public.signaler_incident_bouteille(bigint, date, text, uuid) from public;
grant execute on function public.signaler_incident_bouteille(bigint, date, text, uuid) to authenticated;

-- -----------------------------------------------------------------------------
--  6. Contrôle de volume (NOTICE, non bloquant)
-- -----------------------------------------------------------------------------

do $$
declare
    v_source   bigint;
    v_repris   bigint;
    v_rejetes  bigint;
begin
    select count(*) into v_source  from public.bouteille_requalification;
    select count(*) into v_repris  from public.bouteille_evenement where origine_id is not null;
    -- DISTINCT : une même ligne rejetée pour deux motifs ne compte qu'une fois.
    select count(distinct origine_id) into v_rejetes from public.item_reject where origine_table = 'bouteille_requalification';

    raise notice 'bouteille_evenement : % événement(s) source, % repris, % rejeté(s) (item_reject) — écart % (attendu 0).',
        v_source, v_repris, v_rejetes, v_source - v_repris - v_rejetes;
end $$;

commit;
