# Migration vers Entity Framework Core — étapes d'exécution

Document de travail : la suite d'étapes à dérouler pour préparer la sortie de
Supabase **sans l'interrompre**. Rédigé le 2026-09-09, à exécuter plus tard.

Le raisonnement complet (analyse du couplage, alternatives écartées) tient dans
les ADR du lot 0 ; ce fichier est la liste d'actions.

---

## Pourquoi

Supabase est transitoire : il fait tourner l'application sur GitHub Pages en
attendant les accès au serveur du club. La question posée est celle du coût de
sortie, en particulier pour l'authentification, faite nativement dans GoTrue.

**Constat 1 — côté C#, le joint est déjà au bon endroit.** Le refactor hexagonal
de la branche `39-correction-base-bonnes-pratiques` a isolé les cinq ports dans
`MoaMat.Domain`, et il n'existe **qu'une seule** référence au paquet `Supabase`,
dans `MoaMat.Infrastructure`. Aucun composant Razor ne connaît le backend.

**Constat 2 — le couplage réel est en SQL, et il est mince.** Tout le contrôle
d'accès vit dans PostgreSQL (`has_permission()`, ~164 policies RLS, audit
append-only par triggers, vues `security_invoker`). Ce n'est pas du Supabase,
c'est du PostgreSQL standard. Sa dépendance à Supabase se réduit à :

| Dépendance | Occurrences |
|---|---|
| `auth.uid()` | 9 appels — `rls.sql` (3), `audit.sql` (2), `roles.sql` (2), `comptes.sql` (1), `storage.sql` (1) |
| `auth.users` | FK de `utilisateur_role`, vue `compte_utilisateur`, `set_compte_actif()`, trigger `on_auth_user_created` — 5 colonnes : `id`, `email`, `banned_until`, `created_at`, `last_sign_in_at` |
| `current_setting('request.jwt.claims')` | 1 appel (`audit.sql`) |
| `custom_access_token_hook()` | confort de navigation uniquement |
| rôles PG `anon` / `authenticated` | cibles des `grant` |

**Objectif.** Rendre ce SQL exécutable tel quel sur un PostgreSQL nu, pour que
l'implémentation EF Core **hérite** du modèle de sécurité au lieu de le
réimplémenter — et le prouver par des tests pendant que Supabase reste en service.

---

## Décisions actées

| Sujet | Décision |
|---|---|
| Topologie | Client Blazor WASM conservé + API ASP.NET Core. EF Core ne s'exécute pas dans le navigateur : le tier serveur est structurellement obligatoire. |
| Identité | **ASP.NET Core Identity**, dans `MoaMat.Api`, `IdentityUser<Guid>` mappé sur la table `auth.users`. Keycloak / Authentik écartés : ils sortent l'identité de la base et transforment des FK vérifiées en projections dérivables, pour un bénéfice (SSO multi-applications, fédération d'annuaire) que le projet n'a pas. |
| Propriété du schéma | Hybride : les migrations EF possèdent `auth.users` ; `db/*.sql` reste la source de vérité pour tout le reste, en *database-first*. |
| Périmètre | ADR + EF Core + `MoaMat.Api` en parallèle, prouvés par tests conteneurisés. **Supabase reste le backend déployé**, `deploy.yml` et GitHub Pages ne bougent pas. |

### L'idée structurante : `auth.users` devient une table Identity

`MoaMatUser : IdentityUser<Guid>` est mappé sur `auth.users` avec les noms de
colonnes que le SQL existant utilise déjà :

| Propriété Identity | Colonne | Remarque |
|---|---|---|
| `Id` | `id` (`uuid`) | `IdentityUser<Guid>` → `uuid` natif Npgsql |
| `Email` | `email` | |
| `LockoutEnd` | `banned_until` | `set_compte_actif()` fonctionne sans modification : `'infinity'` désactive, Identity refuse nativement la connexion |
| `CreatedAt` (ajoutée) | `created_at` | |
| `LastSignInAt` (ajoutée) | `last_sign_in_at` | mise à jour à la connexion |
| `PasswordHash`, `SecurityStamp`, `NormalizedEmail`… | colonnes Identity | cohabitent, invisibles du SQL métier |

Ce que cela préserve, **sans toucher une ligne de `db/`** :

- `utilisateur_role.user_id references auth.users(id) on delete cascade` reste une
  vraie clé étrangère vérifiée par le moteur ;
- le trigger `on_auth_user_created` (`after insert on auth.users`) continue de
  provisionner le rôle `lecture` — il ne serait pas créable sur une vue ;
- la vue `compte_utilisateur` et `set_compte_actif()` lisent les mêmes colonnes ;
- attention : `LockoutEnabled` doit valoir `true` par défaut, sinon Identity
  ignore `banned_until`.

### Pourquoi on ne débranche pas Supabase Auth tout de suite

Question tranchée en cadrage, notée ici pour ne pas la rouvrir sans raison.

1. **Identity a besoin d'un processus serveur.** Vérifier un mot de passe, émettre
   un jeton, envoyer un e-mail de réinitialisation : c'est du code serveur.
   L'application est 100 % statique sur GitHub Pages ; le seul « serveur » du
   montage, c'est GoTrue. Poser les tables Identity dans Supabase ne suffirait
   pas, aucun processus ne les lirait.
2. **Le schéma `auth` de Supabase ne nous appartient pas.** `auth.users` est la
   table de GoTrue, avec ses migrations que Supabase rejoue. Y ajouter les
   colonnes Identity serait écrasé, et créer des tables dans `auth` est
   déconseillé par Supabase. Il faudrait les poser ailleurs, donc recibler
   `utilisateur_role.user_id` — ce qui casserait Supabase Auth au même moment.

**Porte de sortie anticipée** (à rouvrir seulement à la fin du lot 4) : héberger
`MoaMat.Api` chez un hébergeur gratuit, Supabase ramené à un PostgreSQL managé,
le client WASM restant sur Pages et parlant à l'API en CORS. Bascule franche, pas
parallèle : `roles.sql` et `comptes.sql` modifiés, propriété « aucun backend »
perdue, application en service mise en jeu. On ne débranche pas une
authentification qui fonctionne avant d'avoir la preuve que la remplaçante
fonctionne.

---

## Architecture cible

```
MoaMat.Web (WASM, navigateur)        [inchangé aujourd'hui]
    | ports MoaMat.Domain
    |-- Supabase*Repository ........ AUJOURD'HUI : PostgREST direct -> Supabase
    '-- Http*Repository ............ DEMAIN (hors périmètre)
                                          |
                                          v HTTPS/JSON + jeton Identity
                                     MoaMat.Api (ASP.NET Core)   [nouveau]
                                          |  Identity  -> auth.users  (moamat_identity)
                                          |  Ef*Repos   -> public.*   (moamat_app)
                                          |  SET moamat.user_id = <id utilisateur>
                                          v
                                     PostgreSQL + db/*.sql inchangés
                                     (RLS, audit, vues, RPC)
```

---

## Prérequis

- [ ] **Docker Desktop installé et démarré.** Vérifié le 2026-09-09 : `docker`
      n'est pas dans le PATH de la machine. Testcontainers en dépend pour tout le
      lot 1 ; sans lui, rien de la chaîne de preuve ne tourne.
- [ ] `dotnet --version` ≥ 10.0.400 (OK au 2026-09-09).
- [ ] Build de départ vert : `dotnet build MoaMat.slnx` puis
      `dotnet test MoaMat.slnx` (56 tests unitaires attendus).

> Rappel : `Directory.Build.props` active `TreatWarningsAsErrors`, `NuGetAudit`
> (`all` / `moderate`) et `EnforceCodeStyleInBuild`. Tout nouveau paquet doit
> passer l'audit, et tout nouveau fichier les analyseurs.
> Les versions NuGet vont **exclusivement** dans `Directory.Packages.props` (CPM).

---

## Lot 0 — ADR

Créer `docs/adr/`. Le dépôt n'a pas encore d'ADR ; `db/MODELE.md` et
`db/SECURITE.md` en tiennent lieu pour leurs sujets — garder le même ton
(la décision, mais surtout les options écartées et pourquoi).

- [ ] `docs/adr/0001-topologie-cible-api-plus-wasm.md` — pourquoi EF Core impose
      un tier serveur ; pourquoi le WASM est conservé ; l'inventaire de ce qui
      meurt au basculement : `BrowserSessionPersistence`, `SupabaseKeyInspector`,
      `SupabaseKeyKind`, `SupabaseSettings` + leurs 3 fichiers de tests,
      `supabase/`, `.github/workflows/supabase-keepalive.yml`, l'étape
      « anon key only » de `deploy.yml`.
- [ ] `docs/adr/0002-identite-aspnet-core-identity.md` — le choix, les
      alternatives écartées (Keycloak, Authentik, Supabase self-hosted), le
      mapping `MoaMatUser` → `auth.users`, la reprise des comptes, le sort de
      `custom_access_token_hook`. Insister sur la réversibilité : le mécanisme du
      GUC est identique quelle que soit l'origine de l'identité.
- [ ] `docs/adr/0003-securite-conservee-en-base.md` — la RLS reste la ligne de
      sécurité, l'API ne duplique pas l'autorisation, mécanisme du GUC,
      séparation des deux rôles de connexion.
- [ ] `docs/adr/0004-propriete-du-schema.md` — frontière migrations EF (`auth`)
      / SQL (`public`), critères de revisite au basculement.
- [ ] `docs/adr/0005-supabase-auth-conserve-jusqu-a-l-hebergement.md` — la
      section « Pourquoi on ne débranche pas Supabase Auth tout de suite »
      ci-dessus, avec son **critère de réouverture** : fin du lot 4, suite de
      tests d'intégration verte.
- [ ] Référencer `docs/adr/` depuis le `README.md` (section Arborescence).

---

## Lot 1 — Portabilité du SQL, prouvée par les tests

**Le lot qui porte l'essentiel de la valeur.** À faire même si tout le reste glisse.

- [ ] `db/compat/auth_local.sql` (nouveau, **jamais appliqué sur Supabase**) :
  - [ ] `auth.uid()` → `nullif(current_setting('moamat.user_id', true), '')::uuid`
  - [ ] de quoi alimenter le `current_setting('request.jwt.claims')` de
        `audit.sql` depuis un GUC `moamat.jwt_claims` posé par l'API
  - [ ] rôles `anon` et `authenticated` (cibles des `grant` existants)
  - [ ] `moamat_app` — contexte métier : DML sur `public`, **lecture seule** sur
        `auth.users`
  - [ ] `moamat_identity` — contexte Identity : DML sur `auth.users` uniquement
  - [ ] les deux rôles de connexion **non superutilisateurs et sans `BYPASSRLS`**,
        sinon les policies sont contournées et les tests faussement verts
- [ ] `db/compat/README.md` — ce fichier n'est jamais appliqué sur Supabase, et
      pourquoi.
- [ ] Nouveau projet `tests/MoaMat.IntegrationTests/` (xUnit v3, comme l'existant,
      `OutputType=Exe` ; `global.json` fait déjà le nécessaire pour le runner) :
  - [ ] `Database/DatabaseScriptRunner.cs` — application ordonnée et rejouable
  - [ ] `Database/PostgresFixture.cs` — Testcontainers PostgreSQL. **Ordre
        d'application** : migration EF Identity (crée `auth.users`) →
        `db/compat/auth_local.sql` → `db/*.sql` dans l'ordre de
        `db/SECURITE.md` §1 : `schema` → `roles` → `permissions` → `model_item`
        → `rls` → `audit` → `comptes` → `storage`.
        Sauter `initial_load.sql` (gitignoré, généré).
  - [ ] `Security/RlsTestSuiteTests.cs` — **exécute `db/tests/rls_tests.sql` tel
        quel** et échoue si une notice `FAIL` remonte. Cette suite existe déjà
        (459 lignes, `begin … rollback`) : la voir passer hors Supabase **est**
        la démonstration que le modèle de sécurité est portable.
- [ ] Paquets à ajouter dans `Directory.Packages.props` :
      `Testcontainers.PostgreSql`, `Npgsql`.

> Note d'ordonnancement : la migration Identity doit précéder `db/roles.sql`,
> qui crée la FK `utilisateur_role.user_id → auth.users(id)` et le trigger
> `on_auth_user_created`.

---

## Lot 2 — `src/MoaMat.Infrastructure.EntityFramework`

Nouveau projet, référence `MoaMat.Domain` uniquement. EF Core + Npgsql alignés
sur `10.0.x`.

**Deux `DbContext`, deux rôles de connexion** — traduction en code de la
séparation ci-dessus :

- [ ] `MoaMatIdentityDbContext` (rôle `moamat_identity`) — `MoaMatUser` mappé sur
      `auth.users` selon le tableau de colonnes. **Seul contexte doté de
      migrations EF.**
- [ ] `MoaMatDbContext` (rôle `moamat_app`) — modèle métier, **migrations
      désactivées** :
  - [ ] **Pas de TPT.** La hiérarchie de `db/model_item.sql` est de l'héritage
        par tables de classes, mais le TPT force EF à arbitrer le type par
        jointures gauches alors que la colonne `famille` porte déjà
        l'information. Mapper `item` comme une entité et les six spécialisations
        (`item_bouteille`, `item_detendeur`, `item_gilet`, `item_petit_materiel`,
        `item_materiel_didactique`, `item_piece_detachee`) en 1:1 à clé partagée
        (`HasOne().WithOne().HasForeignKey<T>(x => x.ItemId)`) — c'est déjà le
        découpage de `ItemRecord` / `ItemCylinderRecord` côté Supabase.
  - [ ] **Lectures sur les vues, écritures sur les tables**, comme l'adaptateur
        Supabase : `v_item`, `v_lieu_contenant`, `compte_utilisateur` en entités
        sans clé (`.ToView(...).HasNoKey()`), miroirs de `ItemViewRecord`
        (27 colonnes) et `LocationPathRecord`.
  - [ ] Colonnes possédées par la base (`code_club_ambigu`, `cree_le`, `maj_le`,
        `origine_*`) en lecture seule (`ValueGeneratedOnAddOrUpdate`,
        `SetAfterSaveBehavior(Throw)`) — `ItemRecord` les omet déjà aujourd'hui.
- [ ] `MoaMatUserContextInterceptor : DbConnectionInterceptor` — pose
      `moamat.user_id` et `moamat.jwt_claims` à l'ouverture de connexion.
      **Vigilance** : couvrir par un test qu'un GUC ne fuit pas d'une requête à
      l'autre via le pool Npgsql.
- [ ] `EfInventoryRepository`, `EfLocationRepository`, `EfAccountRepository`,
      `EfAuditLogRepository` — mêmes contrats, mêmes sémantiques.
      `EfAccountRepository` **appelle `set_compte_actif()` en SQL** plutôt que de
      faire l'`update` : la fonction est `SECURITY DEFINER`, revérifie les
      droits, refuse l'auto-désactivation et journalise. La réécrire en C#
      perdrait les trois.
- [ ] `EfCallGuard` — décalque de `SupabaseCallGuard` : lectures →
      `DataAccessException`, écritures → `OperationResult`.
- [ ] **Réutilisation** : extraire de `SupabaseFailureTranslator` /
      `SupabaseFailureCategory` la partie neutre (catégorie → message français)
      vers un emplacement partagé, et n'écrire côté EF que la traduction
      `PostgresException.SqlState` → catégorie. Les codes sont **déjà les mêmes**
      (`42501` refus RLS, `23505` doublon, `23503` FK) : recâblage, pas
      réécriture.
- [ ] `DependencyInjection/EntityFrameworkServiceCollectionExtensions.cs` —
      `AddMoaMatEntityFramework(...)`, symétrique de `AddMoaMatInfrastructure()`.
- [ ] Paquets : `Microsoft.EntityFrameworkCore`,
      `Npgsql.EntityFrameworkCore.PostgreSQL`,
      `Microsoft.EntityFrameworkCore.Design`,
      `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.

---

## Lot 3 — `src/MoaMat.Api`

ASP.NET Core Minimal API. **Volontairement mince** : elle ne réimplémente pas
l'autorisation, elle transporte l'identité jusqu'à la base.

- [ ] `AddIdentityApiEndpoints<MoaMatUser>()` + `MapIdentityApi<MoaMatUser>()`.
      `/login`, `/refresh`, `/forgotPassword`, `/resetPassword`, `/manage/info`
      recouvrent presque exactement les cinq méthodes de `IAuthenticationService`
      — port dessiné pour GoTrue, ce qui rend le portage ligne à ligne.
- [ ] **Neutraliser `/register`** : les comptes sont créés par un administrateur,
      comme aujourd'hui.
- [ ] `GET /api/moi` — e-mail + rôle applicatif lu dans `utilisateur_role`.
      Remplaçant de `custom_access_token_hook` : les jetons `MapIdentityApi` sont
      opaques, le client ne peut pas y lire un claim. C'est même préférable, le
      rôle est lu à la source plutôt que figé dans un jeton d'une heure.
- [ ] Groupes d'endpoints calqués sur les ports : `/api/inventaire`, `/api/lieux`,
      `/api/comptes`, `/api/journal-audit`. `RequireAuthorization()` seul —
      **pas de policy par rôle côté API**.
- [ ] Sémantiques préservées : lecture non habilitée → **200 avec liste vide**
      (la RLS filtre, comme aujourd'hui) ; écriture refusée → `42501` → `403`
      porteur du message déjà traduit par le `FailureTranslator`.
- [ ] `IEmailSender` pour les e-mails de réinitialisation ; en développement, un
      expéditeur qui journalise.
- [ ] `/health`, OpenAPI.
- [ ] **`MoaMat.Api` n'est référencé par personne** : ni `MoaMat.Web`, ni le
      déploiement. Projet parallèle validé par ses seuls tests.

---

## Lot 4 — Tests de contrat de ports

Dans `tests/MoaMat.IntegrationTests`, une classe **abstraite** par port, portant
les invariants **documentés dans les interfaces** :

- [ ] `InventoryRepositoryContract`, `LocationRepositoryContract`,
      `AccountRepositoryContract`, `AuditLogRepositoryContract`
- [ ] Invariants à couvrir : idempotence des écritures rejouées, liste vide
      plutôt qu'erreur pour un appelant non habilité, bornage de `limit` à
      `[1, 500]`, désactivation logique jamais destructrice, refus de
      l'auto-désactivation.
- [ ] Aujourd'hui : une implémentation concrète par contrat, sur EF.
      Demain : la même classe abstraite dérivée pour l'adaptateur HTTP — et le
      jour où on la fait passer, la bascule est démontrée, pas espérée.

**C'est ici que se rouvre la question de l'ADR 0005** (héberger l'API et quitter
Supabase Auth plus tôt), une fois la suite verte.

---

## Lot 5 — CI

**Ne pas toucher `deploy.yml`** : GitHub Pages doit continuer à déployer, et les
tests conteneurisés n'ont rien à faire dans le chemin de publication.

- [ ] Nouveau `.github/workflows/ci.yml` — sur PR et push : `dotnet build
      MoaMat.slnx` puis `dotnet test MoaMat.slnx` (unitaires + intégration ;
      Docker est disponible sur `ubuntu-latest`).
- [ ] Ajouter les nouveaux projets à `MoaMat.slnx`.

---

## Lot 6 — Dettes de portabilité, à payer tant que c'est gratuit

- [ ] `src/MoaMat.Web/MoaMat.Web.csproj` : ajouter la `PackageReference`
      explicite à `Supabase`. Aujourd'hui
      `Authentication/BrowserSessionPersistence.cs` et
      `DependencyInjection/WebServiceCollectionExtensions.cs` compilent contre
      `Supabase.Gotrue` **par transitivité**. La dépendance existe, autant
      qu'elle soit visible : le jour de la bascule, la ligne à supprimer saute
      aux yeux.
- [ ] **À arbitrer** — renommer `MoaMat.Infrastructure` →
      `MoaMat.Infrastructure.Supabase`, pour que la symétrie avec
      `.EntityFramework` soit évidente. À faire maintenant ou jamais : le
      refactor est encore non commité, le renommage est mécanique aujourd'hui et
      coûteux dans six mois. C'est la seule proposition qui touche du code
      existant qui fonctionne.

---

## Hors périmètre (assumé)

- **Aucun adaptateur HTTP côté client** : sans serveur déployé, rien ne le
  validerait. Le lot 4 prépare sa preuve.
- **Rien n'est supprimé côté Supabase** : ni le paquet, ni `supabase/`, ni le
  keepalive, ni les étapes de `deploy.yml`.
- **Aucune reprise de mots de passe** : mécanisme documenté dans l'ADR 0002,
  implémenté au moment du dump réel.
- **`nominate-super-admin`** reste une Edge Function tant que Supabase tourne.
- **Aucun hébergement de `MoaMat.Api`**, donc aucun abandon de Supabase Auth.

---

## Vérification

1. [ ] `dotnet build MoaMat.slnx` — `TreatWarningsAsErrors` et `NuGetAudit`
       actifs : les nouveaux paquets doivent passer l'audit, les nouveaux
       fichiers les analyseurs.
2. [ ] `dotnet test MoaMat.slnx` — les 56 tests unitaires existants restent
       verts. Ils testent le domaine : **ils ne doivent pas bouger d'une ligne**.
3. [ ] **Le test qui compte** : `db/tests/rls_tests.sql` passe sans notice `FAIL`
       sur le PostgreSQL conteneurisé, avec `auth.users` produite par Identity.
       Si c'est vert, le modèle de sécurité est portable et la migration n'a plus
       de zone d'ombre.
4. [ ] Contrats de ports verts sur l'implémentation EF.
5. [ ] Test de fuite de GUC : deux requêtes successives sur la même connexion
       poolée, avec des identités différentes, ne doivent pas voir les données
       l'une de l'autre.
6. [ ] Bout en bout de la désactivation : `set_compte_actif(u, false)` écrit
       `banned_until = 'infinity'`, et Identity refuse ensuite `/login` pour ce
       compte. Démonstration que l'écran `/comptes` survit tel quel.
7. [ ] Manuel : lancer l'API contre la base conteneurisée, se connecter avec un
       compte par rôle (`lecture` / `gestion` / `admin` / `super-admin`) et
       vérifier qu'un `lecture` reçoit `200` et **zéro ligne** sur `/api/comptes`
       et `/api/journal-audit`, et `403` sur une écriture d'item.
8. [ ] **Non-régression** : `dotnet run --project src/MoaMat.Web` fonctionne
       toujours contre Supabase, et `deploy.yml` publie toujours sur GitHub Pages.

---

## Questions encore ouvertes

- **Reprise des mots de passe** : `IPasswordHasher<MoaMatUser>` custom validant
  le bcrypt GoTrue puis re-hachant en PBKDF2 à la première connexion, ou
  réinitialisation forcée. Se décide au moment du dump. Les UUID, eux, sont
  conservés : `utilisateur_role` et `audit_log.actor_id` n'ont rien à migrer.
- **Storage** : trois buckets définis dans `db/storage.sql`, aucun code C# ne les
  utilise encore. À trancher au basculement (fichier serveur ou S3/MinIO).
- **MFA** : Identity le fournit, l'UI est à écrire. Hors sujet aujourd'hui, mais
  c'est le point où Keycloak reprendrait de l'avance si le besoin apparaissait.
- **Deux dettes déjà signalées dans le `README.md`**, traitables une fois EF Core
  en place : les mises à jour de `public.item` sont en *last-write-wins* sans
  jeton de version (EF donne `xmin` comme jeton de concurrence pour presque
  rien), et l'inventaire borne ses requêtes sans pagination visible.
