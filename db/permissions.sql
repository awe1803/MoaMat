-- =============================================================================
--  MoaMat — Permissions nommées et matrice rôle → permissions.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/roles.sql, AVANT db/rls.sql et db/audit.sql.
--  Ré-exécutable sans erreur (le contenu des tables est réinitialisé).
--
--  Deux tables :
--    * public.permission        — catalogue des permissions nommées
--                                 (« bouteille.read », « pret.create », …)
--    * public.role_permission   — matrice : quelles permissions pour quel rôle
--
--  Convention de nommage : <domaine>.<action>, en minuscules.
--    domaines métier : bouteille, detendeur, gilet, petit_materiel,
--                      materiel_didactique, piece_detachee, compresseur, pret
--    domaines transverses : personne, fournisseur, referentiel, achat, devis
--    domaines système : role, compte, permission, audit, superadmin, status,
--                       utilisateur_legacy
--    actions : read | create | update | delete   (+ actions dédiées : assign,
--              assign_admin, nominate, disable, manage, override, read)
--
--  Les policies RLS (db/rls.sql) ne testent JAMAIS le rôle en dur : elles
--  appellent public.has_permission('<domaine>.<action>').
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Tables
-- -----------------------------------------------------------------------------

create table if not exists public.permission (
    code        text primary key,
    description text not null
);

create table if not exists public.role_permission (
    role            public.app_role not null,
    permission_code text not null references public.permission (code) on delete cascade,
    primary key (role, permission_code)
);

comment on table public.permission      is 'Catalogue des permissions nommées MoaMat (<domaine>.<action>).';
comment on table public.role_permission is 'Matrice rôle → permissions. Testée par public.has_permission().';

-- On repart d'un état propre à chaque exécution.
truncate table public.role_permission;
truncate table public.permission cascade;

-- -----------------------------------------------------------------------------
--  2. Catalogue des permissions
-- -----------------------------------------------------------------------------

insert into public.permission (code, description) values
    -- Domaines métier « items » : lecture / création / modification / suppression
    ('bouteille.read',            'Consulter les bouteilles'),
    ('bouteille.create',          'Créer une bouteille'),
    ('bouteille.update',          'Modifier une bouteille'),
    ('bouteille.delete',          'Supprimer une bouteille'),
    ('detendeur.read',            'Consulter les détendeurs'),
    ('detendeur.create',          'Créer un détendeur'),
    ('detendeur.update',          'Modifier un détendeur'),
    ('detendeur.delete',          'Supprimer un détendeur'),
    ('gilet.read',                'Consulter les gilets'),
    ('gilet.create',              'Créer un gilet'),
    ('gilet.update',              'Modifier un gilet'),
    ('gilet.delete',              'Supprimer un gilet'),
    ('petit_materiel.read',       'Consulter le petit matériel'),
    ('petit_materiel.create',     'Créer un petit matériel'),
    ('petit_materiel.update',     'Modifier un petit matériel'),
    ('petit_materiel.delete',     'Supprimer un petit matériel'),
    ('materiel_didactique.read',  'Consulter le matériel didactique'),
    ('materiel_didactique.create','Créer un matériel didactique'),
    ('materiel_didactique.update','Modifier un matériel didactique'),
    ('materiel_didactique.delete','Supprimer un matériel didactique'),
    ('piece_detachee.read',       'Consulter les pièces détachées'),
    ('piece_detachee.create',     'Créer une pièce détachée'),
    ('piece_detachee.update',     'Modifier une pièce détachée'),
    ('piece_detachee.delete',     'Supprimer une pièce détachée'),
    ('compresseur.read',          'Consulter les relevés compresseur'),
    ('compresseur.create',        'Créer un relevé compresseur'),
    ('compresseur.update',        'Modifier un relevé compresseur'),
    ('compresseur.delete',        'Supprimer un relevé compresseur'),
    ('pret.read',                 'Consulter les prêts'),
    ('pret.create',               'Créer un prêt'),
    ('pret.update',               'Modifier un prêt'),
    ('pret.delete',               'Supprimer un prêt'),
    -- Annuaire des membres (données personnelles — RGPD)
    ('personne.read',             'Consulter l''annuaire des membres'),
    ('personne.create',           'Ajouter un membre à l''annuaire'),
    ('personne.update',           'Modifier une fiche membre'),
    ('personne.delete',           'Supprimer une fiche membre'),
    -- Fournisseurs
    ('fournisseur.read',          'Consulter les fournisseurs'),
    ('fournisseur.create',        'Créer un fournisseur'),
    ('fournisseur.update',        'Modifier un fournisseur'),
    ('fournisseur.delete',        'Supprimer un fournisseur'),
    -- Référentiels (ref_*) : échéances, tarifs, sites, gaz, règles…
    ('referentiel.read',          'Consulter les référentiels'),
    ('referentiel.create',        'Créer une entrée de référentiel'),
    ('referentiel.update',        'Modifier une entrée de référentiel'),
    ('referentiel.delete',        'Supprimer une entrée de référentiel'),
    -- Achats / factures / réceptions (données financières)
    ('achat.read',                'Consulter les achats et factures'),
    ('achat.create',              'Créer un achat / une facture'),
    ('achat.update',              'Modifier un achat / une facture'),
    ('achat.delete',              'Supprimer un achat / une facture'),
    -- Devis
    ('devis.read',                'Consulter les devis'),
    ('devis.create',              'Créer une ligne de devis'),
    ('devis.update',              'Modifier une ligne de devis'),
    ('devis.delete',              'Supprimer une ligne de devis'),
    -- Système
    ('role.read',                 'Consulter les rôles de tous les utilisateurs'),
    ('role.assign',               'Attribuer les rôles lecture / gestion'),
    ('role.assign_admin',         'Attribuer / retirer les rôles admin et super-admin'),
    ('superadmin.nominate',       'Nommer ou transférer le siège de super-admin'),
    ('compte.read',               'Consulter la liste des comptes utilisateurs'),
    ('compte.disable',            'Activer / désactiver un compte utilisateur'),
    ('permission.read',           'Consulter le catalogue des permissions'),
    ('permission.manage',         'Modifier le catalogue des permissions et la matrice'),
    ('audit.read',                'Consulter le journal d''audit'),
    ('status.terminal.override',  'Revenir sur un statut terminal (déclassement, clôture)'),
    ('utilisateur_legacy.read',   'Consulter la table utilisateur héritée d''Access'),
    ('utilisateur_legacy.create', 'Créer une ligne dans la table utilisateur héritée'),
    ('utilisateur_legacy.update', 'Modifier la table utilisateur héritée'),
    ('utilisateur_legacy.delete', 'Supprimer dans la table utilisateur héritée');

-- -----------------------------------------------------------------------------
--  3. Matrice rôle → permissions
--     Chaque rôle est décrit EN ENTIER (pas d'héritage implicite) pour que la
--     lecture de ce fichier suffise à connaître les droits effectifs.
-- -----------------------------------------------------------------------------

-- 3.1 lecture — « CA » / consultation. Lecture seule sur le métier. Ne voit que
--     sa propre ligne de rôle (pas « role.read » : la liste des rôles de tous
--     les comptes est réservée à admin+ via l'écran /comptes).
insert into public.role_permission (role, permission_code) values
    ('lecture', 'bouteille.read'),
    ('lecture', 'detendeur.read'),
    ('lecture', 'gilet.read'),
    ('lecture', 'petit_materiel.read'),
    ('lecture', 'materiel_didactique.read'),
    ('lecture', 'piece_detachee.read'),
    ('lecture', 'compresseur.read'),
    ('lecture', 'pret.read'),
    ('lecture', 'fournisseur.read'),
    ('lecture', 'referentiel.read');

-- 3.2 gestion — équipe matériel. Écriture sur les items, pas sur les
--     référentiels ni les données financières.
insert into public.role_permission (role, permission_code) values
    ('gestion', 'bouteille.read'),   ('gestion', 'bouteille.create'),   ('gestion', 'bouteille.update'),   ('gestion', 'bouteille.delete'),
    ('gestion', 'detendeur.read'),   ('gestion', 'detendeur.create'),   ('gestion', 'detendeur.update'),   ('gestion', 'detendeur.delete'),
    ('gestion', 'gilet.read'),       ('gestion', 'gilet.create'),       ('gestion', 'gilet.update'),       ('gestion', 'gilet.delete'),
    ('gestion', 'petit_materiel.read'),      ('gestion', 'petit_materiel.create'),      ('gestion', 'petit_materiel.update'),      ('gestion', 'petit_materiel.delete'),
    ('gestion', 'materiel_didactique.read'), ('gestion', 'materiel_didactique.create'), ('gestion', 'materiel_didactique.update'), ('gestion', 'materiel_didactique.delete'),
    ('gestion', 'piece_detachee.read'),      ('gestion', 'piece_detachee.create'),      ('gestion', 'piece_detachee.update'),      ('gestion', 'piece_detachee.delete'),
    ('gestion', 'compresseur.read'), ('gestion', 'compresseur.create'), ('gestion', 'compresseur.update'), ('gestion', 'compresseur.delete'),
    ('gestion', 'pret.read'),        ('gestion', 'pret.create'),        ('gestion', 'pret.update'),        ('gestion', 'pret.delete'),
    ('gestion', 'personne.read'),    ('gestion', 'personne.create'),    ('gestion', 'personne.update'),
    ('gestion', 'fournisseur.read'), ('gestion', 'fournisseur.create'), ('gestion', 'fournisseur.update'),
    ('gestion', 'referentiel.read');

-- 3.3 admin — administration. Référentiels, finances, audit, attribution des
--     rôles lecture / gestion.
insert into public.role_permission (role, permission_code) values
    ('admin', 'bouteille.read'),   ('admin', 'bouteille.create'),   ('admin', 'bouteille.update'),   ('admin', 'bouteille.delete'),
    ('admin', 'detendeur.read'),   ('admin', 'detendeur.create'),   ('admin', 'detendeur.update'),   ('admin', 'detendeur.delete'),
    ('admin', 'gilet.read'),       ('admin', 'gilet.create'),       ('admin', 'gilet.update'),       ('admin', 'gilet.delete'),
    ('admin', 'petit_materiel.read'),      ('admin', 'petit_materiel.create'),      ('admin', 'petit_materiel.update'),      ('admin', 'petit_materiel.delete'),
    ('admin', 'materiel_didactique.read'), ('admin', 'materiel_didactique.create'), ('admin', 'materiel_didactique.update'), ('admin', 'materiel_didactique.delete'),
    ('admin', 'piece_detachee.read'),      ('admin', 'piece_detachee.create'),      ('admin', 'piece_detachee.update'),      ('admin', 'piece_detachee.delete'),
    ('admin', 'compresseur.read'), ('admin', 'compresseur.create'), ('admin', 'compresseur.update'), ('admin', 'compresseur.delete'),
    ('admin', 'pret.read'),        ('admin', 'pret.create'),        ('admin', 'pret.update'),        ('admin', 'pret.delete'),
    ('admin', 'personne.read'),    ('admin', 'personne.create'),    ('admin', 'personne.update'),    ('admin', 'personne.delete'),
    ('admin', 'fournisseur.read'), ('admin', 'fournisseur.create'), ('admin', 'fournisseur.update'), ('admin', 'fournisseur.delete'),
    ('admin', 'referentiel.read'), ('admin', 'referentiel.create'), ('admin', 'referentiel.update'), ('admin', 'referentiel.delete'),
    ('admin', 'achat.read'),       ('admin', 'achat.create'),       ('admin', 'achat.update'),       ('admin', 'achat.delete'),
    ('admin', 'devis.read'),       ('admin', 'devis.create'),       ('admin', 'devis.update'),       ('admin', 'devis.delete'),
    ('admin', 'role.read'),        ('admin', 'role.assign'),
    ('admin', 'compte.read'),      ('admin', 'compte.disable'),
    ('admin', 'permission.read'),
    ('admin', 'audit.read'),
    ('admin', 'status.terminal.override'),
    ('admin', 'utilisateur_legacy.read');

-- 3.4 super-admin — tout ce qui précède + gestion des rôles élevés, nomination
--     du super-admin, gestion du catalogue de permissions.
insert into public.role_permission (role, permission_code) values
    ('super-admin', 'bouteille.read'),   ('super-admin', 'bouteille.create'),   ('super-admin', 'bouteille.update'),   ('super-admin', 'bouteille.delete'),
    ('super-admin', 'detendeur.read'),   ('super-admin', 'detendeur.create'),   ('super-admin', 'detendeur.update'),   ('super-admin', 'detendeur.delete'),
    ('super-admin', 'gilet.read'),       ('super-admin', 'gilet.create'),       ('super-admin', 'gilet.update'),       ('super-admin', 'gilet.delete'),
    ('super-admin', 'petit_materiel.read'),      ('super-admin', 'petit_materiel.create'),      ('super-admin', 'petit_materiel.update'),      ('super-admin', 'petit_materiel.delete'),
    ('super-admin', 'materiel_didactique.read'), ('super-admin', 'materiel_didactique.create'), ('super-admin', 'materiel_didactique.update'), ('super-admin', 'materiel_didactique.delete'),
    ('super-admin', 'piece_detachee.read'),      ('super-admin', 'piece_detachee.create'),      ('super-admin', 'piece_detachee.update'),      ('super-admin', 'piece_detachee.delete'),
    ('super-admin', 'compresseur.read'), ('super-admin', 'compresseur.create'), ('super-admin', 'compresseur.update'), ('super-admin', 'compresseur.delete'),
    ('super-admin', 'pret.read'),        ('super-admin', 'pret.create'),        ('super-admin', 'pret.update'),        ('super-admin', 'pret.delete'),
    ('super-admin', 'personne.read'),    ('super-admin', 'personne.create'),    ('super-admin', 'personne.update'),    ('super-admin', 'personne.delete'),
    ('super-admin', 'fournisseur.read'), ('super-admin', 'fournisseur.create'), ('super-admin', 'fournisseur.update'), ('super-admin', 'fournisseur.delete'),
    ('super-admin', 'referentiel.read'), ('super-admin', 'referentiel.create'), ('super-admin', 'referentiel.update'), ('super-admin', 'referentiel.delete'),
    ('super-admin', 'achat.read'),       ('super-admin', 'achat.create'),       ('super-admin', 'achat.update'),       ('super-admin', 'achat.delete'),
    ('super-admin', 'devis.read'),       ('super-admin', 'devis.create'),       ('super-admin', 'devis.update'),       ('super-admin', 'devis.delete'),
    ('super-admin', 'role.read'),        ('super-admin', 'role.assign'),        ('super-admin', 'role.assign_admin'),
    ('super-admin', 'compte.read'),      ('super-admin', 'compte.disable'),
    ('super-admin', 'superadmin.nominate'),
    ('super-admin', 'permission.read'),  ('super-admin', 'permission.manage'),
    ('super-admin', 'audit.read'),
    ('super-admin', 'status.terminal.override'),
    ('super-admin', 'utilisateur_legacy.read'), ('super-admin', 'utilisateur_legacy.create'), ('super-admin', 'utilisateur_legacy.update'), ('super-admin', 'utilisateur_legacy.delete');

-- -----------------------------------------------------------------------------
--  4. public.has_permission(code) — testée par toutes les policies RLS
-- -----------------------------------------------------------------------------

create or replace function public.has_permission(p_code text)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1
        from public.role_permission rp
        where rp.permission_code = p_code
          and rp.role = public.moamat_current_role()
    )
$$;

comment on function public.has_permission(text) is 'Vrai si le rôle courant (public.moamat_current_role()) détient la permission nommée.';

grant execute on function public.has_permission(text) to anon, authenticated;

commit;
