-- =============================================================================
--  MoaMat — Modèle métier « Item » : entité de base + spécialisations,
--  hiérarchie de lieux, catalogue de statuts, détection des codes ambigus.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/schema.sql, db/initial_load.sql, db/roles.sql et
--  db/permissions.sql, AVANT db/transform_item.sql et db/rls.sql.
--  Ré-exécutable sans erreur (create ... if not exists / create or replace).
--  Ce fichier ne CHARGE aucune donnée : la reprise depuis les tables miroir
--  Access se fait dans db/transform_item.sql.
--
--  Rôle de ce modèle
--  -----------------
--  db/schema.sql est un miroir fidèle de l'export Access : une table plate par
--  famille (bouteille, detendeur, gilet…), presque tout en `text`, pas de clé
--  technique propre (l'id Access est réinjecté), pas de hiérarchie de lieux,
--  `code_club` verbatim avec ses doublons. Ces tables restent la ZONE
--  D'ATTERRISSAGE (staging), inchangées et alimentées par db/initial_load.sql.
--
--  Ce fichier pose la couche OPÉRATIONNELLE normalisée, lue et écrite par
--  l'application :
--
--     Access CSV ─► [schema.sql + initial_load.sql]      staging (miroir)
--                            │
--                            ▼
--                   [model_item.sql]  ← CE FICHIER : DDL cible
--                            │
--                            ▼
--                   [transform_item.sql]  ETL idempotent staging ─► cible
--                            │            + typage fin + table de rejets
--                            ▼
--             application WASM  (lecture : vue v_item ; écriture : table item)
--
--  Stratégie d'héritage : CLASS-TABLE INHERITANCE (table par type).
--    * public.item                — tronc commun à toutes les familles
--    * public.item_bouteille …    — une table fille 1:1 par famille, PK = FK
--                                   vers item(id)
--  Justification complète : db/MODELE.md.
--
--  Sécurité : RLS ACTIVÉE SANS POLICY sur toutes les tables créées ici
--  (deny-by-default, comme db/schema.sql). Les policies explicites sont
--  ajoutées par db/rls.sql via public.has_permission('<domaine>.<action>') :
--      item, item_*            -> domaine « item »
--      ref_statut, lieu_*      -> domaine « referentiel »
--      item_reject             -> lecture « item.read », aucune écriture cliente
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Catalogue des statuts (référentiel)
--     Remplace la mosaïque de drapeaux booléens des tables miroir
--     (est_declassee / est_declasse / est_manquant / a_controler /
--      a_requalifier…). `est_terminal` marque les statuts dont on ne revient
--     qu'avec la permission « status.terminal.override » (cf. db/permissions.sql
--     et le trigger d'audit db/audit.sql).
-- -----------------------------------------------------------------------------

create table if not exists public.ref_statut (
    code        text primary key,
    libelle     text not null,
    est_terminal boolean not null default false,
    ordre       integer not null default 100
);

comment on table public.ref_statut is
    'Catalogue des statuts d''un item. est_terminal = retour soumis à « status.terminal.override ».';

insert into public.ref_statut (code, libelle, est_terminal, ordre) values
    ('en_service',    'En service',              false, 10),
    ('a_controler',   'À contrôler',             false, 20),
    ('a_requalifier', 'À requalifier',           false, 30),
    ('en_reparation', 'En réparation',           false, 40),
    ('manquant',      'Manquant / introuvable',  false, 50),
    ('reforme',      'Réformé / hors service',   true,  90),
    ('declasse',      'Déclassé',                true,  91)
on conflict (code) do update
    set libelle = excluded.libelle,
        est_terminal = excluded.est_terminal,
        ordre = excluded.ordre;

-- -----------------------------------------------------------------------------
--  2. Hiérarchie de lieux : Section -> Local -> Contenant
--     Administrable via l'écran /lieux (domaine de permission « referentiel »).
--     AUCUNE logique de droit ne s'appuie sur la section (cf. Q18.3 : la
--     section est un axe de rangement, pas un périmètre d'autorisation).
-- -----------------------------------------------------------------------------

create table if not exists public.lieu_section (
    id      bigint generated always as identity primary key,
    libelle text not null,
    unique (libelle)
);

create table if not exists public.lieu_local (
    id         bigint generated always as identity primary key,
    section_id bigint not null references public.lieu_section (id) on delete cascade,
    libelle    text not null,
    unique (section_id, libelle)
);

create table if not exists public.lieu_contenant (
    id       bigint generated always as identity primary key,
    local_id bigint not null references public.lieu_local (id) on delete cascade,
    libelle  text not null,
    unique (local_id, libelle)
);

comment on table public.lieu_section   is 'Niveau 1 de la hiérarchie de lieux. Pas un périmètre de droits (Q18.3).';
comment on table public.lieu_local     is 'Niveau 2 : un local appartient à une section.';
comment on table public.lieu_contenant is 'Niveau 3 : armoire / bac / palette. item.lieu_contenant_id pointe ici.';

create index if not exists ix_lieu_local_section      on public.lieu_local (section_id);
create index if not exists ix_lieu_contenant_local    on public.lieu_contenant (local_id);

-- Vue « chemin complet » d'un contenant, pratique pour l'UI et les filtres.
drop view if exists public.v_lieu_contenant;
create view public.v_lieu_contenant
    with (security_invoker = true) as
    select
        c.id                                            as contenant_id,
        c.libelle                                       as contenant,
        l.id                                            as local_id,
        l.libelle                                       as local,
        s.id                                            as section_id,
        s.libelle                                       as section,
        s.libelle || ' › ' || l.libelle || ' › ' || c.libelle as chemin
    from public.lieu_contenant c
    join public.lieu_local   l on l.id = c.local_id
    join public.lieu_section s on s.id = l.section_id;

grant select on public.v_lieu_contenant to authenticated;

-- -----------------------------------------------------------------------------
--  3. Table de correspondance texte libre Access -> contenant
--     Alimentée (clés seules, contenant_id NULL) par db/transform_item.sql à
--     partir des colonnes texte des tables miroir (bouteille.site,
--     detendeur.section, gilet.emplacement_remarque…). Les lignes à
--     contenant_id NULL sont la LISTE DE TRAVAIL d'arbitrage manuel : un
--     administrateur les complète via l'écran /lieux, puis on rejoue le
--     transform.
-- -----------------------------------------------------------------------------

create table if not exists public.lieu_mapping (
    source_champ  text not null,          -- ex. 'bouteille.site'
    source_valeur text not null,          -- valeur brute rencontrée
    contenant_id  bigint references public.lieu_contenant (id) on delete set null,
    primary key (source_champ, source_valeur)
);

comment on table public.lieu_mapping is
    'Texte libre Access -> lieu_contenant. contenant_id NULL = à arbitrer (écran /lieux), puis rejouer db/transform_item.sql.';

-- -----------------------------------------------------------------------------
--  4. Entité de base : public.item
--     `id` = vraie clé technique : GENERATED ALWAYS AS IDENTITY (jamais fournie
--     par le client, jamais réutilisée, non modifiable). L'identifiant Access
--     d'origine descend dans (origine_table, origine_id) pour la seule
--     traçabilité de reprise — il n'est plus une clé.
-- -----------------------------------------------------------------------------

create table if not exists public.item (
    id                bigint generated always as identity primary key,

    -- Identité « club » AFFICHÉE, distincte de la clé technique. Jamais
    -- renumérotée automatiquement (cf. Q1.2). Peut être dupliquée / non
    -- structurante : c'est précisément ce que signale code_club_ambigu.
    code_club         text,

    famille           text not null
                      check (famille in ('bouteille', 'detendeur', 'gilet',
                                         'petit_materiel', 'materiel_didactique',
                                         'piece_detachee')),

    num_serie         text,
    marque            text,
    modele            text,
    date_acquisition  date,
    prix_eur          numeric(12,2) check (prix_eur is null or prix_eur >= 0),

    statut_code       text not null default 'en_service'
                      references public.ref_statut (code),

    lieu_contenant_id bigint references public.lieu_contenant (id) on delete set null,
    destination       text,
    remarque          text,

    -- Échéance de contrôle / requalification la plus proche (renseignée pour
    -- les bouteilles ; extensible aux autres familles). Support du filtre
    -- « par échéance » côté application.
    date_echeance     date,

    -- Désactivation LOGIQUE. La couche métier ne fait jamais de DELETE : elle
    -- passe actif à false (traçabilité, intégrité de l'historique).
    actif             boolean not null default true,

    -- Recalculé par public.item_refresh_code_ambigu() (jamais saisi).
    code_club_ambigu  boolean not null default false,

    -- Traçabilité de la reprise Access (NULL pour un item créé dans l'appli).
    origine_table     text,
    origine_id        bigint,

    cree_le           timestamptz not null default now(),
    maj_le            timestamptz not null default now(),

    unique (origine_table, origine_id)
);

comment on table  public.item                  is 'Entité de base commune à toutes les familles de matériel (class-table inheritance). Voir db/MODELE.md.';
comment on column public.item.id               is 'Vraie clé technique : GENERATED ALWAYS AS IDENTITY. Jamais fournie, jamais réutilisée, non modifiable.';
comment on column public.item.code_club        is 'Identité « club » affichée, distincte de id. Jamais renumérotée automatiquement (Q1.2).';
comment on column public.item.code_club_ambigu is 'Vrai si code_club est dupliqué ou non structurant. Recalculé, jamais saisi.';
comment on column public.item.actif            is 'Désactivation logique. La couche métier ne supprime jamais physiquement un item.';

create index if not exists ix_item_famille           on public.item (famille);
create index if not exists ix_item_statut            on public.item (statut_code);
create index if not exists ix_item_lieu              on public.item (lieu_contenant_id);
create index if not exists ix_item_code_club         on public.item (lower(btrim(code_club)));
create index if not exists ix_item_date_echeance     on public.item (date_echeance);
create index if not exists ix_item_actif             on public.item (actif);

-- Horodatage de modification. tg_set_updated_at() (db/roles.sql) écrit
-- new.updated_at ; notre colonne s'appelle maj_le, on fournit donc une
-- fonction dédiée plutôt que de dépendre du nom de colonne.
create or replace function public.tg_item_set_maj_le()
returns trigger language plpgsql as $$
begin
    new.maj_le := now();
    return new;
end $$;

drop trigger if exists set_maj_le on public.item;
create trigger set_maj_le
    before update on public.item
    for each row execute function public.tg_item_set_maj_le();

-- -----------------------------------------------------------------------------
--  5. Tables filles (spécialisations) — 1:1 avec item, PK = FK vers item(id).
--     Colonnes typées selon leur nature réelle (constats A11/A22 : plus de
--     texte pour des nombres). Les valeurs Access non convertibles ne sont pas
--     perdues : db/transform_item.sql les dépose dans public.item_reject.
-- -----------------------------------------------------------------------------

create table if not exists public.item_bouteille (
    item_id              bigint primary key references public.item (id) on delete cascade,
    num_peint            text,
    num_robinet          text,
    filetage             text,
    double_sortie        boolean,
    sangles              text,
    volume_nominal_l     numeric(6,1),
    pression_service_bar integer,
    tare_kg              numeric(6,2),
    capacite_reelle_l    numeric(7,1),
    sortie_autorisee     boolean,
    controle_effectue    boolean,
    date_mise_en_service date,
    autorite_declassement text
);

create table if not exists public.item_detendeur (
    item_id                 bigint primary key references public.item (id) on delete cascade,
    num_ordre               text,
    type_connexion          text,
    a_premier_etage         boolean,
    modele_premier_etage    text,
    num_serie_premier_etage text,
    a_second_etage          boolean,
    modele_second_etage     text,
    num_serie_second_etage  text,
    a_octopus               boolean,
    modele_octopus          text,
    num_serie_octopus       text,
    a_inflateur             boolean,
    a_manometre             boolean,
    sortie_autorisee        boolean,
    reserve_enfants         boolean,
    controle_effectue       boolean,
    date_dernier_controle   date
);

create table if not exists public.item_gilet (
    item_id             bigint primary key references public.item (id) on delete cascade,
    couleur             text,
    taille              text,
    marquage            text,
    sortie_autorisee    boolean,
    emplacement_remarque text
);

create table if not exists public.item_petit_materiel (
    item_id      bigint primary key references public.item (id) on delete cascade,
    sous_famille text,          -- « famille » dans petit_materiel (raquette, palme…)
    couleur      text,
    taille       text,
    pointure     text,
    quantite     numeric(12,2)
);

create table if not exists public.item_materiel_didactique (
    item_id     bigint primary key references public.item (id) on delete cascade,
    designation text,
    quantite    numeric(12,2)
);

create table if not exists public.item_piece_detachee (
    item_id               bigint primary key references public.item (id) on delete cascade,
    reference_fournisseur text,
    dimension             text,
    colisage              text,
    conditionnement       text,
    stock                 numeric(12,2),
    prix_unitaire_eur     numeric(12,2),
    fournisseur           text,
    emplacement           text,
    utilisation           text,
    url_commande          text
);

comment on table public.item_bouteille           is 'Spécialisation « bouteille » de public.item (1:1).';
comment on table public.item_detendeur           is 'Spécialisation « détendeur » de public.item (1:1).';
comment on table public.item_gilet               is 'Spécialisation « gilet » de public.item (1:1).';
comment on table public.item_petit_materiel      is 'Spécialisation « petit matériel » de public.item (1:1).';
comment on table public.item_materiel_didactique is 'Spécialisation « matériel didactique » de public.item (1:1).';
comment on table public.item_piece_detachee      is 'Spécialisation « pièce détachée » de public.item (1:1).';

-- -----------------------------------------------------------------------------
--  6. Journal des rejets de conversion (constats A11/A22)
--     Alimenté par db/transform_item.sql : toute valeur non NULL du miroir qui
--     ne se convertit pas dans le type cible y est consignée VERBATIM. Rien
--     n'est jeté silencieusement. C'est la liste de travail d'arbitrage.
-- -----------------------------------------------------------------------------

create table if not exists public.item_reject (
    id            bigint generated always as identity primary key,
    origine_table text not null,
    origine_id    bigint,
    colonne       text not null,
    valeur_brute  text not null,
    raison        text not null,
    cree_le       timestamptz not null default now()
);

comment on table public.item_reject is
    'Valeurs Access non converties lors de db/transform_item.sql (verbatim). Liste de travail d''arbitrage — aucune donnée perdue.';

create index if not exists ix_item_reject_origine on public.item_reject (origine_table, origine_id);

-- -----------------------------------------------------------------------------
--  7. Détection des codes club ambigus — on SIGNALE, on ne corrige pas (Q1.2)
--
--  Un code_club est « ambigu » si :
--     * il apparaît sur plus d'un item (doublon, casse/espaces ignorés), OU
--     * il n'est pas « structurant » : il ne suit pas le motif attendu
--       <1 à 4 lettres><séparateur optionnel><1 à 5 chiffres>
--       (ex. « B123 », « DET-45 », « MD 007 »). Motif volontairement large et
--       AJUSTABLE — voir db/MODELE.md §5. Aucune renumérotation : la valeur
--       d'origine est conservée, seul le drapeau change.
-- -----------------------------------------------------------------------------

create or replace function public.item_code_est_structurant(p_code text)
returns boolean
language sql
immutable
set search_path = ''
as $$
    select p_code is not null
       and btrim(p_code) ~ '^[A-Za-z]{1,4}[[:space:]./-]?[0-9]{1,5}$'
$$;

comment on function public.item_code_est_structurant(text) is
    'Vrai si le code club suit le motif <lettres><sép. optionnel><chiffres>. Motif ajustable (db/MODELE.md §5).';

-- Vue « live » : recalcule l'ambiguïté à la volée (indépendante de la colonne
-- matérialisée). Utile pour un écran d'audit des codes.
drop view if exists public.v_code_club_ambigu;
create view public.v_code_club_ambigu
    with (security_invoker = true) as
    select
        i.id,
        i.code_club,
        (count(*) filter (where i.code_club is not null)
            over (partition by lower(btrim(i.code_club)))) > 1        as est_duplique,
        (i.code_club is not null
            and not public.item_code_est_structurant(i.code_club))    as est_non_structurant
    from public.item i;

grant select on public.v_code_club_ambigu to authenticated;

-- Recalcule la colonne matérialisée public.item.code_club_ambigu pour TOUTES
-- les lignes. Appelée par db/transform_item.sql et par le trigger ci-dessous.
create or replace function public.item_refresh_code_ambigu()
returns void
language sql
security definer
set search_path = ''
as $$
    update public.item i
    set code_club_ambigu = v.est_duplique or v.est_non_structurant
    from public.v_code_club_ambigu v
    where v.id = i.id
      and i.code_club_ambigu is distinct from (v.est_duplique or v.est_non_structurant)
$$;

comment on function public.item_refresh_code_ambigu() is
    'Recalcule public.item.code_club_ambigu sur toutes les lignes. Idempotent.';

-- Maintien automatique : après toute modification de public.item, on rafraîchit
-- le drapeau. pg_trigger_depth() > 1 => on est dans l'UPDATE de la fonction
-- elle-même : on ne réentre pas.
create or replace function public.tg_item_code_ambigu()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if pg_trigger_depth() <= 1 then
        perform public.item_refresh_code_ambigu();
    end if;
    return null;
end $$;

drop trigger if exists item_code_ambigu on public.item;
create trigger item_code_ambigu
    after insert or update of code_club or delete on public.item
    for each statement execute function public.tg_item_code_ambigu();

-- -----------------------------------------------------------------------------
--  8. Vue de lecture unifiée : public.v_item
--     Modèle de LECTURE de l'application (la table item reste la cible
--     d'ÉCRITURE). Joint le libellé de statut, le chemin de lieu complet et
--     les drapeaux d'ambiguïté « live ». security_invoker => la RLS de
--     public.item s'applique à l'appelant.
-- -----------------------------------------------------------------------------

drop view if exists public.v_item;
create view public.v_item
    with (security_invoker = true) as
    select
        i.id,
        i.code_club,
        i.code_club_ambigu,
        ca.est_duplique          as code_club_duplique,
        ca.est_non_structurant   as code_club_non_structurant,
        i.famille,
        i.num_serie,
        i.marque,
        i.modele,
        i.date_acquisition,
        i.prix_eur,
        i.statut_code,
        st.libelle               as statut_libelle,
        st.est_terminal          as statut_terminal,
        i.lieu_contenant_id,
        lc.section               as lieu_section,
        lc.local                 as lieu_local,
        lc.contenant             as lieu_contenant,
        lc.chemin                as lieu_chemin,
        i.destination,
        i.remarque,
        i.date_echeance,
        i.actif,
        i.origine_table,
        i.origine_id,
        i.cree_le,
        i.maj_le
    from public.item i
    join      public.ref_statut       st on st.code = i.statut_code
    left join public.v_lieu_contenant lc on lc.contenant_id = i.lieu_contenant_id
    left join public.v_code_club_ambigu ca on ca.id = i.id;

comment on view public.v_item is
    'Vue de LECTURE de l''inventaire (statut, lieu, ambiguïté résolus). Écriture : table public.item. security_invoker : RLS de item appliquée.';

grant select on public.v_item to authenticated;

-- -----------------------------------------------------------------------------
--  9. RLS — activation seule (deny-by-default). Policies : db/rls.sql.
-- -----------------------------------------------------------------------------

do $$
declare
    t text;
    tables text[] := array[
        'ref_statut',
        'lieu_section', 'lieu_local', 'lieu_contenant', 'lieu_mapping',
        'item',
        'item_bouteille', 'item_detendeur', 'item_gilet',
        'item_petit_materiel', 'item_materiel_didactique', 'item_piece_detachee',
        'item_reject'
    ];
begin
    foreach t in array tables loop
        execute format('alter table public.%I enable row level security;', t);
        execute format('drop policy if exists moamat_dev_all on public.%I;', t);
    end loop;
end $$;

commit;
