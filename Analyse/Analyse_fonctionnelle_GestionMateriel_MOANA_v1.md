# Analyse fonctionnelle — Application de gestion du matériel
## Royal Moana — École de plongée sous-marine

**Version :** 1.0 (première itération, à valider avec le demandeur)
**Base :** Cahier de charge « Gestion matériel du MOANA » (M. Bastin) + export « Identification et historique des Requalifications » (64 fiches bouteilles, état au 01/09/2026)
**Cible technique envisagée :** PWA Blazor (PC + mobile)

---

## 1. Ce que disent réellement les documents

### 1.1 Le cahier de charge

C'est un document volontairement « en vrac » (l'auteur le dit lui-même), qui exprime **un besoin de traçabilité avant un besoin de gestion**. Trois phrases sont structurantes :

1. *« Le but est de tracer chaque élément et principalement les réépreuve bouteilles ET entretiens détendeurs. »* → le cœur métier n'est pas l'inventaire, c'est **le suivi d'échéances réglementaires et de maintenance**.
2. *« Pouvoir sortir en une seule opération tout l'état de stock de tout le matériel afin de réaliser les contrôles. »* → besoin d'un **export inventaire complet**, utilisé sur le terrain lors du contrôle physique annuel.
3. *« À tout moment, le CA doit pouvoir interroger le gestionnaire matériel quant à l'état du matériel ou des besoins… suivi permanent… pour maîtriser les dépenses. »* → besoin d'un **volet financier et de reporting vers le Conseil d'Administration**.

Autrement dit : le vrai livrable, ce sont les **alertes + l'historique + les états**. Le reste (petit matériel, pièces détachées) est de l'inventaire classique.

### 1.2 L'existant : le fichier bouteilles

L'export a toutes les caractéristiques d'un **état Microsoft Access** (pagination « Page X sur 65 », mise en page en fiches, regroupements). C'est aujourd'hui le seul actif de données du club, et il ne couvre **qu'une seule famille de matériel sur les dix** demandées.

**Champs d'identification déduits :**

| Champ | Exemple | Commentaire |
|---|---|---|
| `Local Phys` | Piscine | Lieu d'usage / d'affectation |
| `Ident_Site` | BOUT-F-08, BOUT-S-6, NIHIL, (vide) | Code interne club — **incohérent, voir §1.3** |
| `Identification` | 85071, N87687, 85/3006/046, LB75A2058 | N° de série gravé sur le fût (identifiant fabricant) |
| `N° identbout` | 8, 1A, H5/83, MOANA_S, OKA170 | Numéro peint sur la bouteille |
| `Type de bouteille` | PLONGEE / DECOMPRESSION O² | Détermine les règles de nettoyage O₂ |
| `Filet` | M25X2 / O² | Filetage du robinet |
| `Double sortie`, `Sangles` | (booléens, jamais renseignés dans l'export) | |
| `Marque` | AQUALUNG, FABER, SPIRO, HEISER, ECS, GRUPPE… | Orthographes multiples : HEISER / HEISE-SPIRO / HEIISER, ROTHMION / ROTHMIONS |
| `Volume` | 5, 6, 7, 9, 10, 11, 12, 15 L | |
| `PrSERVICE` | 200 / 230 / 232 / 348 bar | 348 bar est douteux (cf. §1.3) |
| `Tare` | 6 à 16 kg | Utile pour le lestage et le contrôle |
| `Capacité` | souvent ≠ Volume (ex. vol 6 / cap 9) | **Doublon incohérent** |
| `Lieu` | SERAING, HACCOURT, ROBERTVILLE | Deuxième notion de lieu, différente de `Local Phys` |
| `Age` | 34,168 / 3 / 0,5 / vide | Champ calculé jamais rafraîchi |
| `Destination` | Adulte / Enfant / A VERIFIER | |

**Champs d'historique (1 ligne = 1 événement) :**
`Remarque Réépreuve` · `CERTIFICAT N°` · `Date Réé` · `Type de Réépreuve` · `Coût` · `Réé Suivante` (date) · type de la suivante (R / RR).

**Vocabulaire métier repéré :**
- `New` = mise en service initiale
- `R - Hydraulique` = réépreuve hydraulique (pression)
- `RR - Optique` = requalification par contrôle visuel/optique interne
- `Ecartée suite à une …` = mise à l'écart (avarie robinetterie, etc.)
- `-REBUTEE-` = destruction / fin de vie
- Références du type `R-25/09`, `RR-23/03`, `R24/08` = campagne de réépreuve (année/mois d'envoi groupé)
- `Lb. 125592`, `LB134008`, `Ordre de travail 7462` = n° de certificat / bon de l'organisme agréé (Apragaz)

**Règles de gestion observées :**
- Les bouteilles partent **par campagnes groupées** (mars 2023, sept. 2023, août 2024, sept. 2025, mars 2026), pas individuellement. Un même certificat couvre plusieurs bouteilles.
- Le cycle semble être **une requalification tous les 30 mois** (16-03-23 → 16-09-25 → 01-03-28), avec **alternance hydraulique / optique**. Le champ « type de la suivante » impose donc l'alternance.
- Les **coûts** ne sont saisis que depuis 2023 (~20 € optique, ~33-36 € hydraulique). Total cumulé affiché : **1 830,82 €**.

### 1.3 Anomalies détectées dans les données actuelles

Ces points sont importants : ils justifient à eux seuls une partie des contraintes de la future application.

| # | Constat | Impact |
|---|---|---|
| A1 | **BOUT-F-70 (26461)** : « MANQUANTE SEP 2025 » puis « MANQUANTE MARS 2026 », sans date de réépreuve suivante | Bouteille perdue depuis ≥ 1 an, toujours présente dans l'inventaire actif |
| A2 | **BOUT-F-52 (X26628)** : porte la mention `-REBUTEE-` en tête **mais** a une réépreuve validée le 09-09-25 et une échéance 01-03-28 | Contradiction de statut → risque de mise en service d'une bouteille rebutée |
| A3 | **Fiche n° 3A (25072)** : dernière opération le 03-08-20, aucune réépreuve depuis, aucune échéance | Bouteille hors validité depuis ~2023, non signalée |
| A4 | **Bouteilles O₂ MOANA_F / MOANA_R / MOANA_S** : dernière hydraulique en 2022, **aucune échéance planifiée** | 3 bouteilles de décompression sans suivi |
| A5 | **Deux fiches portent le même N° identbout `MOANA_S`** (séries 06/1496/087 et 06/1496/072) | L'identifiant « métier » n'est pas unique |
| A6 | **BOUT-S-73 porte le n° 72, BOUT-S-72 porte « 73 (ex 71) »** | Inversion / renumérotation non tracée |
| A7 | `Ident_Site` vaut **NIHIL** ou est **vide** sur ~15 fiches ; format non normalisé (BOUT-F-xx, BOUT-S-xx, BOUT-S-H04, BOUT-MOANA_F) | Impossible de s'appuyer dessus comme clé |
| A8 | Le pied de page annonce **« 379 Bouteilles à envoyer à la requalification »** alors qu'il y a 64 fiches | Agrégat faux (probable somme d'un champ numérique) |
| A9 | Nombreuses dates au **01-01-AA / 01-06-AA** = dates inconnues saisies par convention | Fausse précision ; à distinguer d'une vraie date |
| A10 | **BOUT-F-23 (55675)** : 4 hydrauliques consécutives, l'alternance R/RR n'est pas respectée | La règle d'alternance est-elle stricte ? (Q2.3) |
| A11 | `Capacité` ≠ `Volume` sur plusieurs fiches (ex. n°1 : 6 L / capacité 9 ; n°9 : 10 L / capacité 12) | Deux champs redondants dont un est faux |
| A12 | **PrSERVICE 348 bar** sur B3167 et MVM062 | Valeur non standard pour de la plongée loisir — erreur probable (300 ?) |
| A13 | Fiche **85/3002/47** : `Destination = A VERIFIER`, « disparue depuis 2021 », « ROUILLE » — et pourtant réépreuvée en 2024 | Matériel douteux remis en service |
| A14 | **BOUT-S-82 (ILN093)** : une requalification optique datée **avant** la mise en service | Incohérence chronologique |

> **Recommandation :** ces 14 points doivent faire l'objet d'un **arbitrage manuel avant la reprise de données**. L'application ne doit pas importer des données incohérentes « telles quelles » — sinon elle héritera du problème qu'elle est censée résoudre.

---

## 2. Architecture fonctionnelle d'ensemble

```
                    ┌─────────────────────────────────────────┐
                    │  M0 · SOCLE (auth, rôles, audit, réf.)  │
                    └─────────────────────────────────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
┌───────▼────────┐          ┌─────────▼─────────┐         ┌─────────▼────────┐
│ M1 · INVENTAIRE│          │ M7 · CYCLE DE VIE │         │ M12 · HISTORIQUE │
│  (socle Item)  │◄────────►│  statuts & sorties│◄───────►│  & journal (log) │
└───────┬────────┘          └───────────────────┘         └──────────────────┘
        │
        ├── M2 · Bouteilles & requalifications   ◄── cœur métier
        ├── M3 · Détendeurs & entretiens         ◄── cœur métier
        ├── M4 · Gilets (stabs)
        ├── M5 · Petit matériel (masques, tubas, palmes, ceintures…)
        ├── M6 · Matériel didactique
        └── M7bis · Matériel de sauvetage (DEA, kit O₂)
        
┌────────────────┐  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐
│ M8 · PRÊTS     │  │ M9 · ACHATS    │  │ M10 · PIÈCES   │  │ M11 · BOUTIQUE │
│ réservations   │  │ devis/factures │  │ détachées      │  │ (à clarifier)  │
└────────────────┘  └────────────────┘  └────────────────┘  └────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│ M13 · TABLEAU DE BORD & ALERTES   │ M14 · EXPORTS & ÉTATS   │ M15 · PWA/scan │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Principe directeur proposé :** un **modèle « Item » générique** (tout objet du club a un identifiant, un statut, un lieu, un propriétaire, un historique) + des **spécialisations typées** (Bouteille, Détendeur, Gilet, DEA…) qui ajoutent leurs champs et leurs règles d'échéance. Cela évite d'écrire 8 fois la même mécanique de statut / prêt / historique.

---

## 3. Analyse détaillée par module

---

### M0 · Socle : utilisateurs, rôles, droits, audit

**Objectif.** Permettre à l'équipe matériel de travailler à plusieurs, avec des responsabilités différentes, et garder trace de qui fait quoi.

**Rôles demandés par le cahier de charge :**

| Rôle | Périmètre demandé | Interprétation proposée |
|---|---|---|
| **Admin** (responsable matériel) | Tous privilèges, y compris **révocation** des droits des *users* hors CA | Super-utilisateur unique ou petit groupe |
| **CA** (membre du Conseil) | Lecture | Accès aux tableaux de bord, états de stock, suivi budgétaire — **pas** aux opérations |
| **User** (équipe matériel) | Lecture + écriture sur les prêts *(à discuter)* | Opérateur terrain : constate, saisit, prête, rend |

**Point d'attention.** « Admin peut révoquer les privilèges des users **hors CA** » signifie que le responsable matériel ne peut pas retirer l'accès d'un membre du CA. C'est une règle **organisationnelle** : elle implique que le rôle CA est attribué par quelqu'un d'autre (le CA lui-même). Il faut donc soit un 4ᵉ rôle « Super-admin / Président », soit accepter que la liste des CA soit gérée en base par le développeur. **→ Q0.1**

**Fonctionnalités :**
- Création de compte, réinitialisation de mot de passe, désactivation (jamais de suppression — on garde l'historique)
- Attribution de rôles, avec journalisation
- Idéalement : rattachement du compte à un **membre du club** (n° de licence LIFRAS ?) pour lier les prêts à des personnes
- **Journal d'audit** : qui a créé/modifié/supprimé quoi, quand, valeur avant/après. Non modifiable, non purgeable.

**Modèle de permissions recommandé.** Ne pas coder « if (role == Admin) » partout, mais des **permissions nommées** (`Bouteille.Write`, `Pret.Create`, `Facture.Read`, `Users.Manage`) regroupées en rôles. Ça coûte 2 heures de plus au départ et évite une refonte à la première demande de nuance.

**Points ouverts → Q0.1 à Q0.4**

---

### M1 · Inventaire — socle commun

**Objectif.** Décrire tout objet appartenant au club, quelle que soit sa famille.

**Attributs communs à tout item :**
- Identifiant interne (auto, immuable) — **c'est la vraie clé**, jamais le n° peint
- Code club affiché (ex. `BOUT-S-49`) — modifiable, non structurant
- N° de série fabricant
- Famille / catégorie / sous-catégorie
- Marque, modèle
- Date d'acquisition, prix d'achat, référence facture (→ M9)
- Statut (→ M7)
- Lieu de stockage
- Destination (Adulte / Enfant / Encadrement)
- Photo(s)
- Remarques libres
- Historique complet (→ M12)

**La question des lieux.** Le fichier actuel a **deux notions distinctes** : `Local Phys` (Piscine) et `Lieu` (SERAING / HACCOURT / ROBERTVILLE). Il faut clarifier : s'agit-il de (a) local de stockage vs site de plongée, (b) local actuel vs local d'origine, (c) autre chose ? Je propose un **arbre de lieux à 2 niveaux** (Site → Local/Armoire) plus un champ « position actuelle » qui peut aussi valoir « chez un membre (prêt) » ou « chez le prestataire (réépreuve) ». **→ Q1.1**

**La question de la codification.** Vu les anomalies A5/A6/A7, je recommande de **normaliser les codes club** lors de la reprise, sur un format unique du type `BT-012`, `DT-005`, `GL-018`, avec unicité garantie. Le code doit apparaître sur une **étiquette QR** collée sur l'objet (→ M15). **→ Q1.2**

**Points ouverts → Q1.1 à Q1.4**

---

### M2 · Bouteilles & requalifications *(module prioritaire)*

**Objectif.** Garantir qu'aucune bouteille hors validité ne soit mise à disposition d'un plongeur, et préparer/suivre les campagnes de réépreuve.

**Données spécifiques :** volume, pression de service, tare, filetage, robinetterie (double sortie ?), sangles/filet, type de gaz (air / nitrox / O₂ déco), compatibilité O₂ (nettoyage), fabricant, date de fabrication, destination adulte/enfant.

**Sous-entité « Robinetterie ».** Les données montrent des mises à l'écart pour « problème robinetterie » et des robinets abîmés. Le robinet est une pièce **démontable, remplaçable et à entretenir séparément** du fût. Je recommande de le modéliser comme un **composant lié** (avec son propre historique d'entretien), pas comme un simple booléen. **→ Q2.1**

**Événements de requalification** (1..n par bouteille) :
`Date` · `Type` (Mise en service / Hydraulique / Optique / Écartement / Rebut / Constat) · `Résultat` (Conforme / Refus / Non effectuée / Bouteille manquante) · `Coût` · `Prestataire` · `N° certificat` · `N° ordre de travail` · `Campagne` · `Remarque` · `Date échéance suivante` · `Type échéance suivante` · `Pièce jointe (PDF certificat)`

**Règles de gestion à implémenter :**

| Règle | Description | À valider |
|---|---|---|
| R2.1 | Périodicité = 30 mois (à confirmer), calculée depuis la date de la dernière requalification conforme | Q2.2 |
| R2.2 | Alternance hydraulique ↔ optique imposée par le champ « type suivant » | Q2.3 |
| R2.3 | **Une bouteille dont l'échéance est dépassée passe automatiquement en statut « Hors validité » et sort du stock utilisable** (exigence explicite du CDC, point 4.a.iv) | — |
| R2.4 | Une bouteille « Manquante » à deux campagnes consécutives bascule en « Perdue » | Q2.4 |
| R2.5 | Une bouteille « Rebutée » ne peut plus recevoir de requalification ni être prêtée (cf. anomalie A2) | — |
| R2.6 | Les bouteilles O₂ déco suivent-elles le même cycle ? Contrôle de nettoyage O₂ annuel à tracer ? | Q2.5 |
| R2.7 | Une bouteille en cours de réépreuve (partie chez le prestataire) est indisponible mais pas « hors service » | — |

**Notion de campagne.** C'est le vrai mode opératoire du club et il est absent du fichier actuel (juste une référence textuelle `R-25/09`). Je propose une **entité Campagne** : date d'envoi, prestataire, liste des bouteilles envoyées, n° de bon, date de retour, coût total, certificats reçus. Elle permet de :
- générer le **bordereau d'envoi** (PDF) à remettre à Apragaz
- pointer les retours (et détecter les manquantes, cf. « MANQUANTE SEP 2025 »)
- saisir les coûts en une fois plutôt que bouteille par bouteille
- alimenter le suivi budgétaire du CA
**→ Q2.6**

**Écrans :**
1. Liste bouteilles (filtres : statut, échéance, lieu, volume, destination, type)
2. Fiche bouteille : caractéristiques + timeline des requalifications + composants + photos + documents
3. Écran « Préparer une campagne » : sélection multiple des bouteilles dont l'échéance tombe dans les N mois → génération du bordereau
4. Écran « Retour de campagne » : pointage, saisie certificats + coûts en masse
5. Écran mobile « Scanner une bouteille » → état de validité en 1 seconde (usage bord de bassin)

**Points ouverts → Q2.1 à Q2.7**

---

### M3 · Détendeurs & entretiens *(module prioritaire, entièrement à créer)*

**Objectif.** Le CDC le met explicitement au même niveau que les bouteilles, or **rien n'existe aujourd'hui**. C'est le module qui apporte le plus de valeur nouvelle.

**Données spécifiques :** marque, modèle, n° de série (souvent gravé sur le 1ᵉʳ étage), type (1ᵉʳ étage / 2ᵉ étage / octopus / manomètre / direct-system), configuration (un détendeur = un **ensemble** de plusieurs pièces sériées), pression de service, compatibilité nitrox/O₂, date d'achat.

**Point de modélisation important.** Un « détendeur » au sens du club est un **kit** : 1ᵉʳ étage + 2ᵉ étage principal + octopus + manomètre + flexibles. Ces éléments peuvent être **recombinés** entre kits. Deux options :
- **(a) Simple** : le détendeur est un objet unique avec un n° de série, on ne trace pas les sous-éléments.
- **(b) Complet** : chaque étage est un item, groupé dans un « ensemble détendeur » avec historique de composition.
L'option (b) est plus juste mais coûte cher en saisie. **→ Q3.1**

**Entretiens :** révision annuelle ou biennale selon le fabricant, remplacement de kits de joints (→ lien direct avec M10 pièces détachées), contrôle avant-saison, réglage. Chaque entretien : date, opérateur (interne / atelier externe), pièces consommées, coût, résultat, prochaine échéance.

**Règles :**
- R3.1 : échéance d'entretien paramétrable **par modèle** (Aqualung ≠ Scubapro ≠ Mares)
- R3.2 : un détendeur dont l'entretien est échu sort du stock prêtable (même logique que R2.3)
- R3.3 : traçabilité « qui a fait l'entretien » (interne = personne nommée, externe = fournisseur + facture)

**→ Q3.1 à Q3.4**

---

### M4 · Gilets (stabilisateurs)

**Objectif.** Suivre les gilets, mentionnés séparément dans le CDC (4.a.iii) — donc considérés comme une famille à part entière.

**Données :** marque, modèle, taille (XS→XXL, crucial pour l'attribution), volume de flottabilité, présence d'un direct-system, sangles, plombs intégrés (poches), état.

**Suivi :** un gilet n'a pas d'obligation réglementaire, mais il a un **contrôle périodique de bon fonctionnement** (étanchéité vessie, purges, inflateur) qu'il est pertinent de tracer au même format que les entretiens. Prévoir aussi le **remplacement du direct-system** (pièce d'usure).

**→ Q4.1 à Q4.2**

---

### M5 · Petit matériel (masques, tubas, palmes, ceintures, plombs)

**Objectif.** Savoir combien on en a, dans quel état, et ce qu'il faut racheter avant la saison.

**Différence fondamentale avec les modules précédents :** ce matériel est **fongible et non sérié**. Suivre individuellement 40 masques est une perte de temps.

**Modèle proposé : gestion par lot / quantité.**
- Un « article » = *Masque junior modèle X, taille S*
- Attributs : quantité totale, quantité disponible, quantité en prêt, quantité HS, seuil d'alerte de réapprovisionnement
- Pas d'historique individuel, mais un **historique de mouvements** (entrée, sortie, mise au rebut, inventaire)

**Exception :** les **ceintures de plomb / plombs** relèvent plutôt d'une gestion en **poids total** (kg disponibles) que par pièce. **→ Q5.2**

**Attention à la tentation du sur-suivi.** C'est le module où l'on peut faire couler le projet en voulant tout tracer. Recommandation : lot + inventaire annuel, rien de plus.

**→ Q5.1 à Q5.3**

---

### M6 · Matériel didactique

**Objectif.** Point 4.b du CDC, non détaillé. À priori : mannequins de sauvetage, tables de décompression, plaquettes immergeables, maquettes, ordinateurs de démonstration, projecteur, matériel de piscine (cerceaux, lestage pédagogique), documentation.

Ce module se ramène très largement à M1 + M5 (inventaire + lot + prêt), sans échéance réglementaire. Il faut surtout savoir **qui l'a emprunté pour un cours** et **où c'est rangé**.

**→ Q6.1** — le contenu exact reste à faire préciser.

---

### M7 · Matériel de sauvetage (DEA, kit O₂) — *plus critique qu'il n'y paraît*

**Objectif.** Point 4.c du CDC. Ce module est traité rapidement dans le cahier de charge alors qu'il porte les **échéances les plus dures**.

**DEA / défibrillateur :**
- Date de péremption des **électrodes** (typiquement 2 ans, différentes adulte/pédiatrique)
- Date de péremption de la **batterie** (2 à 5 ans)
- Auto-test / vérification du témoin (à consigner mensuellement)
- N° de série, obligation de déclaration selon la réglementation belge
- **Alerte à 3 mois, 1 mois et à échéance** — un DEA hors service est un risque vital et un risque de responsabilité pour le club

**Kit O₂ :**
- Bouteille O₂ (déjà partiellement dans M2 : les fiches MOANA_F / MOANA_R / MOANA_S)
- Pression de remplissage à contrôler après chaque usage
- Masque/BAVU, détendeur O₂ dédié, durée de vie des consommables
- Traçabilité des **utilisations** (un kit utilisé = un kit à recontrôler)

**Trousse de secours :** péremption des consommables.

**Règle R7.1 :** tout élément de sauvetage échu déclenche une alerte de **niveau critique** (distincte des alertes matériel courantes) visible dès la page d'accueil.

**→ Q7.1 à Q7.3**

---

### M7bis · Cycle de vie & statuts *(transversal)*

**Objectif.** Répondre au point 4.d (« matériel déclassé, perdu, volé ») et corriger les anomalies A1/A2/A13.

**Machine à états proposée :**

```
        ┌──────────┐
        │ En stock │◄──────────────────┐
        └────┬─────┘                   │
             │                         │ retour
   ┌─────────┼──────────┬──────────────┴─────┐
   ▼         ▼          ▼                    │
┌──────┐ ┌────────┐ ┌────────────┐    ┌──────────────┐
│ Prêté│ │ En     │ │ En attente │    │ En maintenance│
│      │ │ contrôle│ │ de contrôle│   │  / réparation │
└──┬───┘ └───┬────┘ └─────┬──────┘    └───────┬──────┘
   │         │            │                   │
   │         ▼            ▼                   │
   │   ┌──────────────────────┐               │
   └──►│  Hors validité       │◄──────────────┘
       │  (indisponible)      │
       └──────────┬───────────┘
                  ▼
   ┌──────────┬───────────┬──────────┬──────────┐
   │ Déclassé │  Rebuté   │  Perdu   │   Volé   │  ← états terminaux
   └──────────┴───────────┴──────────┴──────────┘
                  │
                  ▼  (traçabilité conservée, jamais de suppression physique)
```

**Règles :**
- R7bis.1 : un état terminal est **irréversible** sauf action Admin explicite et journalisée (le cas « retrouvée dans le coffre de la cave » existe dans les données !)
- R7bis.2 : tout changement d'état demande **un motif + une date**, et pour Perdu/Volé éventuellement une pièce jointe (déclaration)
- R7bis.3 : le matériel en état terminal disparaît des listes par défaut mais reste consultable et exportable
- R7bis.4 : la valeur du matériel perdu/volé alimente un état pour le CA et l'assurance

**→ Q7bis.1 à Q7bis.2**

---

### M8 · Prêts et réservations

**Objectif.** Point 4.h : prêts pour les vacances et week-ends. Aujourd'hui probablement géré au carnet ou de mémoire.

**Fonctionnement proposé :**
- Un prêt = un emprunteur (membre) + une liste d'items + date de sortie prévue/réelle + date de retour prévue/réelle + un état constaté au départ et au retour + remarques
- **Réservation** en amont (« je pars à Robertville le 12, je bloque 4 blocs 12 L et 2 détendeurs »)
- Contrôles bloquants à la sortie : item hors validité → **refus**, item déjà prêté → **refus**, item réservé par un autre → **avertissement**
- Retour : constat d'état → peut déclencher directement une mise en maintenance
- Relances automatiques sur retards
- Vue « qui a quoi actuellement »
- Historique complet par membre (utile en cas de litige ou de casse récurrente)

**Question de droit d'écriture.** Le CDC dit « user droits en écriture sur la gestion prêts de matériel (**à discuter**) ». Deux modèles possibles :
- (a) **Seule l'équipe matériel** saisit les prêts (le membre demande, l'équipe valide et sort le matériel)
- (b) **Auto-service** : le membre réserve lui-même en ligne, l'équipe valide
Le (b) est nettement plus riche mais implique d'ouvrir l'application à **tous les membres du club**, avec toutes les conséquences (comptes, RGPD, support). Vu le budget annoncé, je recommande (a) pour la v1. **→ Q8.1**

**Caution / responsabilité :** faut-il tracer une caution, une signature, une décharge ? **→ Q8.2**

**→ Q8.1 à Q8.4**

---

### M9 · Achats : devis, commandes, factures

**Objectif.** Points 4.e et 4.g + le paragraphe en rouge du CDC : *« maîtriser les dépenses »*. C'est le module **destiné au CA**.

**Sous-modules :**

**a) Besoins / demandes.** Un membre de l'équipe constate un manque → crée une demande (article, quantité, urgence, justification). Le responsable arbitre.

**b) Devis.** Fournisseur, date, montants HT/TVA/TTC, validité, lignes, PDF joint, statut (En attente / Accepté / Refusé / Expiré). Possibilité de **comparer plusieurs devis** pour un même besoin — c'est exactement ce que le CA voudra voir.

**c) Commandes.** Devis accepté → commande → réception (partielle possible) → **création automatique des items** dans l'inventaire. C'est le point d'intégration important : l'achat doit alimenter l'inventaire sans double saisie.

**d) Factures.** Le CDC demande explicitement le **scan PDF joint**. Ajouter : fournisseur, n° facture, date, montant, rapprochement avec la commande, affectation budgétaire, statut de paiement.

**e) Suivi budgétaire.** Budget annuel matériel (voté par le CA ?), engagé (devis acceptés), réalisé (factures), reste à consommer. Vue par famille de matériel et par exercice. Les coûts de requalification (M2) et d'entretien (M3) doivent y remonter automatiquement.

**Point d'attention comptable.** Il ne s'agit **pas** de faire une comptabilité — le club en a probablement déjà une. L'objectif est un **suivi analytique du poste matériel**. Il faut s'assurer qu'on ne duplique pas l'outil du trésorier. **→ Q9.1**

**→ Q9.1 à Q9.4**

---

### M10 · Stock de pièces détachées

**Objectif.** Point 4.f. Kits de joints, membranes, flexibles, sangles, boucles, direct-systems, o-rings, embouts, colliers.

**Modèle :** gestion par article + quantité + seuil d'alerte, comme M5. Différence : la **consommation est liée à un entretien** (M3/M4). Quand on révise un détendeur, on décrémente le kit de joints correspondant, ce qui donne le coût réel de l'entretien et déclenche le réapprovisionnement.

**Attributs :** référence fabricant, compatibilité (quels modèles de détendeur), quantité, seuil, prix unitaire, fournisseur, emplacement de rangement.

**→ Q10.1 à Q10.2**

---

### M11 · « Boutique matériel » — *point à clarifier absolument*

Le CDC cite « Boutique matériel » dans les **besoins** (2.c) mais ne le développe jamais dans la partie Application. Trois lectures possibles, avec des conséquences très différentes :

| Interprétation | Ce que ça implique | Charge |
|---|---|---|
| **(1) Catalogue interne** : la liste de ce que le club possède et met à disposition, présentée comme un « catalogue » consultable par les membres | Vue en lecture sur l'inventaire + réservation → largement couvert par M1 + M8 | Faible |
| **(2) Vente aux membres** : le club revend du petit matériel (masques, tubas, tee-shirts, sangles) aux membres | Panier, prix, stock commercial, encaissement, factures/reçus, TVA éventuelle | **Élevée** |
| **(3) Références fournisseurs** : un carnet d'adresses/tarifs pour préparer les devis | Simple annuaire lié à M9 | Faible |

**C'est la principale ambiguïté du cahier de charge.** Si c'est l'interprétation (2), cela ajoute un module e-commerce complet avec des questions de paiement et de fiscalité, incompatible avec un « budget minimum ». **→ Q11.1 (question bloquante)**

---

### M12 · Historique et journal

**Objectif.** Point 4.i : *« Historique complet du matériel (style Fichier Log en PDF par exemple) »*.

**Deux besoins distincts à ne pas confondre :**
- **Historique métier** : la vie de l'objet (achat, requalifications, entretiens, prêts, incidents, changement de statut, réforme). C'est ce qu'on imprime sur une fiche.
- **Journal technique / audit** : qui a modifié quel champ dans l'application, quand. C'est ce qui protège le responsable matériel en cas de contestation.

**Fonctionnalités :**
- Timeline chronologique unifiée sur la fiche de chaque item
- Immuabilité : on n'efface jamais un événement, on l'annule par un événement correctif
- Export **PDF « fiche de vie »** d'un item (l'équivalent moderne de la fiche Access actuelle)
- Export PDF/CSV de l'ensemble

**→ Q12.1**

---

### M13 · Tableau de bord & alertes

**Objectif.** C'est l'écran d'accueil, et probablement la fonctionnalité la plus utilisée.

**Contenu proposé :**
- **Alertes critiques** : DEA/O₂ échus, bouteilles hors validité encore en circulation
- **Échéances** : bouteilles à requalifier à 3 / 6 / 12 mois, détendeurs à réviser
- **Matériel indisponible** : en maintenance, manquant, en réépreuve
- **Prêts en retard**
- **Stock sous seuil** (pièces détachées, petit matériel)
- **Budget** : engagé / réalisé / restant sur l'exercice
- **Compteurs** : total items par famille, valeur d'inventaire

**Notifications.** Un tableau de bord ne suffit pas si personne ne l'ouvre. Prévoir un **e-mail récapitulatif** (hebdomadaire ou mensuel) au responsable matériel, et éventuellement au CA. C'est peu coûteux et ça transforme l'outil en véritable garde-fou. Les notifications push PWA sont possibles mais moins fiables. **→ Q13.1**

---

### M14 · Exports et états

**Objectif.** L'exigence explicite : *« sortir en une seule opération tout l'état de stock de tout le matériel afin de réaliser les contrôles »*.

**États à prévoir :**
1. **Inventaire complet** (PDF + Excel) — toutes familles, avec statut, lieu, échéance
2. **Feuille de contrôle terrain** : version imprimable avec cases à cocher, triée par lieu de rangement, pour le pointage physique annuel
3. **Fiche de vie d'un item** (PDF)
4. **Bordereau d'envoi en requalification**
5. **État des échéances** sur N mois
6. **État financier** pour le CA : dépenses par famille et par exercice, engagements en cours
7. **État du matériel déclassé/perdu/volé** (assurance)

**Note technique :** génération PDF côté serveur avec QuestPDF (licence gratuite sous conditions pour une ASBL) ou Aspose/IronPDF (payants). L'export Excel via ClosedXML.

**→ Q14.1**

---

### M15 · Spécificités PWA, mobilité et identification physique

**Usages mobiles réels à prévoir :**
- Au bord du bassin ou au local : **scanner une bouteille** et voir instantanément sa validité
- Sortir/rentrer du matériel en scannant, sans passer par un PC
- Prendre une **photo** d'un dommage constaté
- Faire l'inventaire annuel en marchant dans le local

**Étiquetage.** Je recommande fortement des **QR codes** (imprimables sur étiquettes résistantes, lisibles par l'appareil photo, gratuits) plutôt que des codes-barres ou du NFC. Prévoir dans l'application une fonction de **génération de planches d'étiquettes** à imprimer. **→ Q15.1**

**Mode hors-ligne.** Le local matériel d'un club est souvent en sous-sol, sans réseau. C'est l'argument principal en faveur d'une PWA. Mais l'offline **complet en écriture** (avec synchronisation et gestion des conflits) est coûteux. Proposition en trois niveaux :
- **Niveau 1 (recommandé v1)** : l'app se lance hors-ligne et affiche les données consultées récemment (cache) — lecture seule
- **Niveau 2** : les opérations simples (prêt, retour, constat) sont mises en file d'attente et envoyées à la reconnexion
- **Niveau 3** : base locale complète et synchronisation bidirectionnelle — à éviter en v1
**→ Q15.2**

**Implication Blazor.** Le niveau 1 ou 2 impose **Blazor WebAssembly** (ou Blazor Web App en mode Auto/WASM). Blazor Server ne peut pas fonctionner hors-ligne : il tombe dès que la connexion SignalR est coupée. Si l'offline n'est finalement pas nécessaire, Blazor Server est plus simple et moins gourmand à héberger.

---

## 4. Sujets transversaux

### 4.1 Reprise des données existantes
Les 64 fiches bouteilles doivent être migrées. Le PDF n'est pas une source exploitable : il faut **récupérer le fichier Access (ou Excel) d'origine**. Étapes : extraction → nettoyage (normalisation des marques, arbitrage des 14 anomalies §1.3, décision sur les dates conventionnelles `01-01-AA`) → import → **contrôle contradictoire avec le responsable matériel**. Compter une charge réelle non négligeable, souvent sous-estimée. **→ Q16.1**

### 4.2 Données personnelles (RGPD)
Dès qu'on enregistre des prêts nominatifs, on traite des données personnelles de membres. À prévoir : base légale, durée de conservation, information des membres, droit d'accès/rectification, hébergement en UE, sécurité des accès. C'est léger pour une ASBL mais ne doit pas être ignoré.

### 4.3 Sauvegardes
Sauvegarde quotidienne automatique + restauration testée + export périodique complet récupérable par le club. **C'est une exigence, pas une option** : l'application deviendra la seule source de vérité sur du matériel de sécurité.

### 4.4 Pérennité
Un club de plongée change de responsable matériel. L'application doit être documentée, les données exportables dans un format ouvert, et l'hébergement ne doit pas dépendre du compte personnel du développeur. **→ Q16.2**

---

## 5. Proposition de phasage

Le cahier de charge décrit ~10 modules. Tout livrer d'un coup est le meilleur moyen de ne rien livrer. Proposition :

### Phase 1 — MVP (le cœur de la valeur)
- M0 socle (auth, 3 rôles, audit)
- M1 inventaire générique + lieux + statuts (M7bis)
- **M2 bouteilles & requalifications** (avec campagnes)
- **M3 détendeurs & entretiens**
- M13 tableau de bord avec alertes d'échéances
- M14 export inventaire complet + fiche de vie PDF
- Reprise des données bouteilles
- PWA installable, consultation mobile, scan QR en lecture

> À la fin de la phase 1, le besoin n° 1 du CDC (*« tracer les réépreuves bouteilles ET les entretiens détendeurs »*) est satisfait.

### Phase 2 — Élargissement
- M4 gilets, M5 petit matériel, M6 didactique
- **M7 matériel de sauvetage** (à remonter en phase 1 si le club juge le risque DEA prioritaire — c'est défendable)
- M8 prêts et retours
- Notifications e-mail

### Phase 3 — Volet gestion
- M9 devis / commandes / factures avec PDF
- M10 pièces détachées liées aux entretiens
- États financiers pour le CA
- M11 « boutique » selon l'arbitrage Q11.1

---

## 6. Notes techniques (à confirmer après validation fonctionnelle)

- **Blazor** : le choix se joue entre *Blazor WebAssembly Standalone + API* (offline possible, PWA native, deux projets à maintenir) et *Blazor Web App .NET 9 en rendu Auto* (plus simple, offline partiel). Le facteur décisif est la réponse à **Q15.2** sur l'offline.
- **Persistance** : PostgreSQL (gratuit, robuste, hébergeable partout) via EF Core. SQLite est tentant pour le budget mais fragile en multi-utilisateurs concurrents.
- **Authentification** : ASP.NET Core Identity, suffisant et gratuit. Éviter un fournisseur externe payant.
- **Fichiers (PDF factures, certificats, photos)** : stockage objet (S3-compatible type Scaleway/OVH, quelques euros par an) plutôt qu'en base.
- **PDF** : QuestPDF (vérifier les conditions de licence pour une ASBL) ou génération HTML → PDF.
- **Hébergement « budget minimum »** : un petit VPS européen (5-10 €/mois) couvre largement les besoins d'un club, sauvegardes incluses. Les offres « gratuites » (Azure free tier, Fly.io) posent des problèmes de pérennité et de démarrage à froid.
- **Scan QR** : bibliothèque JS (`html5-qrcode` ou l'API `BarcodeDetector`) via interop Blazor.

---

## 7. Questions consolidées

### Bloquantes (à trancher avant de continuer)

- **Q11.1** — Que signifie exactement « **Boutique matériel** » ? Catalogue consultable, vente réelle aux membres, ou carnet de fournisseurs ? *(Cette réponse peut faire varier le projet d'un facteur 2.)*
- **Q16.1** — Le fichier source des bouteilles est-il bien une base **Access** ? Peut-on l'obtenir (.accdb / .mdb ou export Excel) ? Y a-t-il d'autres fichiers (tableur des détendeurs, carnet de prêts, factures) ?
- **Q15.2** — Le local matériel est-il couvert par le réseau (wifi / 4G) ? Faut-il vraiment un **mode hors-ligne en écriture**, ou une simple consultation suffit-elle ?
- **Q17.1** — Quel est le **volume total** à gérer, hors bouteilles ? (nombre approximatif de détendeurs, gilets, masques, combinaisons…) Et combien d'utilisateurs de l'application ?

### Module M0 — Accès
- **Q0.1** — Qui attribue le rôle « CA » si l'Admin ne peut pas le révoquer ? Faut-il un rôle Super-admin ?
- **Q0.2** — Combien de personnes dans l'équipe matériel ? Combien de membres au CA ?
- **Q0.3** — Les membres « simples » du club auront-ils un accès (même en lecture) ?
- **Q0.4** — Faut-il tracer nominativement chaque saisie (audit) ou une trace globale suffit-elle ?

### Module M1 — Inventaire
- **Q1.1** — Quelle est la différence entre `Local Phys` (Piscine) et `Lieu` (SERAING / HACCOURT / ROBERTVILLE) ? Combien de lieux de stockage réels ?
- **Q1.2** — Accepte-t-on de **renuméroter** le matériel pour avoir une codification propre, ou faut-il conserver les codes existants tels quels (`BOUT-S-H5/83`, `NIHIL`, doublons) ?
- **Q1.3** — Que doit valoir `Capacité` quand elle diffère de `Volume` (ex. 6 L / capacité 9) ? Quel est le champ juste ?
- **Q1.4** — Souhaite-t-on gérer des photos pour chaque item ?

### Module M2 — Bouteilles
- **Q2.1** — La **robinetterie** doit-elle être suivie séparément du fût (elle est démontable et remplaçable) ?
- **Q2.2** — Confirmez-vous une périodicité de **30 mois** ? Est-elle identique pour toutes les bouteilles (air, O₂, enfant) ?
- **Q2.3** — L'**alternance hydraulique / optique** est-elle une règle stricte imposée par Apragaz, ou une pratique ? (BOUT-F-23 a 4 hydrauliques d'affilée)
- **Q2.4** — Au bout de combien de temps une bouteille « manquante » est-elle déclarée « perdue » ? (BOUT-F-70 est manquante depuis sept. 2025)
- **Q2.5** — Les bouteilles de **décompression O₂** ont-elles des obligations supplémentaires à tracer (nettoyage O₂, contrôle annuel) ? Les trois bouteilles MOANA n'ont aucune échéance planifiée — est-ce normal ?
- **Q2.6** — Confirmez-vous que les réépreuves partent **par campagnes groupées** ? Combien de campagnes par an ? Un seul prestataire (Apragaz) ?
- **Q2.7** — Faut-il stocker les **certificats PDF** dans l'application ?

### Module M3 — Détendeurs
- **Q3.1** — Un détendeur est-il suivi comme **un objet unique** ou comme un **ensemble d'étages** interchangeables ?
- **Q3.2** — Quelle périodicité d'entretien ? Fixe pour tous, ou variable selon marque/modèle ?
- **Q3.3** — Les entretiens sont-ils faits **en interne** ou par un atelier externe ? (change beaucoup l'écran de saisie)
- **Q3.4** — Existe-t-il déjà des données sur les détendeurs à reprendre, ou tout est-il à créer ?

### Modules M4 à M7
- **Q4.1** — Faut-il un contrôle périodique des gilets ? À quelle fréquence ?
- **Q4.2** — La **taille** des gilets doit-elle être un critère de recherche/attribution ?
- **Q5.1** — Le petit matériel peut-il être géré **par lot/quantité** plutôt qu'individuellement ?
- **Q5.2** — Les plombs sont-ils comptés en pièces ou en kilos ?
- **Q5.3** — Le club possède-t-il des **combinaisons** ? (absentes du CDC mais très fréquentes, et gérées par taille)
- **Q6.1** — Que recouvre précisément le « matériel didactique » ?
- **Q7.1** — Combien de DEA ? Quelles sont les dates de péremption actuelles des électrodes et batteries ?
- **Q7.2** — Les kits O₂ font-ils l'objet d'un contrôle après chaque utilisation à tracer ?
- **Q7.3** — Le matériel de sauvetage doit-il remonter en **phase 1** vu sa criticité ?
- **Q7bis.1** — Quelle différence faites-vous entre « **déclassé** », « **écarté** » et « **rebuté** » ? (les trois apparaissent dans vos données)
- **Q7bis.2** — Faut-il gérer une valeur d'inventaire pour l'assurance ?

### Module M8 — Prêts
- **Q8.1** — Modèle **(a)** l'équipe matériel saisit tout, ou **(b)** les membres réservent eux-mêmes en ligne ?
- **Q8.2** — Faut-il tracer une caution, une décharge ou une signature de l'emprunteur ?
- **Q8.3** — Durée maximale d'un prêt ? Faut-il des relances automatiques ?
- **Q8.4** — Prête-t-on à des non-membres (stages, clubs invités) ?

### Module M9 — Achats
- **Q9.1** — Le trésorier utilise-t-il déjà un outil comptable ? Comment éviter la double saisie ?
- **Q9.2** — Y a-t-il un **budget matériel annuel** voté par le CA à suivre ?
- **Q9.3** — Faut-il un circuit de **validation** des achats (demande → arbitrage → commande) ou une simple saisie a posteriori ?
- **Q9.4** — Quel volume de factures par an ?

### Modules M10, M12 à M15
- **Q10.1** — Le stock de pièces détachées est-il aujourd'hui suivi d'une manière ou d'une autre ?
- **Q10.2** — Souhaite-t-on **décrémenter automatiquement** les pièces lors d'un entretien ?
- **Q12.1** — L'« historique en PDF » doit-il être une **fiche par matériel** (comme l'export Access actuel) ou un journal global ?
- **Q13.1** — Souhaitez-vous des **e-mails d'alerte** automatiques ? À quelle fréquence, et vers qui (responsable seul, ou CA aussi) ?
- **Q14.1** — Le contrôle physique du matériel se fait-il une fois par an ? Sous quelle forme aujourd'hui ?
- **Q15.1** — Peut-on **coller un QR code** sur chaque matériel (bouteilles comprises) ?

### Divers
- **Q16.2** — Qui hébergera l'application, et qui en aura les accès à long terme (question de pérennité au-delà du développeur actuel) ?
- **Q17.2** — Y a-t-il une **échéance** souhaitée ? (une campagne de réépreuve est prévue vers septembre 2026 d'après vos données)
- **Q17.3** — L'application doit-elle être multilingue (FR uniquement, ou FR/NL/EN) ?

---

## 8. Recommandations pour la suite

1. **Trancher les 4 questions bloquantes** avant toute conception technique.
2. **Récupérer le fichier Access source** — sans lui, la reprise de données est impossible et sa qualité conditionne le planning.
3. **Faire arbitrer les 14 anomalies** du §1.3 par le responsable matériel, sur papier. C'est un travail métier, pas informatique, et il peut démarrer immédiatement en parallèle.
4. **Valider le phasage** : obtenir un accord explicite sur le fait que la phase 1 ne couvre que bouteilles + détendeurs. C'est la meilleure protection contre l'élargissement progressif du périmètre.
5. **Faire préciser le budget réel** : « le minimum » n'est pas un chiffre. Une fourchette annuelle (hébergement + nom de domaine + sauvegardes) est nécessaire pour arbitrer les choix techniques.
