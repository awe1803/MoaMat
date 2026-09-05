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
dotnet restore MoaMat.slnx
dotnet build   MoaMat.slnx
dotnet run --project src/MoaMat.Web
```

La configuration Supabase est dans
[`src/MoaMat.Web/wwwroot/appsettings.json`](src/MoaMat.Web/wwwroot/appsettings.json)
(`Supabase:Url` + `Supabase:AnonKey`). La clé est une clé *publishable* (anon),
publique par nature dans une app WebAssembly : le contrôle d'accès réel repose sur
les policies **RLS** définies dans `db/schema.sql`.

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
