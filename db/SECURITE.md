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
| 3 | `db/roles.sql` | type `app_role` (5 rôles, dont `en_attente`), table `utilisateur_role` + trigger `on_auth_user_created`, fonctions de rôle, hook de jeton, protection du super-admin |
| 4 | `db/permissions.sql` | catalogue `permission`, matrice `role_permission`, `has_permission()` |
| 5 | `db/rls.sql` | policies explicites sur **toutes** les tables ; supprime `moamat_dev_all` |
| 6 | `db/audit.sql` | table `audit_log` append-only + triggers sur tables sensibles |
| 7 | `db/comptes.sql` | vue `compte_utilisateur` (liste des comptes) + `set_compte_actif()` (désactivation, réversible) + `supprimer_compte()` (suppression définitive, super-admin uniquement) |
| 8 | `db/storage.sql` | buckets + policies Storage (réutilise les fonctions de `roles.sql`) |

Test : `db/tests/rls_tests.sql` (non destructif, `begin … rollback`).

## 2. Les 5 rôles

| Rôle | Rang | Public visé | Peut… |
|------|------|-------------|-------|
| `en_attente` | 0 | **compte fraîchement inscrit**, en attente d'activation | **RIEN** : aucune permission (aucune ligne dans `role_permission`). Ne voit que sa **propre** ligne `utilisateur_role` — aucune autre table, aucun écran. |
| `lecture` | 1 | **CA**, membre simple (« User ») | lire l'inventaire et les référentiels. Aucune écriture. Ne voit ni l'annuaire des membres, ni le journal d'audit, ni les rôles des autres comptes (seulement sa propre ligne). |
| `gestion` | 2 | équipe matériel | tout `lecture` + créer / modifier / supprimer les **items** (bouteilles, détendeurs, gilets, petit matériel, matériel didactique, pièces, relevés compresseur, prêts) + gérer l'annuaire des membres. **Pas** les référentiels, **pas** les finances, **pas** la liste des rôles. |
| `admin` | 3 | administration | tout `gestion` + référentiels (échéances, tarifs, sites, gaz, règles) + achats / factures / devis + **consultation du journal d'audit** + **écran `/comptes`** (liste des comptes, attribution des rôles `lecture` / `gestion`, désactivation d'un compte non-CA). |
| `super-admin` | 4 | siège unique, **vacant à l'initialisation** | tout `admin` + attribution des rôles `admin` / `super-admin` + nomination du super-admin + gestion du catalogue de permissions + **suppression définitive d'un compte** (départ du club — jamais un compte super-admin). |

### Source de vérité et absence de synchronisation client

- Le rôle est stocké dans **`public.utilisateur_role`** (une ligne par
  compte `auth.users`).
- À la création d'un compte, le trigger **`on_auth_user_created`**
  (`public.handle_new_user()`) insère une ligne au rôle **`en_attente`**.
  Jamais `super-admin`, jamais un rôle élevé, jamais `lecture` directement.
- Les policies RLS lisent le rôle via **`public.moamat_current_role()`**, qui
  interroge la table — **pas** un claim JWT que le client pourrait falsifier.
- **`public.custom_access_token_hook()`** recopie le rôle dans
  `app_metadata.role` du JWT, uniquement pour que le front Blazor pilote sa
  navigation. À activer une fois : Dashboard → Authentication → Hooks →
  *Custom Access Token* → `public.custom_access_token_hook`.
- Il n'y a **aucun code de synchronisation applicative** côté client.

### Compte « en attente » (cycle de vie complet d'une inscription)

- Un utilisateur qui s'inscrit via Supabase Auth — que ce soit via l'écran
  **`/inscription`** de l'app ou directement via l'API Supabase — n'a
  **jamais** d'accès direct à l'application : son compte reste **`en_attente`**,
  sans aucune permission, tant qu'un `admin` / `super-admin` ne l'a pas
  explicitement **activé et affecté à un rôle** (`lecture` / `gestion` /
  `admin`) via l'écran `/comptes`. La confirmation par e-mail est désactivée
  par choix produit (`supabase/config.toml`, `[auth.email]
  enable_confirmations = false` — à répercuter côté projet hébergé) : ce n'est
  qu'une vérification de possession de l'adresse, pas une porte d'accès —
  celle-ci reste entièrement portée par le rôle `en_attente` et la RLS. L'écran
  `/inscription` ne connecte jamais l'appelant, même si GoTrue ouvre une
  session à l'inscription : `SupabaseAuthenticationService.SignUpAsync` la
  ferme systématiquement.
- Aucune policy dédiée n'est nécessaire au-delà du modèle existant : `en_attente`
  n'a **aucune** ligne dans `public.role_permission`, donc `has_permission()`
  renvoie faux partout ; seule la policy `utilisateur_role_sel`
  (`user_id = auth.uid()`) lui laisse voir **sa propre** ligne de rôle — rien
  d'autre. C'est la policy RLS dédiée à l'état « en attente ».

### Attribuer / retirer un rôle

- `lecture` / `gestion` : par un `admin` ou `super-admin`, via l'écran
  **`/comptes`** ou en SQL (`update public.utilisateur_role …`) — y compris
  pour **activer** un compte `en_attente`.
- `admin` / `super-admin` : par un `super-admin` uniquement.
- Personne ne peut modifier **sa propre** ligne (anti-élévation, policy
  `utilisateur_role_upd` / `_del`).
- **Premier super-admin** : requête SQL documentée en bas de `db/roles.sql`,
  exécutée une fois par la personne qui administre le projet Supabase. Ensuite,
  Edge Function `nominate-super-admin`.

### Protection du super-admin (niveau base)

Trois triggers dans `db/roles.sql`, appliqués **même à un appelant qui
contourne la RLS** (`postgres`, `service_role`), sur le modèle du verrou
append-only de `public.audit_log` :

- **Suppression impossible** : ni la ligne `public.utilisateur_role` d'un
  super-admin, ni son compte `auth.users`, ne peuvent être supprimés. Seule une
  **rétrogradation** (`UPDATE … set role = …`) est possible.
- **Jamais zéro super-admin actif** : une rétrogradation qui ne laisserait plus
  aucun super-admin **actif** (compte non désactivé) est refusée. La même
  invariante est vérifiée côté **désactivation** dans
  `public.set_compte_actif()` (`db/comptes.sql`) : désactiver le dernier
  super-admin actif est refusé.
- **Modification réservée à un autre super-admin** : côté PostgREST (rôle
  `authenticated`), seul un autre super-admin peut modifier la ligne d'un
  super-admin — défense en profondeur, la RLS (`role.assign_admin`) impose déjà
  la même règle.

### Désactivation d'un compte (réversible)

- Un compte peut être **désactivé** sans être supprimé : `db/comptes.sql` →
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

### Suppression définitive d'un compte (départ du club)

- `db/comptes.sql` → `public.supprimer_compte(user_id)` — **irréversible**,
  contrairement à la désactivation : le compte `auth.users` et sa ligne
  `utilisateur_role` (cascade) disparaissent. Le journal d'audit conserve la
  trace (e-mail, rôle) d'avant suppression.
- Réservée à la permission **`compte.delete`** (super-admin **uniquement** — un
  `admin` ne peut pas supprimer de compte, seulement désactiver).
  **Auto-suppression refusée.**
- Un compte **super-admin ne peut jamais être supprimé** : la fonction le
  refuse explicitement, et le trigger `protect_super_admin_account_delete`
  (`db/roles.sql`, §"Protection du super-admin" ci-dessus) l'interdit de toute
  façon de manière inconditionnelle — il faut d'abord rétrograder son rôle.
- Journalisé (`compte.deleted`).

## 3. Permissions nommées

Catalogue dans `public.permission`, matrice dans `public.role_permission`
(intégralement en SQL — `db/permissions.sql`). Convention :
`<domaine>.<action>`.

- domaines « items » : `bouteille`, `detendeur`, `gilet`, `petit_materiel`,
  `materiel_didactique`, `piece_detachee`, `compresseur`, `pret` — actions
  `read` / `create` / `update` / `delete` ;
- transverses : `personne`, `fournisseur`, `referentiel`, `achat`, `devis` ;
- système : `role.read` / `role.assign` / `role.assign_admin`,
  `compte.read` / `compte.disable` / `compte.delete` (super-admin uniquement),
  `superadmin.nominate`, `audit.read`,
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
| `compte.deleted` | RPC `public.supprimer_compte()` (écran `/comptes`, suppression définitive) |
| `status.terminal` | triggers sur `bouteille` (déclassement), `detendeur` / `gilet` / `petit_materiel` / `materiel_didactique` (`est_declasse`), `pret` (clôture) |
| `superadmin.nominated` | Edge Function `nominate-super-admin` (via RPC `audit_write`) |

Machine à états de l'item (`db/item_etat.sql`) : en complément de `audit_log`
ci-dessus, chaque transition de `item.statut_code` est aussi journalisée avec
son motif, sa date d'effet, son autorité décisionnaire et sa pièce jointe dans
`public.item_transition` (append-only, même principe qu'`audit_log` : pas de
policy insert/update/delete, seul le trigger `SECURITY DEFINER`
`tg_item_valider_transition_statut` y écrit). Détail des règles :
[`db/MODELE.md`](MODELE.md) §9.

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
| Rôle `super-admin` jamais par défaut ; seul un super-admin élève à admin/super-admin | `nouveau compte -> rôle « en_attente » par défaut`, `le trigger n'attribue jamais « super-admin »`, `admin — NE PEUT PAS attribuer le rôle admin / super-admin`, `super-admin — PEUT attribuer …` |
| Anti auto-promotion | `User — ne peut pas s'auto-promouvoir` |
| Un nouveau compte est `en_attente` (aucune permission) et ne voit que son propre profil | `en_attente — role courant = en_attente`, `en_attente — ne voit que sa propre ligne de role`, `en_attente — 0 … visible` (bouteille, personne, audit, comptes), `en_attente — ne peut pas s'auto-activer` |
| Un super-admin ne peut être ni supprimé (ligne de rôle ou compte), ni rétrogradé/désactivé si c'est le dernier actif | `super-admin — retrogradation refusee : dernier super-admin actif`, `super-admin — suppression de la ligne de role refusee`, `super-admin — suppression du compte auth.users refusee`, `compte — desactivation refusee : dernier super-admin actif` |
| Journal d'audit append-only + alimenté par trigger | `audit — les changements de rôle sont tracés`, `audit — UPDATE/DELETE du journal refusé (append-only)`, `audit — UPDATE refusé même pour le propriétaire` |
| **Gestion des comptes** : un Admin ne peut pas désactiver / révoquer un compte **CA** (rôle `lecture`) ni un compte élevé ; auto-désactivation refusée ; seul un Super-admin le peut ; désactivation tracée | `compte — admin NE PEUT PAS désactiver un compte CA / un super-admin / lui-même`, `compte — admin NE PEUT PAS supprimer la ligne de rôle d'un compte CA`, `compte — désactivation d'un compte « gestion » par un admin prend effet` + `… tracée (compte.disabled)` + `réactivation … (compte.enabled)`, `compte — super-admin PEUT désactiver un compte CA` + `… supprimer la ligne de rôle d'un compte CA`, `compte — lecture NE VOIT PAS la liste` + `NE PEUT PAS (ré)activer un compte` |
| **Suppression définitive d'un compte** (départ du club) : réservée au super-admin, auto-suppression refusée, un compte super-admin ne peut jamais être supprimé, suppression tracée | `compte — admin NE PEUT PAS supprimer un compte (compte.delete reservee au super-admin)`, `compte — super-admin ne peut pas s'auto-supprimer`, `compte — super-admin NE PEUT PAS supprimer un autre compte super-admin`, `compte — la suppression definitive retire le compte auth.users` + `… retire la ligne de role (cascade)` + `… est tracee (compte.deleted)`, `compte — suppression d'un compte deja supprime refusee (introuvable)` |

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
   d'audit (`superadmin.nominated`) ; un compte `lecture` / `gestion` /
   `admin` peut être **supprimé définitivement** (bouton dédié sur
   `/comptes`) — le compte disparaît de la liste et l'action apparaît dans le
   journal (`compte.deleted`) ; auto-suppression refusée ; bouton de
   suppression absent / inactif sur un compte `super-admin` (y compris pour un
   autre super-admin) et appel direct de `supprimer_compte` sur un compte
   `super-admin` ⇒ erreur `42501`.
6. **Audit inviolable** — via l'éditeur SQL avec la clé `service_role` :
   `update public.audit_log …` et `delete from public.audit_log …` échouent
   tous les deux.
