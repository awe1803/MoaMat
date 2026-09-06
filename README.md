# MoaMat

Gestion de l'inventaire du matériel de plongée de l'ASBL Royal Moana.

/ Inventory management for the Moana NPO diving gear.

## Pile technique

- **Front** : Blazor WebAssembly **PWA**, .NET 10, hébergement *standalone* (100 % statique).
- **Back** : [Supabase](https://supabase.com) (PostgreSQL managé) — seul backend, appelé
  depuis le client via le package `Supabase` (`supabase-csharp`).

## Arborescence

```
src/MoaMat.Web/         Application Blazor WASM PWA
src/MoaMat.Web/Auth/    Authentification Supabase (session persistée, rôles)
src/MoaMat.Web/Data/    Accès aux données Supabase (journal d'audit…)
db/schema.sql           Schéma PostgreSQL (28 tables + RLS activée sans policy)
db/roles.sql            Table utilisateur_role + trigger + fonctions de rôle
db/permissions.sql      Catalogue de permissions nommées + matrice par rôle
db/rls.sql              Policies RLS explicites sur toutes les tables
db/audit.sql            Journal d'audit append-only + triggers
db/storage.sql          Buckets Supabase Storage + policies d'accès par rôle
db/initial_load.sql     Reprise des données Access (généré)
db/tests/rls_tests.sql  Tests de sécurité RLS / rôles / audit (non destructif)
db/SECURITE.md          Modèle de sécurité + procédure de test
supabase/functions/     Edge Functions (convention + déploiement CLI)
tools/Generate-InitialLoad.ps1   Régénère db/initial_load.sql depuis Access_Data/csv/
Access_Data/            Export CSV de l'ancienne base Access + doc de nommage
Analyse/                Analyse fonctionnelle et plan de reprise
```

## Démarrage — application

```bash
cp src/MoaMat.Web/wwwroot/appsettings.sample.json src/MoaMat.Web/wwwroot/appsettings.json
# éditer appsettings.json : renseigner Supabase:Url et Supabase:AnonKey
dotnet restore MoaMat.slnx
dotnet build   MoaMat.slnx
dotnet run --project src/MoaMat.Web
```

### Configuration Supabase

`Supabase:Url` + `Supabase:AnonKey` (clé *publishable* / anon). Cette clé est
publique par nature dans une app WebAssembly : le contrôle d'accès réel repose sur
les policies **RLS** définies dans `db/schema.sql`.

- **En local** : `src/MoaMat.Web/wwwroot/appsettings.json`, copié depuis
  [`appsettings.sample.json`](src/MoaMat.Web/wwwroot/appsettings.sample.json). Ce
  fichier **n'est pas versionné** (`.gitignore`).
- **En CI** : le workflow de déploiement génère `appsettings.json` à partir des
  **Variables de dépôt** `SUPABASE_URL` et `SUPABASE_ANON_KEY`
  (*Settings → Secrets and variables → Actions → Variables*). Le workflow
  keepalive lit les mêmes Variables. Rien n'est commité en clair.

**Seule la clé anon voyage côté client.** La clé `service_role` (et toute clé
`sb_secret_…`) ne doit jamais figurer dans le code, `appsettings*.json`, les
Variables/Secrets lus par `deploy.yml`, ni les assets publiés. Deux garde-fous :

- au démarrage, `Program.cs` décode la clé configurée et journalise une **erreur**
  si son rôle JWT n'est pas `anon` (ou si c'est une clé `sb_secret_…`) ;
- l'étape *« Assert only the anon key ships to the client »* de `deploy.yml`
  échoue (donc bloque le déploiement) si `service_role` / `sb_secret_` apparaît
  dans `dist/wwwroot`, ou si la clé embarquée n'est pas une clé publique.

### Authentification & rôles

- Connexion / déconnexion et réinitialisation de mot de passe : Supabase Auth
  (Gotrue), via [`src/MoaMat.Web/Auth/`](src/MoaMat.Web/Auth/). Pages
  `/connexion`, `/mot-de-passe-oublie`, `/reinitialiser-mot-de-passe`,
  `/deconnexion`. Toutes les autres routes exigent une session (`[Authorize]`).
- **Session persistée** dans le `localStorage` du navigateur
  (`BrowserSessionPersistence`) : l'utilisateur reste connecté d'un rechargement
  ou d'une réouverture de la PWA à l'autre ; le jeton est rafraîchi
  automatiquement (`AutoRefreshToken`).
- **Rôle applicatif** (`lecture` < `gestion` < `admin` < `super-admin`).
  Source de vérité : la table `public.utilisateur_role`, alimentée par le
  trigger Postgres `on_auth_user_created` au rôle `lecture` à la création du
  compte (jamais un rôle élevé). Aucune synchronisation applicative côté
  client. Les policies RLS lisent le rôle dans cette table ; le hook
  `public.custom_access_token_hook` le recopie dans le claim JWT
  `app_metadata.role`, que Blazor lit pour piloter la navigation (politiques
  `role:lecture+` … `role:super-admin`, voir `MoaMatRoles`). Détail complet du
  modèle (permissions nommées, RLS, audit, tests) : [`db/SECURITE.md`](db/SECURITE.md).
- **Configuration Supabase requise** : *Authentication → Hooks* → activer
  *Custom Access Token* → `public.custom_access_token_hook`.
- **Configuration Supabase requise** : *Authentication → URL Configuration* →
  ajouter `<origine>/reinitialiser-mot-de-passe` aux *Redirect URLs* (local **et**
  URL GitHub Pages) pour que le lien de récupération revienne dans l'app.

## Déploiement

Cible : **GitHub Pages**, site de projet — `https://<org>.github.io/<dépôt>/`.
Application 100 % statique (Blazor WASM standalone), servie sans runtime ASP.NET
Core.

Workflow [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) :

- déclenché à chaque push sur `main` (et manuellement via *workflow_dispatch*) ;
- `dotnet publish -c Release` → l'artefact `dist/wwwroot` est publié via
  `actions/upload-pages-artifact` + `actions/deploy-pages` (pas de branche
  `gh-pages`) ;
- le `<base href>` est réécrit **avant** `publish` en `/<PAGES_BASE_PATH>/`
  (sinon les empreintes SRI du service worker sont invalidées).
  `PAGES_BASE_PATH` vaut le nom du dépôt par défaut, surchargeable par une
  Variable de dépôt du même nom ;
- une étape *smoke test* sert `dist/wwwroot` avec `python3 -m http.server` (aucune
  dépendance .NET) et vérifie que l'app se charge sous le sous-chemin ;
- **build cassé = déploiement bloqué** : le job `deploy` a `needs: build` ; si une
  étape de `build` échoue (Variable manquante, compilation, smoke test…),
  l'artefact n'est pas produit et le site en ligne reste inchangé.

Prérequis côté dépôt : *Settings → Pages → Source = GitHub Actions*, et les
Variables `SUPABASE_URL` / `SUPABASE_ANON_KEY` définies.

## Démarrage — base de données

Dans l'éditeur SQL Supabase (ou via `psql`), exécuter **dans l'ordre** :

1. [`db/schema.sql`](db/schema.sql) — 28 tables, index ; RLS **activée sans
   policy** (deny-by-default).
2. [`db/initial_load.sql`](db/initial_load.sql) — charge les données reprises de
   l'ancienne base Access. Ré-exécutable (commence par
   `truncate ... restart identity cascade`).
3. [`db/roles.sql`](db/roles.sql) — type `app_role`, table
   `public.utilisateur_role` liée à `auth.users`, trigger
   `on_auth_user_created`, fonctions de rôle, hook de jeton d'accès.
4. [`db/permissions.sql`](db/permissions.sql) — catalogue `public.permission`,
   matrice `public.role_permission`, `public.has_permission()`.
5. [`db/rls.sql`](db/rls.sql) — policies RLS explicites sur **toutes** les
   tables ; supprime la policy permissive `moamat_dev_all`.
6. [`db/audit.sql`](db/audit.sql) — journal `public.audit_log` append-only et
   triggers sur les tables sensibles.
7. [`db/storage.sql`](db/storage.sql) — buckets Supabase Storage
   (`materiel-photos`, `certificats-requalification`, `factures`, tous privés)
   et policies d'accès par rôle sur `storage.objects`. Ré-exécutable.

Tous ces scripts sont ré-exécutables. Ensuite : activer le hook
*Custom Access Token* (Dashboard → Authentication → Hooks →
`public.custom_access_token_hook`) et nommer le premier super-admin (requête
documentée en bas de [`db/roles.sql`](db/roles.sql)).

Tests de sécurité : `psql "$SUPABASE_DB_URL" -f db/tests/rls_tests.sql`
(non destructif). Modèle complet : [`db/SECURITE.md`](db/SECURITE.md).

Compte administrateur de démarrage (dev / première connexion) :
[`db/seed_admin.sql`](db/seed_admin.sql) — crée `admin@moamat.local` /
`MoAdmin1234` au rôle `admin` (mot de passe à changer ensuite). Le
**super-admin** reste vacant : le poser via la requête en bas de
[`db/roles.sql`](db/roles.sql).

Contrôle rapide des volumes après chargement :

```sql
select 'bouteille' t, count(*) from bouteille
union all select 'bouteille_requalification', count(*) from bouteille_requalification
union all select 'detendeur', count(*) from detendeur
union all select 'petit_materiel', count(*) from petit_materiel
union all select 'personne', count(*) from personne;
```

Les comptes doivent correspondre à la colonne `nb_lignes` de
[`Access_Data/_dictionnaire_tables.csv`](Access_Data/_dictionnaire_tables.csv).

## Régénérer la reprise

```powershell
pwsh ./tools/Generate-InitialLoad.ps1
```

Le script relit les CSV de `Access_Data/csv/`, nettoie les valeurs (sentinelles de
dates Access → `NULL`, drapeaux `0/1` → booléens, virgules décimales, apostrophes)
et réécrit `db/initial_load.sql`. Les 8 tables `zz_` de l'export et le mot de passe
en clair de `utilisateur` ne sont volontairement pas repris.

## Edge Functions

Fonctions serverless Supabase (Deno / TypeScript) dans
[`supabase/functions/`](supabase/functions/). Convention (un dossier =
une fonction, `_shared/` pour le commun) et procédure de déploiement CLI
(`supabase link`, `supabase functions deploy`) : voir
[`supabase/functions/README.md`](supabase/functions/README.md).

Fonction de référence déployée : **`nominate-super-admin`** — nomme ou
transfère le siège de super-admin (vacant à l'initialisation, jamais par
défaut ni codé en dur), avec vérification de l'appelant et journalisation
dans `audit_log`.

## Journal d'audit

Écran **`/journal-audit`** dans l'application, réservé aux rôles `admin` /
`super-admin`. Il liste le contenu de `public.audit_log` (append-only) :
changements de rôle, changements de statut terminal, nominations. Le lien
n'apparaît dans le menu que pour ces rôles.

## Notes de modélisation

`db/schema.sql` reproduit **fidèlement** la structure de l'export Access (noms de
`Access_Data/Nommage_tables_MOANA.md`). La normalisation métier (fusion des tables
`*_sortie_inventaire`, suppression des colonnes calculées, typage fin des mesures
type « 12 Li » / « 14,3 », activation des clés étrangères) est un chantier distinct :
les contraintes FK sont pré-écrites mais commentées en fin de `schema.sql`.

## Architecture cible

Découpage visé, **non encore implémenté** :

| Couche | Rôle | Contenu prévu |
|--------|------|---------------|
| Domaine | modèles métier | classes POCO mappées sur les tables de `db/schema.sql` (attributs `[Table]` / `BaseModel` de `supabase-csharp`) |
| Services | accès données | interfaces + implémentations encapsulant le `Supabase.Client` (une par agrégat : bouteilles, détendeurs, prêts…), injectées dans l'UI |
| UI | présentation | composants Razor, aucun appel Supabase direct |

État actuel : le `Supabase.Client` est enregistré en DI dans
[`Program.cs`](src/MoaMat.Web/Program.cs). La couche **authentification** est en
place (`src/MoaMat.Web/Auth/` : session persistée, `AuthenticationStateProvider`,
`AuthService`, pages de connexion / réinitialisation, garde de routes, rôles). Le
reste — modèles POCO, services d'accès aux données par agrégat, UI métier — n'est
pas encore implémenté (l'UI hors auth est encore le gabarit Home / Counter /
Weather). Cette mise en couches est liée à la normalisation métier décrite
ci-dessus et sera traitée séparément.
