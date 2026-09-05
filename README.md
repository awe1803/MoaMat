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
db/schema.sql           Schéma PostgreSQL (28 tables + RLS)
db/initial_load.sql     Reprise des données Access (généré)
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

1. [`db/schema.sql`](db/schema.sql) — crée les 28 tables, les index et les policies
   RLS permissives de démarrage.
2. [`db/initial_load.sql`](db/initial_load.sql) — charge les données reprises de
   l'ancienne base Access. Ré-exécutable (commence par
   `truncate ... restart identity cascade`).

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
[`Program.cs`](src/MoaMat.Web/Program.cs) mais n'est ni encapsulé ni consommé ;
aucun modèle n'existe ; l'UI est encore le gabarit Blazor par défaut
(Home / Counter / Weather). Cette mise en couches est liée à la normalisation
métier décrite ci-dessus et sera traitée séparément.
