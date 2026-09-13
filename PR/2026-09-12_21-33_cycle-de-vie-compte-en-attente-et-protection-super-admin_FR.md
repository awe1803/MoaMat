# MoaMat — Cycle de vie du compte « en attente » & protection base du super-admin

**Date :** 2026-09-12 21:33
**Branche :** `38-gestion-des-comptes-utilisateurs`
**Périmètre :** `db/roles.sql`, `db/permissions.sql`, `db/rls.sql`, `db/comptes.sql`, `db/tests/rls_tests.sql`, `db/SECURITE.md`, `README.md`, `AppRole` / `UserAccount` / `AccountManagement.razor` (domaine .NET + UI), tests unitaires.

---

## 1. Verdict

✅ **Acceptable.**

L'essentiel de la fonctionnalité demandée existait déjà sur cette branche (table de rôles alimentée par un trigger `on_auth_user_created`, permissions nommées, RLS sur toutes les tables, journal d'audit append-only). Deux écarts réels subsistaient par rapport à l'énoncé :

1. Un compte nouvellement inscrit recevait directement le rôle `lecture`, qui **porte déjà de vraies permissions métier en lecture** — pas le comportement « aucun accès tant que non activé » demandé.
2. La protection du super-admin n'existait qu'au niveau RLS/permission (`role.assign_admin`, réservé au super-admin). Aucune garantie **au niveau trigger**, en dur, n'empêchait la suppression d'un compte super-admin ni ne garantissait qu'une rétrogradation/désactivation ne laisse jamais zéro super-admin actif — le genre d'invariant qui doit tenir même face à une session SQL directe ou une clé service-role compromise.

Les deux écarts sont comblés par un 5e rôle (`en_attente`, rang 0, aucune permission) et trois triggers en défense en profondeur. Le build est vert, les 121 tests unitaires passent. Le défaut `en_attente` s'intègre directement dans la mécanique existante `has_permission()` / RLS — aucune nouvelle forme de policy n'a été nécessaire, seulement l'absence de lignes `role_permission` pour ce rôle.

Un point de contrôle de conception a été soumis au tech lead avant finalisation (voir §6) — les deux réponses sont reflétées dans le code ci-dessus.

---

## 2. Problèmes critiques (impact production)

Aucun dans l'état final. Deux ont été détectés et corrigés pendant cette passe ; documentés comme « aurait pu être » :

### 2.1 — (détecté avant commit) Le rattrapage aurait silencieusement coupé l'accès de comptes existants

L'insertion idempotente de « rattrapage » (`db/roles.sql`, bloc de commentaire « Rattrapage ») a d'abord été écrite pour réutiliser `en_attente`, par cohérence avec le nouveau défaut du trigger. Ré-exécutée sur une base **déjà provisionnée**, tout compte qui — pour une raison quelconque — n'avait jamais reçu de ligne `utilisateur_role` serait passé d'un accès implicite `lecture` à zéro accès, silencieusement, à la prochaine ré-exécution du script. Revenu à `lecture` pour ce chemin de rattrapage spécifique ; seul le trigger des *nouvelles* inscriptions utilise `en_attente`. Confirmé avec le tech lead (§6).

### 2.2 — (décision de conception, pas un bug) Compte de rôles vs compte de comptes actifs pour « dernier super-admin »

La première version de `tg_protect_super_admin_update()` comptait les *lignes* au rôle `super-admin`, pas les *actives*. Cela garantit techniquement « un rôle super-admin existe toujours », mais pas « un super-admin est toujours en mesure d'agir » si le seul restant est banni. Corrigé en joignant `auth.users` et en excluant les comptes bannis du décompte — reflète le contrôle équivalent désormais ajouté à `set_compte_actif()`.

---

## 3. Améliorations recommandées

- **`AccountManagement.razor` — un compte en attente s'affichait avec un rôle trompeur.** Le `<select>` lié à `account.Role.Code` ne listait que `AppRole.Assignable` (lecture/gestion/admin/super-admin) ; le code d'un compte en attente (`en_attente`) ne correspondait à rien, si bien que le navigateur aurait silencieusement affiché la première option (`lecture`) comme si c'était le rôle réel du compte. Corrigé en injectant une option `en_attente` désactivée en placeholder quand `account.IsPending`, plus un badge « en attente » explicite. **À vérifier :** si ce projet dispose d'une suite de tests visuels par capture d'écran, la relancer sur `/comptes` avec un compte en attente dans les données de test.
- **La branche « restriction à l'acteur » de `tg_protect_super_admin_update` est limitée à `current_setting('role', true) = 'authenticated'`.** C'est voulu (reflète la façon dont la RLS traite `service_role`/`postgres` comme des opérateurs de confiance), mais c'est une comparaison de chaîne sur le nom du rôle Postgres posé par PostgREST — si le projet renomme un jour le rôle « authenticator » de PostgREST, ce contrôle cesserait silencieusement de se déclencher. Risque faible aujourd'hui (la RLS bloque déjà le même scénario), mais mérite une ligne de note dans `db/SECURITE.md` si ce nom de rôle devient un jour configurable. Non bloquant.
- **`db/roles.sql` documente toujours en bas de fichier le SQL de bootstrap du premier super-admin, inchangé.** À vérifier que le bootstrap d'un projet **neuf** (`auth.users` vide, première exécution de `roles.sql`) fonctionne toujours : la première nomination est un `INSERT ... ON CONFLICT DO UPDATE`, pas un `UPDATE`, donc aucun des trois nouveaux triggers ne se déclenche dessus (correct — confirmé à la lecture des conditions des triggers, qui n'agissent que sur `DELETE`/`UPDATE`, jamais `INSERT`). Aucun changement de code nécessaire, signalé pour information du relecteur uniquement.

---

## 4. Commentaires en ligne (équivalent « commentaires GitLab »)

| Fichier | Ligne(s) | Commentaire |
|---|---|---|
| `db/roles.sql` | ~59 | `alter type ... add value if not exists 'en_attente' before 'lecture'` s'exécute sans condition à chaque exécution du script, y compris juste après le `create type` qui l'inclut déjà — correct (no-op idempotent), mais mérite un test de fumée sur une base où le type précède ce changement, pour confirmer que `ADD VALUE ... BEFORE` accepte gracieusement une valeur déjà en première position (c'est le cas, selon la sémantique Postgres : no-op quand `IF NOT EXISTS` trouve la valeur déjà présente, et `BEFORE`/`AFTER` ne sont évalués que quand la valeur n'existe pas encore). |
| `db/roles.sql` | ~292-297 | Le décompte des « super-admin actifs » est dupliqué (quasi mot pour mot) entre `tg_protect_super_admin_update()` et `set_compte_actif()`. Les deux sont courts et autonomes ; extraire un helper partagé `public.moamat_active_super_admin_count(exclude uuid)` serait une suite légitime, mais ne vaut pas le coup maintenant pour deux requêtes de trois lignes — signalé comme optionnel, pas requis. |
| `db/tests/rls_tests.sql` | nouvelle §9 | Le compte de test `a7` est créé mais jamais élevé, volontairement (c'est le témoin « reste en attente pour toujours »). Le commentaire le précise déjà — bien. |
| `src/MoaMat.Web/Pages/AccountManagement.razor` | bloc select | L'option placeholder `en_attente` désactivée n'est rendue que `@if (account.IsPending)` ; une fois qu'un admin affecte un vrai rôle, la ligne se re-rend sans elle au prochain `ReloadAsync()` — vérifié que cela correspond au motif existant de rechargement après écriture déjà utilisé par `ChangeRoleAsync`. |

---

## 5. Tests ajoutés

- SQL (`db/tests/rls_tests.sql`, nouvelle §9, compte `en_attente` `a7`) :
  - le rôle par défaut à l'inscription est `en_attente`, pas `lecture`.
  - `en_attente` ne voit que sa propre ligne `utilisateur_role` ; zéro ligne sur `bouteille`, `personne`, `audit_log`, `compte_utilisateur`.
  - `en_attente` ne peut pas s'auto-activer (écriture refusée).
  - un super-admin actif seul ne peut pas être rétrogradé (trigger de rôle).
  - la rétrogradation d'un super-admin réussit dès qu'un second super-admin actif existe, puis échoue de nouveau quand il n'en reste qu'un.
  - la ligne `utilisateur_role` d'un super-admin ne peut pas être supprimée, même par `postgres`.
  - le compte `auth.users` d'un super-admin ne peut pas être supprimé, même par `postgres`.
  - désactiver le dernier super-admin actif via `set_compte_actif` est refusé, même quand l'appelant détient le rôle `super-admin` mais est lui-même banni.
- .NET (`AppRoleTests`, `AccountAdministrationPolicyTests`) :
  - `AppRole.FromCode("en_attente")` correspond à un rôle distinct de `AppRole.None`, rang 0.
  - `AppRole.Pending` est exclu de `AppRole.Assignable`.
  - un administrateur peut changer le rôle d'un compte en attente (c.-à-d. l'activer).

### Non couvert — signalé pour suite, non bloquant

- Aucun test n'exerce directement, au niveau SQL, la **branche de restriction à l'acteur du trigger** (`current_setting('role', true) = 'authenticated'`) — elle est couverte indirectement par le test RLS existant (« admin NE PEUT PAS attribuer le role admin/super-admin »), la RLS filtrant la ligne avant même que le trigger ne s'exécute. Un test direct nécessiterait de forcer `current_setting('role')` à `'authenticated'` tout en exécutant *en tant que* rôle non privilégié, ce que le motif actuel `set local role authenticated` fait déjà — mais aucun scénario actuel n'atteint le trigger avec une ligne que la RLS aurait laissée passer et que seul le trigger bloque. À envisager si cette branche devient un jour la défense *principale* plutôt qu'une défense en profondeur.
- Aucun test de composant Blazor (bUnit ou équivalent) pour le nouveau badge/placeholder de compte en attente dans `AccountManagement.razor` — ce dépôt ne semble pas disposer aujourd'hui de tests UI au niveau composant (seulement des tests unitaires de domaine), donc aucun n'a été ajouté, cohérent avec la forme de couverture de tests existante.

---

## 6. Point de contrôle résolu avec le tech lead

1. **Forme du modèle « en attente » :** rôle distinct `en_attente` (rang 0, aucune permission) vs réutilisation de `lecture` avec un drapeau caché → **confirmé : rôle distinct**, tel qu'implémenté.
2. **Comportement du rattrapage pour les comptes existants sans ligne de rôle :** continuer à poser `en_attente` par défaut (cohérent avec le nouveau trigger) vs garder `lecture` pour le rattrapage seul → **confirmé : garder `lecture` pour le rattrapage**, seul le trigger des nouvelles inscriptions utilise `en_attente`. Code mis à jour en conséquence (§2.1 ci-dessus).

---

## 7. Refactoring optionnel

Aucun jugé assez porteur de valeur pour être fait de manière proactive. Le fichier `db/roles.sql` s'allonge (367 lignes) — extraire la section de protection du super-admin (§6 du fichier) dans un `db/superadmin_protect.sql` dédié, exécuté juste après `db/roles.sql`, garderait chaque fichier mono-responsabilité, conformément à la convention existante du projet (un fichier par préoccupation : `db/audit.sql`, `db/comptes.sql`, etc. sont déjà séparés). Non fait ici pour éviter d'élargir le diff et la documentation de l'ordre d'exécution (README, SECURITE.md) au-delà de ce qui était demandé ; à envisager dans une future passe si `roles.sql` continue de grossir.
