# Sécurité des données — MoaMat

Rôles, permissions nommées, Row Level Security, journal d'audit et Edge
Functions privilégiées. Ce document décrit le modèle **et** les tests de
sécurité qui le vérifient.

## 1. Ordre d'exécution des scripts

Dans l'éditeur SQL Supabase (ou `psql`), **dans cet ordre** :

| # | Fichier | Rôle |
|---|---------|------|
| 1 | `db/schema.sql` | 28 tables (miroir Access), index, RLS **activée sans policy** (deny-by-default) |
| 2 | `db/initial_load.sql` | reprise des données Access (généré) |
| 3 | `db/roles.sql` | type `app_role`, table `utilisateur_role` + trigger `on_auth_user_created`, fonctions de rôle, hook de jeton |
| 4 | `db/permissions.sql` | catalogue `permission`, matrice `role_permission`, `has_permission()` |
| 5 | `db/rls.sql` | policies explicites sur **toutes** les tables ; supprime `moamat_dev_all` |
| 6 | `db/audit.sql` | table `audit_log` append-only + triggers sur tables sensibles |
| 7 | `db/comptes.sql` | vue `compte_utilisateur` (liste des comptes) + fonction `set_compte_actif()` (désactivation, jamais de suppression) |
| 8 | `db/storage.sql` | buckets + policies Storage (réutilise les fonctions de `roles.sql`) |

Test : `db/tests/rls_tests.sql` (non destructif, `begin … rollback`).

## 2. Les 4 rôles

| Rôle | Rang | Public visé | Peut… |
|------|------|-------------|-------|
| `lecture` | 1 | **CA**, membre simple (« User ») | lire l'inventaire et les référentiels. Aucune écriture. Ne voit ni l'annuaire des membres, ni le journal d'audit, ni les rôles des autres comptes (seulement sa propre ligne). |
| `gestion` | 2 | équipe matériel | tout `lecture` + créer / modifier / supprimer les **items** (bouteilles, détendeurs, gilets, petit matériel, matériel didactique, pièces, relevés compresseur, prêts) + gérer l'annuaire des membres. **Pas** les référentiels, **pas** les finances, **pas** la liste des rôles. |
| `admin` | 3 | administration | tout `gestion` + référentiels (échéances, tarifs, sites, gaz, règles) + achats / factures / devis + **consultation du journal d'audit** + **écran `/comptes`** (liste des comptes, attribution des rôles `lecture` / `gestion`, désactivation d'un compte non-CA). |
| `super-admin` | 4 | siège unique, **vacant à l'initialisation** | tout `admin` + attribution des rôles `admin` / `super-admin` + nomination du super-admin + gestion du catalogue de permissions. |

### Source de vérité et absence de synchronisation client

- Le rôle est stocké dans **`public.utilisateur_role`** (une ligne par
  compte `auth.users`).
- À la création d'un compte, le trigger **`on_auth_user_created`**
  (`public.handle_new_user()`) insère une ligne au rôle **`lecture`**.
  Jamais `super-admin`, jamais un rôle élevé.
- Les policies RLS lisent le rôle via **`public.moamat_current_role()`**, qui
  interroge la table — **pas** un claim JWT que le client pourrait falsifier.
- **`public.custom_access_token_hook()`** recopie le rôle dans
  `app_metadata.role` du JWT, uniquement pour que le front Blazor pilote sa
  navigation. À activer une fois : Dashboard → Authentication → Hooks →
  *Custom Access Token* → `public.custom_access_token_hook`.
- Il n'y a **aucun code de synchronisation applicative** côté client.

### Attribuer / retirer un rôle

- `lecture` / `gestion` : par un `admin` ou `super-admin`, via l'écran
  **`/comptes`** ou en SQL (`update public.utilisateur_role …`).
- `admin` / `super-admin` : par un `super-admin` uniquement.
- Personne ne peut modifier **sa propre** ligne (anti-élévation, policy
  `utilisateur_role_upd` / `_del`).
- **Premier super-admin** : requête SQL documentée en bas de `db/roles.sql`,
  exécutée une fois par la personne qui administre le projet Supabase. Ensuite,
  Edge Function `nominate-super-admin`.

### Désactivation d'un compte (jamais de suppression)

- Un compte n'est **jamais supprimé** (traçabilité, intégrité des données
  historiques). Il est **désactivé** : `db/comptes.sql` →
  `public.set_compte_actif(user_id, actif)` bascule la colonne native
  `auth.users.banned_until` (`'infinity'` = désactivé, `NULL` = actif).
- La fonction (`SECURITY DEFINER`) revérifie la permission `compte.disable`
  (`admin` + `super-admin`), **refuse l'auto-désactivation**, et journalise
  (`compte.disabled` / `compte.enabled`).
- **Règle « compte CA »** : un compte CA = un compte au rôle **`lecture`**. Un
  `admin` ne peut **ni désactiver, ni supprimer la ligne de rôle** d'un compte
  `lecture` (ni d'un compte `admin` / `super-admin`) : seul un `super-admin`
  (permission `role.assign_admin`) le peut. Contrôlé côté données — fonction
  `set_compte_actif` **et** policy `utilisateur_role_del`.
- Liste des comptes : vue `public.compte_utilisateur` (e-mail, rôle, état),
  réservée à la permission `compte.read` — 0 ligne pour un appelant non habilité.

## 3. Permissions nommées

Catalogue dans `public.permission`, matrice dans `public.role_permission`
(intégralement en SQL — `db/permissions.sql`). Convention :
`<domaine>.<action>`.

- domaines « items » : `bouteille`, `detendeur`, `gilet`, `petit_materiel`,
  `materiel_didactique`, `piece_detachee`, `compresseur`, `pret` — actions
  `read` / `create` / `update` / `delete` ;
- transverses : `personne`, `fournisseur`, `referentiel`, `achat`, `devis` ;
- système : `role.read` / `role.assign` / `role.assign_admin`,
  `compte.read` / `compte.disable`, `superadmin.nominate`, `audit.read`,
  `permission.read` / `permission.manage`, `status.terminal.override`,
  `utilisateur_legacy.*`.

Les policies RLS n'écrivent **jamais** un rôle en dur : elles appellent
`public.has_permission('<domaine>.<action>')`.

## 4. Row Level Security

- RLS activée sur **toutes** les tables `public`, **sans exception**. Un
  contrôle en fin de `db/rls.sql` lève une exception si une table `public`
  (hors extensions) reste sans RLS.
- Aucune table n'est exposée sans policy explicite :
  - 28 tables métier : 4 policies chacune (`_sel` / `_ins` / `_upd` / `_del`),
    générées depuis la correspondance table → domaine ;
  - `utilisateur_role`, `permission`, `role_permission` : policies dédiées ;
  - `audit_log` : policy **SELECT seule** (`audit.read`) ; aucune policy
    d'écriture (réservée aux triggers).
- `anon` (non authentifié) : **aucune** policy ne le vise ⇒ accès nul sur tout
  le métier.
- `service_role` (Edge Functions) : contourne la RLS par nature (`BYPASSRLS`).

## 5. Journal d'audit

`public.audit_log` — **append-only** :

- pas de policy INSERT/UPDATE/DELETE ⇒ les clients ne peuvent pas écrire
  directement ;
- alimentation par triggers **SECURITY DEFINER** (`public.audit_write()`) ;
- deux triggers `BEFORE UPDATE` / `BEFORE DELETE` lèvent une exception — y
  compris pour le propriétaire de la table et pour `service_role` : aucune
  modification ni purge applicative.

Événements tracés :

| Action | Déclencheur |
|--------|-------------|
| `role.assigned` / `role.changed` / `role.revoked` | trigger sur `public.utilisateur_role` |
| `compte.disabled` / `compte.enabled` | RPC `public.set_compte_actif()` (écran `/comptes`) |
| `status.terminal` | triggers sur `bouteille` (déclassement), `detendeur` / `gilet` / `petit_materiel` / `materiel_didactique` (`est_declasse`), `pret` (clôture) |
| `superadmin.nominated` | Edge Function `nominate-super-admin` (via RPC `audit_write`) |

Consultation : écran **`/journal-audit`** de l'application, visible et
accessible **uniquement** aux rôles `admin` / `super-admin` (permission
`audit.read` côté RLS ; policy Blazor `role:admin+` côté UI).

## 6. Edge Functions privilégiées

Convention et procédure de déploiement CLI : `supabase/functions/README.md`.

Fonction de référence déployée : **`nominate-super-admin`** — nomme ou
transfère le siège de super-admin (vacant à l'initialisation, jamais par
défaut ni codé en dur). Elle vérifie l'appelant via son JWT, puis écrit avec
la clé `service_role` et journalise dans `audit_log`.

## 7. Tests de sécurité

### 7.1 Test automatisé

```bash
psql "$SUPABASE_DB_URL" -f db/tests/rls_tests.sql
# ou : coller le fichier dans l'éditeur SQL Supabase (rôle « postgres »)
```

Attendu en fin d'exécution :

```
NOTICE:  ===== TOUS LES CONTROLES SONT PASSES =====
ROLLBACK
```

Le script est **non destructif** (`begin … rollback`) : comptes de test,
lignes de test et entrées d'audit générées sont annulés.

### 7.2 Contrôles couverts (mapping avec les exigences)

| Exigence | Contrôle(s) dans `rls_tests.sql` |
|----------|----------------------------------|
| Un utilisateur **non authentifié** est bloqué sur toutes les tables métier | `anon — 0 … visible` (bouteille, détendeur, tarif, prêt, personne, facture), `anon — INSERT bouteille refusé` |
| Un utilisateur **User** ne peut pas modifier les référentiels (bouteilles, échéances, tarifs…) | `User — UPDATE/INSERT/DELETE référentiel tarif refusé` |
| Un utilisateur **CA** ne peut pas écrire sur les items (lecture seule) | `CA/User — INSERT/UPDATE bouteille refusé`, `CA/User — peut LIRE …` |
| Écriture pour l'**équipe matériel** (gestion / admin) | `gestion — INSERT/UPDATE bouteille autorisé`, `gestion — UPDATE détendeur autorisé` |
| **Administration** réservée à Super-admin / Admin | `gestion — INSERT rôle refusé`, `admin — UPDATE référentiel autorisé`, `admin — VOIT le journal d'audit`, `CA/User` & `gestion` — `NE VOIT PAS le journal d'audit` |
| Rôle `super-admin` jamais par défaut ; seul un super-admin élève à admin/super-admin | `nouveau compte -> rôle « lecture » par défaut`, `le trigger n'attribue jamais « super-admin »`, `admin — NE PEUT PAS attribuer le rôle admin / super-admin`, `super-admin — PEUT attribuer …` |
| Anti auto-promotion | `User — ne peut pas s'auto-promouvoir` |
| Journal d'audit append-only + alimenté par trigger | `audit — les changements de rôle sont tracés`, `audit — UPDATE/DELETE du journal refusé (append-only)`, `audit — UPDATE refusé même pour le propriétaire` |
| **Gestion des comptes** : un Admin ne peut pas désactiver / révoquer un compte **CA** (rôle `lecture`) ni un compte élevé ; auto-désactivation refusée ; seul un Super-admin le peut ; désactivation tracée ; aucune suppression physique | `compte — admin NE PEUT PAS désactiver un compte CA / un super-admin / lui-même`, `compte — admin NE PEUT PAS supprimer la ligne de rôle d'un compte CA`, `compte — désactivation d'un compte « gestion » par un admin prend effet` + `… tracée (compte.disabled)` + `réactivation … (compte.enabled)`, `compte — super-admin PEUT désactiver un compte CA` + `… supprimer la ligne de rôle d'un compte CA`, `compte — lecture NE VOIT PAS la liste` + `NE PEUT PAS (ré)activer un compte` |

### 7.3 Contrôles manuels complémentaires (recette)

À exécuter une fois l'app connectée à un vrai projet Supabase, avec un compte
par rôle :

1. **Non authentifié** — ouvrir l'app sans session : toutes les routes hors
   `/connexion` redirigent vers la connexion ; un appel direct PostgREST
   (`curl …/rest/v1/bouteille`) sans jeton renvoie `[]`.
2. **User / CA** (`lecture`) — l'inventaire s'affiche en lecture ; aucun bouton
   de création/édition ; les entrées « Comptes » et « Journal d'audit » sont
   absentes du menu ; `/journal-audit` et `/comptes` affichent « accès
   réservé » ; la vue `compte_utilisateur` renvoie 0 ligne.
3. **Équipe matériel** (`gestion`) — création/édition d'une bouteille OK ;
   tentative d'édition d'un tarif de requalification refusée.
4. **Admin** — édition des référentiels OK ; écran « Journal d'audit »
   accessible et peuplé ; écran **`/comptes`** accessible et peuplé ;
   attribution d'un rôle `gestion` OK ; attribution d'un rôle `admin` refusée ;
   désactivation / réactivation d'un compte `gestion` OK (visible dans le
   journal : `compte.disabled` / `compte.enabled`) ; bouton de désactivation
   inactif sur les comptes `lecture` (CA) et `admin` / `super-admin` ; appel
   direct de `set_compte_actif` sur un compte `lecture` ⇒ erreur `42501`.
5. **Super-admin** — attribution d'un rôle `admin` OK ; désactivation d'un
   compte `lecture` (CA) OK ; auto-désactivation refusée ; appel de l'Edge
   Function `nominate-super-admin` OK ; l'action apparaît dans le journal
   d'audit (`superadmin.nominated`).
6. **Audit inviolable** — via l'éditeur SQL avec la clé `service_role` :
   `update public.audit_log …` et `delete from public.audit_log …` échouent
   tous les deux.
