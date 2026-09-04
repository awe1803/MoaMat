# Plan de reprise des données — Base Access « Bouteilles » → Nouvelle application
## Royal Moana — Gestion matériel

**Version :** 1.0
**Source connue :** export PDF « Identification et historique des Requalifications » du 01/09/2026 — 64 fiches bouteilles, ~470 lignes d'historique, total 1 830,82 €
**Source réelle à obtenir :** base Microsoft Access (`.accdb` ou `.mdb`) — *non encore fournie*

---

## 1. Principes directeurs

Cinq règles qui conditionnent tout le reste :

1. **On ne migre jamais depuis le PDF.** Le PDF est un état de restitution : il a perdu les clés, les relations et probablement des champs non imprimés. Il sert uniquement de **référence de contrôle** pour vérifier que la migration est fidèle.
2. **On ne nettoie pas dans Access.** La base source reste figée et intacte. Tout le nettoyage se fait dans une couche intermédiaire (staging), reproductible et rejouable.
3. **La migration est rejouable à volonté.** On l'exécutera 5 à 10 fois avant la bonne. Un import manuel « une fois pour toutes » est à proscrire.
4. **Rien n'est jeté.** Chaque enregistrement importé conserve un lien vers sa donnée d'origine (`SourceTable`, `SourceId`, `SourceRaw`). En cas de doute six mois plus tard, on doit pouvoir remonter à la ligne Access d'origine.
5. **Les arbitrages métier sont faits par le responsable matériel, pas par le développeur.** Décider si une bouteille rouillée retrouvée dans une cave est utilisable n'est pas une décision informatique.

---

## 2. Étape 0 — Récupérer et auditer la source *(bloquant)*

### 2.1 À demander à Michel Bastin

| Élément | Pourquoi |
|---|---|
| Le fichier `.accdb` / `.mdb` complet | Source de vérité |
| Toutes ses versions/copies (`_old`, `_sauvegarde`, sur d'autres PC) | Il y a souvent plusieurs bases divergentes |
| Le mot de passe de base éventuel | Certaines bases Access sont protégées |
| Les fichiers annexes : Excel de suivi, carnet de prêts, classeur de factures | Sources parallèles fréquentes |
| Les certificats papier/PDF d'Apragaz | À rattacher aux campagnes |
| La version d'Access utilisée | Détermine le format et les outils d'extraction |

**Question à poser explicitement :** *« Y a-t-il des informations que vous gardez ailleurs que dans la base — un cahier, un tableur, ou de tête ? »* La réponse est presque toujours oui, et c'est là que se cachent les surprises.

### 2.2 Audit technique de la base

À produire avant toute décision :

- **Liste des tables** avec nombre d'enregistrements
- **Liste des champs** par table : nom, type, taille, valeurs nulles, valeurs distinctes
- **Relations et clés** déclarées (souvent absentes dans les bases Access artisanales)
- **Requêtes et états** : l'état imprimé du 01/09/2026 contient de la logique métier (calcul de l'âge, du total, du tri) qu'il faut comprendre
- **Formulaires** : ils révèlent les règles de saisie et les listes de valeurs
- **Code VBA éventuel** : peut contenir les règles de calcul d'échéance

**Outils :**
- Sous Windows : Access lui-même, ou un export via ODBC
- Sous Linux/macOS : `mdb-tools` (`mdb-tables`, `mdb-schema`, `mdb-export`)
- En .NET : le pilote ACE OLEDB (attention : nécessite la version 32/64 bits correspondante)

**Recommandation d'export :** `mdb-export -D "%Y-%m-%d" -b strip` vers CSV **UTF-8**. Attention à l'encodage : les bases Access francophones sont souvent en Windows-1252, et un mauvais décodage transforme « Rééprouvée » en caractères illisibles.

### 2.3 Structure présumée (à confirmer)

D'après la mise en page de l'état, la base contient au minimum :

```
T_Bouteilles          (1 ligne par bouteille — 64)
   └─< T_Requalifications  (n lignes par bouteille — ~470)
```

Plus, probablement, quelques tables de référence (marques, lieux, types de réépreuve) ou — plus vraisemblablement vu les incohérences d'orthographe — **des champs texte libres**.

---

## 3. Modèle de données cible

### 3.1 Schéma proposé

```
Fabricant ──┐
Lieu ───────┼──< Bouteille >──┬──< EvenementRequalification >──┐
TypeGaz ────┘        │        │                                │
                     │        └──< Composant (robinetterie)    │
                     │                                          │
                     └──< Document (photo, certificat)          │
                                                                │
                                     Campagne >─────────────────┘
                                        │
                                        └──> Prestataire
```

### 3.2 Table `Bouteille` (cible)

| Champ | Type | Origine |
|---|---|---|
| `Id` | GUID / int | généré |
| `CodeClub` | string, unique | ← `Ident_Site` normalisé (voir §5.6) |
| `NumeroPeint` | string | ← `N° identbout` |
| `NumeroSerie` | string, unique | ← `Identification` |
| `FabricantId` | FK | ← `Marque` normalisée |
| `TypeBouteille` | enum (Plongee / DecompressionO2) | ← `Type de bouteille` |
| `VolumeLitres` | decimal | ← `Volume` |
| `PressionServiceBar` | int | ← `PrSERVICE` (contrôle §5.5) |
| `TareKg` | decimal? | ← `Tare` |
| `Filetage` | string | ← `Filet` |
| `DoubleSortie` | bool | ← `Double sortie` (jamais renseigné) |
| `AvecFilet` / `AvecSangles` | bool | ← `Sangles` |
| `Destination` | enum (Adulte / Enfant / Indetermine) | ← `Destination` |
| `LieuId` | FK | ← `Lieu` |
| `LocalPhysique` | string / FK | ← `Local Phys` |
| `DateMiseEnService` | date? | ← événement `New` |
| `DateMiseEnServiceEstimee` | bool | true si date conventionnelle (§5.3) |
| `Statut` | enum | **calculé** (§6.1) |
| `DateProchaineRequalif` | date? | ← dernier événement |
| `TypeProchaineRequalif` | enum (R / RR) | ← dernier événement |
| `SourceLegacyId` | string | traçabilité |
| `NecessiteArbitrage` | bool + `MotifArbitrage` | anomalies non tranchées |

**Champs abandonnés :**
- `Age` → recalculé dynamiquement depuis `DateMiseEnService`, jamais stocké
- `Capacité` → fusionné avec `Volume` après arbitrage (§5.4)

### 3.3 Table `EvenementRequalification`

| Champ | Origine |
|---|---|
| `Id`, `BouteilleId` | |
| `Date` | ← `Date Réé` |
| `TypeEvenement` | enum : MiseEnService, Hydraulique, Optique, Ecartement, Rebut, Constat |
| `Resultat` | enum : Conforme, Refus, NonEffectuee, Manquante, Inconnu — **déduit des remarques** (§5.7) |
| `Cout` | ← `Cout` |
| `NumeroCertificat` | ← `CERTIFICAT N°` (extraction, §5.8) |
| `NumeroOrdreTravail` | extrait de la remarque (« Ordre de travail 7462 ») |
| `CampagneId` | FK — **déduit** (§6.2) |
| `DateEcheanceSuivante` | ← `Réé Suivante` |
| `TypeEcheanceSuivante` | ← colonne R/RR |
| `Remarque` | ← `Remarque Réépreuve` nettoyée |
| `DateEstimee` | bool (§5.3) |
| `SourceRaw` | ligne source complète, en JSON |

### 3.4 Table `Campagne` *(à créer, absente de la source)*

`Id`, `Libelle` (ex. « R-25/09 »), `DateEnvoi`, `DateRetour`, `PrestataireId`, `NumeroCertificat`, `NumeroOrdreTravail`, `CoutTotal`, `Remarque`.

---

## 4. Pipeline de migration

```
┌─────────────┐   1. Extraction    ┌──────────────┐
│  Access     │ ─────────────────► │  CSV bruts   │   (figés, versionnés)
│  .accdb     │   sans aucune      │  UTF-8       │
└─────────────┘   transformation   └──────┬───────┘
                                          │ 2. Chargement tel quel
                                          ▼
                                  ┌───────────────┐
                                  │  STAGING      │  tables miroir,
                                  │  (tout texte) │  aucune contrainte
                                  └──────┬────────┘
                                         │ 3. Profilage + rapport d'anomalies
                                         ▼
                                  ┌───────────────┐
                                  │ FICHIER       │ ◄── arbitrages
                                  │ D'ARBITRAGE   │     de M. Bastin
                                  │ (Excel)       │
                                  └──────┬────────┘
                                         │ 4. Transformation (règles §5-6)
                                         ▼
                                  ┌───────────────┐
                                  │  BASE CIBLE   │
                                  └──────┬────────┘
                                         │ 5. Réconciliation automatique
                                         ▼
                                  ┌───────────────┐
                                  │  RAPPORT DE   │  écarts vs PDF de référence
                                  │  CONTRÔLE     │
                                  └───────────────┘
```

**Le fichier d'arbitrage est la pièce maîtresse.** C'est un tableur généré automatiquement à partir du profilage, que Michel remplit hors ligne, et qui est ensuite **rejoué en entrée de la transformation**. Ainsi les décisions métier sont versionnées et le processus reste 100 % rejouable.

**Implémentation recommandée :** un projet console .NET séparé (même solution que l'application), avec les CSV sources et le fichier d'arbitrage en ressources. Alternative : script Python + pandas pour la phase de profilage, souvent plus rapide à écrire. À ne pas faire : import manuel via l'assistant Access/Excel.

---

## 5. Règles de nettoyage détaillées

### 5.1 Normalisation des marques

Le champ est libre, avec des variantes évidentes. Table de correspondance à établir :

| Valeurs sources observées | Cible proposée |
|---|---|
| `AQUALUNG`, `AQUALUNG MANNESMAN`, `HEIISER AQUALUNG` | Aqualung *(à trancher pour les composés)* |
| `HEISER`, `HEISE/SPIRO` | Heiser |
| `SPIRO`, `LB75A…` | Spiro |
| `ROTHMION`, `ROTHMIONS` | Rothmion *(orthographe correcte à confirmer)* |
| `FABER` | Faber |
| `ECS` | ECS |
| `GRUPPE` | Gruppe |
| `SCUBASPORT`, `SCUBAPRO/MANNESM` | *à trancher — deux marques différentes ?* |
| *(vide — fiche ILN093)* | Inconnu |

**Les valeurs composées** (`AQUALUNG MANNESMAN`, `HEISE/SPIRO`) désignent probablement fabricant du fût + fabricant du robinet. Si on modélise la robinetterie séparément (Q2.1 de l'analyse), il faut les scinder. **→ arbitrage requis**

### 5.2 Normalisation des lieux

Trois valeurs : `SERAING`, `HACCOURT`, `ROBERTVILLE` (1 seule occurrence). Plus `Local Phys` = `Piscine` / `PISCINE` (casse variable). À clarifier avec Q1.1 de l'analyse avant de figer le référentiel.

### 5.3 Dates conventionnelles

Les dates `01-01-AA`, `01-06-AA`, `01-05-AA` sont massivement présentes sur les événements anciens. Ce sont des **dates inventées** signifiant « quelque part cette année-là ».

**Règle :** conserver la date telle quelle, mais marquer `DateEstimee = true` selon l'heuristique suivante :
- jour = 01 **et** mois = 01 → estimée (année seule connue)
- jour = 01 et mois ≠ 01 → probablement estimée (mois connu) — **à confirmer**
- toute autre date → réputée exacte

L'interface devra afficher ces dates différemment (ex. « ~2005 »). Sans cela, l'application afficherait une précision au jour qui n'existe pas.

### 5.4 Conflit `Volume` / `Capacité`

Sur plusieurs fiches les deux diffèrent (n°1 : volume 6 / capacité 9 ; n°9 : volume 10 / capacité 12 ; n°61 : volume 12 / capacité 12,2 ; n°18 : volume 10 / capacité 10,1).

**Hypothèse :** `Volume` = volume nominal commercial, `Capacité` = volume réel mesuré lors de la réépreuve. Les écarts de 0,1-0,2 L le confirment. Mais les écarts de 6→9 et 10→12 sont trop grands pour ça : ce sont des **erreurs de saisie**.

**Règle proposée :** conserver les deux champs (`VolumeNominal`, `VolumeReel`) et **signaler pour arbitrage** tout écart supérieur à 10 %. Concerne au moins 3 fiches.

### 5.5 Pressions de service aberrantes

`348 bar` sur B3167 et MVM062. Aucune bouteille de plongée loisir ne fonctionne à 348 bar (standards : 200, 230, 232, 300). Probable erreur de saisie (300 ? 248 ?). **→ arbitrage, 2 fiches**

Valeurs vides : plusieurs fiches sans `PrSERVICE` ni `Tare`. À compléter par relevé physique.

### 5.6 Codification `Ident_Site`

Formats observés : `BOUT-F-08`, `BOUT-S-6`, `BOUT-S-H04`, `BOUT-S-H5/83`, `BOUT-MOANA_F`, `NIHIL`, vide.

**Points à trancher :**
- Que signifient `F` et `S` ? (Le `Lieu` est un champ distinct, donc ce n'est pas Seraing/Haccourt.) Hypothèses : Fixe/Sortie, Fond/Surface, ou un ancien découpage abandonné.
- `NIHIL` = « rien » en latin → équivaut à vide. À convertir en `NULL`.
- ~15 fiches sans code.

**Recommandation :** générer un `CodeClub` propre et unique (`BT-001` à `BT-064`) tout en conservant l'ancien code dans `CodeClubHistorique`. Cela rend l'inventaire cohérent sans perdre l'information. Suppose une **campagne de réétiquetage physique** (cohérente avec la pose de QR codes).

### 5.7 Déduction du champ `Resultat`

Le champ n'existe pas en source : l'information est noyée dans les remarques. Règles d'extraction (insensibles à la casse) :

| Motif dans la remarque | `Resultat` |
|---|---|
| `MANQUANTE`, `INTROUVABLE`, `disparue` | Manquante |
| `NON EFFECTUEE` | NonEffectuee |
| `Ecartée suite à` | → `TypeEvenement = Ecartement` |
| `REBUTEE` | → `TypeEvenement = Rebut` |
| `PROBLEME ROBINETTERIE`, `Robinetterie abimée` | Refus + motif technique |
| `ROUILLE`, `BOUTEILLE SALE` | Conforme avec réserve → à signaler |
| aucun motif + coût > 0 | Conforme |
| aucun motif + coût = 0 + date ancienne | Inconnu (historique non chiffré) |

**Toute remarque non reconnue par ces règles doit remonter dans le fichier d'arbitrage** plutôt que d'être silencieusement ignorée.

### 5.8 Numéros de certificat

Formats : `Lb. 125592`, `LB134008`, `LB.135348`, `LB127966`, `Ordre de travail 7462 - LB135348`.

**Règle :** extraction par expression régulière `LB\.?\s?(\d{6})` → `NumeroCertificat = "LB" + chiffres`. Le préfixe « Ordre de travail NNNN » est extrait dans un champ distinct.

**Cas particulier :** `Lb. 125591 MODIFIE EN Lb. 125648 suite erreur apragaz` (2 fiches). Le certificat valide est **125648**. Le texte doit être conservé en remarque et le numéro corrigé retenu.

### 5.9 Champ `Age`

Valeurs hétérogènes : `34,168`, `3`, `0,5`, `7,0061`, vide. Certaines semblent être des années décimales calculées, d'autres des saisies manuelles arrondies. **Ce champ n'est pas migré** — il sera recalculé à l'affichage.

---

## 6. Enrichissements à la migration

### 6.1 Calcul du statut initial

Le statut n'existe pas en source. Il doit être calculé, puis **validé fiche par fiche** :

```
si dernier événement = Rebut                    → Rebutee
sinon si dernier événement = Ecartement          → Ecartee
sinon si dernier résultat = Manquante (≥2 fois)  → Perdue
sinon si dernier résultat = Manquante            → Manquante
sinon si DateProchaineRequalif est nulle         → ArbitrageRequis
sinon si DateProchaineRequalif < aujourd'hui     → HorsValidite
sinon                                            → EnStock
```

Application à la date du 01/09/2026, résultat attendu approximatif :
- **~55 bouteilles** en stock valide (échéances 2027-2028)
- **1 perdue** (BOUT-F-70, manquante à 2 campagnes consécutives)
- **~4 sans échéance** → arbitrage (fiche 3A + les 3 bouteilles O₂ MOANA)
- **1 contradictoire** (BOUT-F-52 : rebutée *et* réépreuvée)

Ces chiffres sont à confirmer sur la base réelle.

### 6.2 Reconstitution des campagnes

Déductible des références et certificats communs. Campagnes identifiées dans l'export :

| Libellé | Date | Certificats | Observation |
|---|---|---|---|
| Mars 2023 | 16-03-23 | Lb.125591, 125592, 125597 → 125648 | Erreur Apragaz corrigée |
| Sept. 2023 | 20-09-23 | LB127966 | |
| Août 2024 | 08-08-24 | LB131088 | Grosse campagne (~20 bouteilles) |
| Sept. 2025 | 09-09-25 | LB134008 | 3 bouteilles déclarées manquantes |
| Mars 2026 | 16-03-26 | LB135348, LB135349 — ordre de travail 7462 | |

**Règle de regroupement :** même date + même numéro de certificat = même campagne. Cas non regroupables → campagne « historique » générique.

### 6.3 Grille tarifaire déduite (utile pour le contrôle)

| Période | Optique (RR) | Hydraulique (R) |
|---|---|---|
| 2023 | 19,90 € | 32,61 / 32,62 € |
| 2024 | 20,50 € | 33,59 € |
| 2025 | 21,32 € | 34,93 € |
| 2026 | 21,32 € | 35,82 € |

Toute ligne s'écartant de cette grille (hors 0,00 € historiques) doit être signalée. Cette grille servira aussi à **estimer le budget** de la prochaine campagne.

---

## 7. Contrôles de réconciliation

À exécuter automatiquement après chaque import, avec rapport d'écarts :

| # | Contrôle | Valeur attendue |
|---|---|---|
| C1 | Nombre de bouteilles importées | 64 |
| C2 | Somme de tous les coûts | **1 830,82 €** |
| C3 | Somme des coûts par bouteille | = total imprimé de chaque fiche |
| C4 | Nombre total d'événements | = nombre de lignes source |
| C5 | Unicité de `NumeroSerie` | 0 doublon (⚠ vérifier `MOANA_S`) |
| C6 | Unicité de `CodeClub` | 0 doublon |
| C7 | Chaque bouteille a ≥ 1 événement | ⚠ B3167, MVM062, N87619 n'ont qu'un événement récent |
| C8 | Aucun événement antérieur à la mise en service | ⚠ ILN093 échoue (anomalie A14) |
| C9 | Événements triés chronologiquement, sans doublon | ⚠ n°17 a deux `RR-Optique` le 01-01-13 |
| C10 | Toute valeur de `Marque` mappée | 0 non mappée |
| C11 | Aucune date future incohérente | dates de réalisation ≤ aujourd'hui |
| C12 | Cohérence alternance R/RR | signalement, non bloquant |

**Le contrôle C2 est le plus important** : si le total ne tombe pas exactement à 1 830,82 €, la migration a perdu ou dupliqué des lignes.

---

## 8. Fichier d'arbitrage — contenu à soumettre à M. Bastin

Un tableur avec une ligne par point à trancher, colonnes :
`Réf · Bouteille (code + n° série) · Constat · Question · Décision · Commentaire · Date`

**Contenu minimal (issu de l'analyse fonctionnelle §1.3) :**

| Réf | Bouteille | Question posée |
|---|---|---|
| A1 | BOUT-F-70 / 26461 | Manquante depuis sept. 2025, absente 2 campagnes → déclarer perdue ? |
| A2 | BOUT-F-52 / X26628 | Marquée REBUTEE mais réépreuvée en 2025 → en service ou rebutée ? |
| A3 | n° 3A / 25072 | Aucune réépreuve depuis 2020 → où est-elle ? statut ? |
| A4 | MOANA_F, MOANA_R, MOANA_S | Bouteilles O₂ sans échéance → quel cycle appliquer ? |
| A5 | 06/1496/087 et 06/1496/072 | Même numéro `MOANA_S` → renuméroter laquelle ? |
| A6 | BOUT-S-72 / BOUT-S-73 | Numéros inversés (« 73 ex 71 ») → quelle numérotation retenir ? |
| A7 | ~15 fiches | `Ident_Site` vide ou `NIHIL` → accepte-t-on la renumérotation générale ? |
| A11 | n°1, n°9, n°18, n°61 | Volume ≠ Capacité → quelle valeur est juste ? |
| A12 | B3167, MVM062 | 348 bar → valeur réelle ? |
| A13 | 85/3002/47 | Destination « A VERIFIER », rouille, disparue 3 ans → utilisable ? |
| A14 | ILN093 | Requalification datée avant la mise en service → corriger quelle date ? |
| — | toutes | Confirmer la liste des marques normalisées |
| — | toutes | Confirmer signification de `F` / `S` dans les codes |
| — | toutes | Confirmer périodicité (30 mois ?) et règle d'alternance |

Je peux générer ce fichier au format Excel prêt à remplir, avec listes déroulantes, sur demande.

---

## 9. Recette et bascule

1. **Import à blanc** sur environnement de test → rapport de contrôle §7
2. **Revue avec M. Bastin** : il ouvre 10 fiches au hasard et les compare au PDF du 01/09/2026, ligne par ligne
3. **Corrections** du fichier d'arbitrage et des règles → réimport complet
4. Itérer jusqu'à **zéro écart non expliqué**
5. **Gel de la base Access** : plus aucune saisie dedans à partir d'une date convenue
6. **Import final** + archivage de la base Access et du PDF de référence
7. **Double saisie temporaire ?** Sur 2-3 semaines, en cas de doute — mais uniquement si le club l'accepte, car c'est contraignant

**Le point 5 est un engagement organisationnel indispensable.** Si Michel continue à saisir dans Access après la migration, les deux bases divergent immédiatement.

---

## 10. Charge estimée

| Phase | Charge indicative |
|---|---|
| Audit de la base Access | 0,5 j |
| Développement extraction + staging | 1 j |
| Profilage et génération du fichier d'arbitrage | 1 j |
| Développement des transformations (§5-6) | 2 j |
| Contrôles de réconciliation | 1 j |
| Itérations de recette (3 à 5 tours) | 1,5 j |
| **Total développeur** | **~7 jours** |
| Arbitrages métier (M. Bastin) | 1 à 2 j, en parallèle |

Cette charge concerne **uniquement les bouteilles**. Les autres familles de matériel n'ont pas d'existant à reprendre : elles seront saisies à la main, ce qui est un chantier distinct (et probablement plus lourd en temps club).

---

## 11. Ce qu'il faut lancer maintenant

**En parallèle, sans attendre le développement :**

1. **Obtenir le fichier Access** — c'est le seul vrai bloquant.
2. **Faire remplir le fichier d'arbitrage** — travail purement métier, réalisable dès aujourd'hui à partir du tableau §8.
3. **Décider de la renumérotation** — elle conditionne l'étiquetage QR et donc la commande des étiquettes.
4. **Faire relever physiquement** les données manquantes (tare, pression sur les fiches incomplètes) lors du prochain passage au local.

---

## 12. Questions ouvertes propres à la reprise

- **QR.1** — La base Access est-elle accessible, et sous quelle forme ? Y a-t-il plusieurs versions en circulation ?
- **QR.2** — Existe-t-il un code VBA ou des requêtes calculant l'échéance ? (cela répondrait à la question des 30 mois)
- **QR.3** — Que signifient `F` et `S` dans `BOUT-F-xx` / `BOUT-S-xx` ?
- **QR.4** — Les dates `01-01-AA` sont-elles bien des dates conventionnelles ?
- **QR.5** — Acceptez-vous une renumérotation complète du parc avec conservation de l'historique des codes ?
- **QR.6** — À partir de quelle date peut-on geler la saisie dans Access ?
- **QR.7** — Les certificats Apragaz existent-ils sous forme numérique, ou faut-il les scanner ?
