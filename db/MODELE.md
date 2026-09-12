# MoaMat — Modèle métier « Item »

Ce document décrit la couche **opérationnelle normalisée** posée par
[`db/model_item.sql`](model_item.sql) et alimentée par
[`db/transform_item.sql`](transform_item.sql), et **justifie la stratégie
d'héritage** retenue pour les spécialisations (bouteille, détendeur, gilet, …).

Il répond à la fusion des tickets *modèle Item*, *hiérarchie de lieux*,
*codes ambigus* et *couche d'accès aux données*.

---

## 1. Deux étages : miroir Access (staging) + modèle Item (opérationnel)

[`db/schema.sql`](schema.sql) est un **miroir fidèle de l'export Access** : une
table plate par famille, presque tout en `text`, l'`id` = l'id Access réinjecté,
aucune hiérarchie de lieux, `code_club` verbatim avec ses doublons. Ce miroir
**ne change pas** : c'est la zone d'atterrissage, rechargée par
[`db/initial_load.sql`](initial_load.sql) (lui-même régénéré par
`tools/Generate-InitialLoad.ps1`).

Le modèle Item est construit **par-dessus**, sans jamais modifier le miroir :

```
Access CSV ─► [schema.sql + initial_load.sql]      staging (miroir, inchangé)
                       │
                       ▼
              [model_item.sql]      DDL cible : item + item_* + lieux + statuts
                       │
                       ▼
              [transform_item.sql]  ETL idempotent : miroir ─► cible
                       │            + typage fin + journal des rejets
                       ▼
   application WASM   (lecture : vue v_item ; écriture : table item)
```

### Ordre d'exécution des scripts `db/`

| # | Script | Rôle |
|---|--------|------|
| 1 | `schema.sql`         | miroir Access (28 tables) |
| 2 | `initial_load.sql`   | données reprises (généré) |
| 3 | `roles.sql`          | rôles, `utilisateur_role`, fonctions |
| 4 | `permissions.sql`    | catalogue + matrice (domaine **`item`** ajouté) |
| 5 | **`model_item.sql`** | **DDL du modèle Item (RLS activée sans policy)** |
| 6 | **`transform_item.sql`** | **reprise miroir → Item (rejouable)** |
| 7 | `rls.sql`            | policies RLS (tables `item*` / `lieu*` / `ref_statut` incluses) |
| 8 | `audit.sql`          | journal + trigger statut terminal sur `item` |
| 9 | **`item_etat.sql`**  | **machine à états : historique décisionnaire, verrouillage terminal, disponibilité calculée — voir `MODELE.md` §9** |
| 10 | **`item_bouteille.sql`** | **moteur métier Bouteilles : référentiels réglementaire/tarifaire datés, échéances à deux compteurs, bascule automatique — voir `MODELE.md` §10** |
| 11 | `comptes.sql`       | écran /comptes |
| 12 | `storage.sql`       | buckets Storage |

### Transition / repli

Tant que l'application n'est pas en service, `transform_item.sql` fait
`truncate … restart identity` puis recharge : `item.id` est réattribué à chaque
exécution, exactement comme `initial_load.sql`. Le couple
`(origine_table, origine_id)` est la clé stable de correspondance vers le miroir.
Une fois l'appli en production et `item.id` référencé ailleurs, le transform
passera en `upsert` sur `(origine_table, origine_id)` — voir §6.

Contrôle de parité après reprise : `select count(*) from item` vs la somme des
lignes des familles du miroir, plus `select count(*) from item_reject`
(0 donnée perdue : toute valeur non convertie y figure verbatim).

---

## 2. Entité de base `public.item`

| Colonne | Type | Notes |
|---|---|---|
| `id` | `bigint GENERATED ALWAYS AS IDENTITY` | **vraie clé technique** : jamais fournie par le client, jamais réutilisée, non modifiable |
| `code_club` | `text` | identité « club » **affichée**, distincte de `id` ; jamais renumérotée automatiquement (Q1.2) |
| `famille` | `text` (CHECK) | `bouteille` \| `detendeur` \| `gilet` \| `petit_materiel` \| `materiel_didactique` \| `piece_detachee` |
| `num_serie` | `text` | n° de série fabricant |
| `marque`, `modele` | `text` | |
| `date_acquisition` | `date` | |
| `prix_eur` | `numeric(12,2)` CHECK ≥ 0 | |
| `statut_code` | `text` → `ref_statut` | catalogue de statuts (remplace les drapeaux booléens du miroir) |
| `lieu_contenant_id` | `bigint` → `lieu_contenant` | niveau fin de la hiérarchie de lieux ; `NULL` = non localisé |
| `destination` | `text` | |
| `remarque` | `text` | |
| `date_echeance` | `date` | échéance de contrôle / requalification la plus proche (support du filtre) |
| `actif` | `boolean` (def. `true`) | **désactivation logique** — la couche métier ne fait jamais de `DELETE` |
| `code_club_ambigu` | `boolean` | **recalculé**, jamais saisi — voir §5 |
| `origine_table`, `origine_id` | `text`, `bigint` | traçabilité de reprise ; `UNIQUE` ensemble ; `NULL` pour un item créé dans l'appli |
| `cree_le`, `maj_le` | `timestamptz` | `maj_le` maintenu par trigger |

La **lecture** applicative passe par la vue `public.v_item` (statut, chemin de
lieu et drapeaux d'ambiguïté déjà résolus, `security_invoker` → la RLS de `item`
s'applique). L'**écriture** vise la table `public.item`.

---

## 3. Stratégie d'héritage : **Class-Table Inheritance** (table par type)

`public.item` porte le tronc commun ; **une table fille 1:1 par famille**
(`item_bouteille`, `item_detendeur`, `item_gilet`, `item_petit_materiel`,
`item_materiel_didactique`, `item_piece_detachee`), dont la clé primaire **est**
la clé étrangère vers `item(id)` (`on delete cascade`).

### Options examinées

| Approche | Décision | Raison |
|---|---|---|
| **Table unique + colonne discriminante** (une table géante `item` avec toutes les colonnes de toutes les familles) | ❌ rejetée | Les familles sont très divergentes : `item_detendeur` a ~17 colonnes propres (1ᵉʳ / 2ᵉ étage, octopus, inflateur, manomètre, n° de série par étage…), `item_bouteille` a volume / pression / tare / capacité / filetage, `item_piece_detachee` n'a ni n° de série ni `code_club`. Une table unique = des dizaines de colonnes `NULL` à 80 %, des `CHECK` conditionnels par famille ingérables, et aucune contrainte `NOT NULL` réelle possible sur le spécifique. |
| **Table par type _concrète_** (une table autonome par famille, sans tronc `item` partagé) | ❌ rejetée | Perd la **clé technique unique inter-familles** demandée, et interdit les requêtes transverses (inventaire complet, recherche par lieu / statut / échéance toutes familles confondues) sans `UNION` systématique. Ne résout pas la duplication des colonnes communes. |
| **Class-Table Inheritance** (`item` + fille 1:1) | ✅ **retenue** | 1) Une seule séquence d'identité → `id` **jamais réutilisé** trivialement garanti. 2) Le tronc commun est défini une fois, `NOT NULL` / `CHECK` s'appliquent réellement. 3) Chaque fille reste **1:1 avec un domaine de permission** existant (`bouteille`, `detendeur`, …) — la RLS et l'audit se raccordent sans refonte (ici : domaine unifié `item`). 4) Les tables enfants du miroir (`bouteille_requalification.bouteille_id`, `detendeur_intervention.detendeur_id`) se rebrancheront proprement sur `item_bouteille.item_id` / `item_detendeur.item_id`. 5) Volume ~1 400 lignes : le `JOIN` 1:1 est gratuit. |
| Héritage natif PostgreSQL (`INHERITS`) | ❌ écarté | Les contraintes d'unicité et les FK ne se propagent pas aux tables filles ; incompatible avec PostgREST / Supabase de façon prévisible. |

### Conséquences pour l'application

- Lecture liste : `v_item` (aplati, une ligne par item).
- Lecture détail d'une bouteille : `item` + `item_bouteille` (join sur `item_id`).
- Création : `insert into item (…) returning id`, puis `insert into item_<famille> (item_id, …)`.
- `famille` est **immuable** en pratique (un item ne change pas de type) — non
  contraint techniquement pour l'instant, à verrouiller si besoin par trigger.

---

## 4. Hiérarchie de lieux : Section → Local → Contenant

Trois tables (`lieu_section`, `lieu_local`, `lieu_contenant`), chaînées par FK
`on delete cascade`. `item.lieu_contenant_id` pointe le niveau fin (nullable :
`NULL` = localisation inconnue). Vue `v_lieu_contenant` = chemin complet
(`Section › Local › Contenant`).

**Administration** : écran `/lieux` (rôle `admin`+), ou directement en SQL. Le
domaine de permission est **`referentiel`** (lecture : tous ; écriture :
`admin`+) — les lieux sont un référentiel comme les autres.

**Aucune logique d'autorisation ne s'appuie sur la section** (Q18.3). Ni
`lieu_section`, ni aucune de ces tables n'apparaît dans une clause de policy RLS
autrement que via `referentiel.*`. La section est un axe de **rangement**, pas un
périmètre de droits.

### Reprise des lieux depuis le texte libre Access

`transform_item.sql` :

1. sème `lieu_section` depuis `ref_site.libelle` + les colonnes `section` texte
   du miroir ;
2. remplit `lieu_mapping (source_champ, source_valeur, contenant_id)` avec
   **toutes les valeurs de lieu en texte libre rencontrées**, `contenant_id`
   laissé `NULL` ;
3. résout `item.lieu_contenant_id` via `lieu_mapping` quand la correspondance
   existe.

Les lignes de `lieu_mapping` à `contenant_id NULL` sont la **liste de travail
d'arbitrage** : un admin crée les `lieu_local` / `lieu_contenant` manquants
(écran `/lieux`), renseigne `contenant_id`, puis on **rejoue**
`transform_item.sql`. Rien n'est deviné : à la première passe, la plupart des
items sont `lieu_contenant_id = NULL` (assumé et visible).

---

## 5. Détection des codes club ambigus — on signale, on ne corrige pas (Q1.2)

**Aucune renumérotation.** La valeur `code_club` d'origine est conservée telle
quelle. Seul un **drapeau** est calculé.

Un `code_club` est marqué ambigu (`item.code_club_ambigu = true`) si :

- **doublon** : la même valeur (casse et espaces ignorés) porte sur > 1 item ; ou
- **non structurant** : il ne suit pas le motif attendu
  `^[A-Za-z]{1,4}[[:space:]./-]?[0-9]{1,5}$`
  (1 à 4 lettres, séparateur optionnel, 1 à 5 chiffres — ex. `B123`, `DET-45`,
  `MD 007`). Motif **volontairement large et ajustable** :
  `public.item_code_est_structurant(text)` dans `model_item.sql`.

Mise en œuvre :

- `public.v_code_club_ambigu` — recalcul **live** (`est_duplique`,
  `est_non_structurant`), pour un éventuel écran d'audit des codes ;
- `public.item.code_club_ambigu` — colonne **matérialisée** (pour filtrer / trier
  efficacement), recalculée par `public.item_refresh_code_ambigu()` et maintenue
  par un trigger `AFTER INSERT/UPDATE OF code_club/DELETE` sur `item` (garde
  `pg_trigger_depth()` contre la ré-entrée) ;
- l'UI (`/inventaire`) affiche un **badge « code ambigu »** sur les lignes
  concernées ; `v_item` expose aussi `code_club_duplique` /
  `code_club_non_structurant` pour préciser la raison.

---

## 6. Rejets de conversion (constats A11/A22)

Les colonnes numériques stockées en `text` dans le miroir (« mesures sales » :
`volume_nominal_l` = `"12 Li"`, `pression_service_bar`, `tare_kg`,
`capacite_reelle_l`) sont converties par `public.moamat_reprise_num(text)` /
`moamat_reprise_int(text)` (extraction du premier nombre, virgule décimale
tolérée). **Toute valeur non NULL non convertible** est consignée verbatim dans
`public.item_reject (origine_table, origine_id, colonne, valeur_brute, raison)`.

C'est la garantie « on ne perd rien silencieusement » : `item_reject` est la
liste de travail d'arbitrage, consultable via `item.read` (aucune écriture
cliente).

---

## 7. Sécurité — rappel

Les services WASM (`ItemService`, `LieuService`) **ne sont pas** la ligne de
sécurité. La sécurité effective est portée par les **policies RLS**
(`db/rls.sql`) : `item*` → permission `item.*` ; `lieu*` / `ref_statut` →
`referentiel.*` ; `item_reject` → `item.read` en lecture seule. Les services ne
font que **structurer les appels** PostgREST (filtres, projections, désactivation
logique).

---

## 9. Machine à états / disponibilité calculée / historique décisionnaire ([`db/item_etat.sql`](item_etat.sql))

Fusion des tickets *machine à états*, *calcul de disponibilité* et
*historique décisionnaire*. Principe directeur (A16) : **le statut — et donc
la disponibilité — est calculé, jamais saisi**. Aucune case à cocher manuelle
de disponibilité n'existe nulle part dans le modèle.

### 9.1 Catalogue de statuts (`ref_statut`, posé par `model_item.sql`)

`en_stock`, `prete`, `en_controle`, `en_attente_controle`, `en_maintenance`,
`hors_validite` (non terminaux), puis **trois statuts terminaux** :
`retire_du_service`, `perdu`, `vole`.

`retire_du_service` **fusionne** les trois anciens statuts « déclassé / écarté
/ rebuté » (Q7bis.1) : un seul enregistrement par objet retiré, pas de tables
dupliquées. `perdu` et `vole` restent distincts (assurance, plainte).

### 9.2 Transition de statut : comment l'appli l'appelle

Il n'y a pas de RPC dédiée : le client envoie, dans le **même** `UPDATE`
PostgREST que `statut_code`, les colonnes transitoires de `public.item` :
`statut_motif` (obligatoire), `statut_date_effet` (obligatoire),
`statut_piece_jointe_url` (obligatoire pour `perdu` / `vole` — chemin
Supabase Storage), `statut_autorite` (obligatoire pour une transition VERS un
statut terminal — `organisme_controle` | `ca` | `gestionnaire_materiel`).

Le trigger `public.tg_item_valider_transition_statut` (BEFORE UPDATE OF
`statut_code`) valide ces règles, écrit une ligne dans
`public.item_transition` (historique append-only, comme `audit_log`), puis
remet les colonnes transitoires à `NULL` : ce ne sont pas des champs d'état
durable, seulement le véhicule de la transition demandée.

### 9.3 Irréversibilité des statuts terminaux (R7bis.1)

Imposée **à la fois** :
- par le **trigger** ci-dessus (refuse tout changement depuis un statut
  terminal sans la permission `status.terminal.override`) ;
- par la **policy RLS** `item_upd` (remplace la policy générique posée par
  `db/rls.sql` pour la seule table `item`) : `USING` exige en plus
  `status.terminal.override` dès lors que la ligne ciblée est déjà dans un
  statut terminal. Entrer dans un statut terminal reste une transition
  normale (rôle `gestion`+) ; seul le retour — ou toute autre modification
  après coup — est verrouillé.

Le passage par un Super-admin/Admin est journalisé deux fois : dans
`audit_log` (trigger générique déjà existant sur `item.statut_code`,
`db/audit.sql`) et dans `item_transition` (autorité décisionnaire, motif,
pièce jointe).

### 9.4 Disponibilité calculée (`public.item_est_disponible()`)

Combine : `actif` × `statut_code = 'en_stock'` × échéance non dépassée × pas
de prêt ouvert. Volontairement **défensif** plutôt que redondant avec le
statut : les autres statuts (`en_maintenance`, `en_controle`,
`en_attente_controle`, statuts terminaux…) le rendent déjà indisponible, mais
la fonction revérifie aussi l'échéance et le prêt pour couvrir une dérive de
données (statut resté « en stock » alors que l'échéance est dépassée ou
qu'un prêt reste ouvert) — précisément le type de contradiction relevé par
les constats d'audit **A15 / A16 / A20** sur les données existantes (voir
[`db/tests/item_etat_tests.sql`](tests/item_etat_tests.sql)).

Le prêt en cours est détecté via `public.item_a_pret_en_cours()`, qui **ponte**
vers le miroir `public.pret` (par `code_club` — aucun module « prêt » n'est
encore posé sur le modèle Item, cf. §1 : le miroir ne change pas).

Exposée uniquement en lecture, via `public.v_item.disponible` — jamais une
colonne éditable.

---

## 10. Moteur métier Bouteilles ([`db/item_bouteille.sql`](item_bouteille.sql))

Fusion des tickets *modèle bouteille*, *référentiel réglementaire*,
*référentiel tarifaire* et *moteur d'échéances*. Les valeurs initiales ne sont
pas inventées : le miroir Access porte déjà les **6 profils** réglementaires
réels (`public.ref_regle_requalification` : Plongée ACIER, Plongée ALU,
Plongée Carbonne, Deco O², O² Secourisme, Tampons) et les tarifs Apragaz
2023-2026 (`public.ref_tarif_requalification`). Ce fichier construit la
couche normalisée et administrable par-dessus, sur le principe déjà posé par
`model_item.sql` — le miroir ne change pas.

### 10.1 Classification (`public.item_bouteille`)

Deux colonnes distinguent les 6 profils réglementaires : `famille`
(`plongee` / `deco_o2` / `o2_secourisme` / `bloc_tampon`, le champ métier du
ticket) et `matiere` (`acier` / `alu` / `carbone`, pertinente seulement pour
`famille = 'plongee'` : les trois autres familles ont une périodicité propre,
indépendante de la matière). `etat_robinetterie` est un champ texte simple —
**volontairement pas une entité séparée** (Q2.1). Le résolveur
`public.bouteille_type_referentiel(famille, matiere)` retombe sur l'un des 6
codes réglementaires (`plongee_acier`, `plongee_alu`, `plongee_carbone`,
`deco_o2`, `o2_secourisme`, `bloc_tampon`) ; il est reproduit en C# pur
(`MoaMat.Domain.Cylinders.CylinderReferenceType`) pour rester testable hors
base de données.

### 10.2 Référentiels administrables et DATÉS

`public.ref_periodicite_bouteille` (périodicité en mois par type × contrôle)
et `public.ref_tarif_apragaz` (RR / hydraulique huile / hydraulique eau)
suivent tous les deux le principe d'historisation **append-only** déjà
utilisé pour `public.item_transition` (§9.2) : « modifier » une valeur, c'est
insérer une nouvelle ligne avec un `date_effet` plus récent — rien n'est
jamais écrasé, et des triggers `BEFORE UPDATE`/`BEFORE DELETE` refusent toute
modification, y compris pour un rôle admin. La lecture (`referentiel.read`)
et l'écriture (`referentiel.create`, admin+) réutilisent le même domaine de
permission que `ref_statut` / `lieu_*`.

Les tarifs hydraulique huile / hydraulique eau restent deux lignes
**distinctes** à dessein : l'écart entre les deux (de l'ordre de 13 à 15 €
selon les années, visible dans les valeurs reprises de
`ref_tarif_requalification`) est une donnée réelle du référentiel Apragaz, pas
une erreur de saisie — les fusionner ou les moyenner ferait disparaître cette
distinction.

### 10.3 Moteur à deux compteurs indépendants

`public.item_bouteille` porte deux compteurs, alimentés indépendamment :
`date_dernier_controle_optique` et `date_dernier_controle_hydraulique`.
`public.bouteille_echeance(type, controle, dernier_controle)` calcule
l'échéance d'**un seul** compteur à la fois (dernier contrôle + périodicité en
vigueur à cette date) — il n'y a aucune alternance codée en dur entre optique
et hydraulique : l'absence de ligne réglementaire pour un couple donne
simplement une échéance `NULL` (ex. carbone n'a pas de contrôle optique).
`public.v_item_bouteille` expose les deux échéances plus `echeance_min` (la
plus proche des deux, `NULL` seulement si les deux le sont).

Un trigger `AFTER INSERT/UPDATE` sur `item_bouteille` recopie `echeance_min`
dans `public.item.date_echeance`, pour que `public.item_est_disponible()`
(§9.4) et les filtres existants « par échéance » restent corrects sans
dupliquer le calcul côté client.

### 10.4 Bascule automatique en « Hors validité » (R2.3)

`public.item_bouteille_appliquer_hors_validite()` fait passer au statut
`hors_validite` (déjà présent dans `public.ref_statut`, non terminal) toute
bouteille active dont `echeance_min` est dépassée et dont le statut est
`en_stock`, `en_attente_controle` ou `prete`. **Volontairement exclues** :
`en_maintenance` et `en_controle` — une bouteille déjà prise en charge par un
workflow actif garde ce statut même si son ancienne échéance est dépassée, la
bascule automatique ne l'écrase pas ; et les statuts déjà `hors_validite` ou
terminaux, qui ne sont de toute façon pas dans la liste blanche. Ce passage se
fait via un simple `UPDATE ... SET statut_code = 'hors_validite'` sur
`public.item` : il traverse donc le trigger existant
`public.tg_item_valider_transition_statut` (§9.2), qui exige motif et date
d'effet (fournis par la fonction) et journalise la transition dans
`item_transition`/`audit_log` — **sans dupliquer la logique de transition**.
Planifiée quotidiennement via `pg_cron` quand l'extension est disponible
(no-op sinon, pour rester rejouable en local/CI) ; peut aussi être appelée
directement pour un rattrapage manuel ou en test.

La fonction est `SECURITY DEFINER` et contourne entièrement la policy RLS
`item_upd` (aucune vérification de `item.update`) : son `EXECUTE` est donc
**révoqué de `public`/`authenticated`/`anon`** et réservé à `service_role`
(rattrapage manuel côté serveur) — `pg_cron` l'invoque de toute façon en tant
que propriétaire de la fonction, qui a toujours le droit de l'exécuter. Même
garde que `public.custom_access_token_hook` (§4, `db/roles.sql`) : sans ce
`revoke`, PostgreSQL accorde `EXECUTE` à `PUBLIC` par défaut à la création
d'une fonction, ce qui aurait permis à n'importe quel compte authentifié de
forcer le statut de n'importe quelle bouteille.

Tests : [`db/tests/item_bouteille_tests.sql`](tests/item_bouteille_tests.sql).
