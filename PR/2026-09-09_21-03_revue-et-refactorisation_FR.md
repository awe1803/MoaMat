# MoaMat — revue .NET DDD / hexagonale et refactorisation

**Date :** 2026-09-09 21:03
**Branche :** `39-correction-base-bonnes-pratiques`
**Référence :** `fbe4fd2` (merge de la PR #37)
**Périmètre :** dépôt entier, mis en conformité avec la grille de revue DDD / Clean Architecture / .NET, plus « une classe par fichier » et « code en anglais ».

---

## 1. Verdict

⚠️ **Risqué avant ce changement — ✅ Acceptable après.**

Le code existant était propre, bien documenté et honnête sur l'endroit où la sécurité résidait réellement (les policies RLS). Ce qui le rendait risqué n'était pas du laisser-aller, c'était la **structure** : un assembly Blazor unique dans lequel des règles métier vivaient dans le balisage des composants, où chaque méthode d'accès aux données avalait un `catch (Exception)` et rapportait une panne réseau comme un refus de droits, et où rien n'était couvert par un test.

Trois défauts étaient atteignables en production :

| | Défaut | Impact |
|---|---|---|
| 1 | `NavMenu.Initials` indexait une chaîne vide | Exception non gérée, menu de navigation blanc |
| 2 | `listUsers()` non paginé dans l'Edge Function super-admin | Nomination par e-mail silencieusement en échec au-delà de la 1re page |
| 3 | Libellés vides acceptés dans la hiérarchie de lieux | Lignes inutilisables créées dans `lieu_*` |

Les trois sont corrigés, et chacun est verrouillé par un test — ou, pour l'Edge Function, par une recherche paginée explicite.

La compilation est verte avec les **avertissements traités en erreurs**, et **112 tests unitaires passent**.

---

## 2. Problèmes critiques (impact production)

### 2.1 — Toute erreur du fournisseur rapportée comme « droits insuffisants » 🔴

`ItemService`, `LieuService` et `CompteService` faisaient tous :

```csharp
catch (Exception)
{
    return AuthResult.Fail("Enregistrement refusé (droits insuffisants ou données invalides).");
}
```

**Que se passe-t-il si le réseau tombe en pleine sauvegarde ?** On dit à l'utilisateur qu'il n'a pas les droits. Il demande à un administrateur des droits qu'il possède déjà ; l'administrateur ne trouve rien d'anormal ; personne ne regarde la connectivité. L'exception n'était jamais journalisée, donc il n'y a même pas de trace à consulter. `OperationCanceledException` était capturée de la même façon : un rendu annulé signalait lui aussi une erreur de permission.

**Corrigé :** `SupabaseCallGuard` + `SupabaseFailureTranslator` classifient désormais l'échec (refusé / conflit / injoignable / inattendu), journalisent la cause non traduite une fois via `ILogger`, et relaient intacte l'annulation demandée par l'appelant.

### 2.2 — Règles d'autorisation logées dans le balisage 🔴

`GestionComptes.razor.cs` portait les règles décidant qui peut changer le rôle de qui et qui peut désactiver qui — sous forme de cinq prédicats inversés au nommage négatif (`RoleSelectDisabled`, `OptionDisabled`, `ActifToggleDisabled`, `IsElevated`, `IsCa`), mêlés à l'état d'affichage. Elles reflètent des règles appliquées en SQL : une divergence entre les deux reste invisible jusqu'au jour où un utilisateur se heurte à un refus que l'écran annonçait comme permis.

**Corrigé :** `AccountAdministrationPolicy` dans le domaine, énoncée positivement (`CanChangeRoleOf`, `CanAssignRole`, `CanChangeActivationOf`), chaque branche verrouillée par un test — y compris celles qui doivent rester refusées.

### 2.3 — Lecture d'inventaire non bornée 🟠

`GetItemsAsync` n'avait aucun `Limit`. Sur un téléphone au bord de l'eau, avec un inventaire qui grossit, l'écran se dégrade jusqu'à ne plus charger. Il n'y a pas non plus de pagination dans l'UI : le coût est payé à chaque changement de filtre.

**Corrigé :** `InventoryFilter.MaxResults` (500 par défaut, 1000 au maximum, borné plutôt que rejeté). La pagination d'écran est inscrite au reste à faire.

### 2.4 — Faille de redirection ouverte à la connexion 🟠

`Login.SafeReturnUrl` rejetait `://` et un `//` initial, mais pas `\evil` (les navigateurs normalisent l'antislash en slash), ni `javascript:` / `data:`, ni les caractères de contrôle utilisés pour faire passer un schéma.

**Corrigé :** `RelativeReturnUrl`, un *value object* qui ne peut pas contenir de valeur dangereuse — tout ce qui est suspect retombe sur la page d'accueil. 15 cas verrouillés.

### 2.5 — Libellés vides atteignant la base 🟠

`LieuService.AddSectionAsync(libelle)` appelait `libelle.Trim()` sans validation ni garde de nullité : une saisie composée d'espaces créait un lieu au libellé vide, et `null` aurait levé une exception.

**Corrigé :** `LocationLabel`, un *value object* qui ne peut être construit ni vide ni trop long. La signature du dépôt prend un `LocationLabel` : l'appel invalide ne compile plus.

### 2.6 — `NavMenu.Initials` plantait sur une adresse sans partie locale 🔴

```csharp
var local = name.Split('@')[0];          // "" pour "@club.be"
var parts = local.Split(...);            // vide
return ... : char.ToUpperInvariant(local[0]).ToString();   // IndexOutOfRangeException
```

**Corrigé et déplacé** dans `UserInitials.FromDisplayName`, défensif sur les entrées vides, uniquement ponctuées ou sans partie locale.

### 2.7 — Edge Function : recherche d'utilisateur non paginée 🔴

`nominate-super-admin` résolvait `target_email` via `asAdmin.auth.admin.listUsers()` sans pagination. Cet appel ne renvoie que la **première page** (50 utilisateurs par défaut) : nommer quelqu'un au-delà échoue en « utilisateur introuvable » — sur l'opération la plus privilégiée du système, et précisément au moment où le club a assez grandi pour que ça compte.

**Corrigé :** recherche paginée, 1000 par page, plafonnée à 50 pages, avec l'erreur de lecture distinguée du « non trouvé ».

### 2.8 — Edge Function : achèvement partiel silencieux 🟠

**Que se passe-t-il si ça casse en cours de route ?** La fonction effectue trois écritures non transactionnelles : la ligne de rôle, la recopie du claim `app_metadata`, et l'entrée d'audit. Le résultat des deux dernières n'était pas vérifié. Une élévation de privilège pouvait donc s'achever **sans être auditée**, l'appelant recevant un « tout s'est bien passé ».

**Corrigé :** les deux résultats sont vérifiés et rapportés (`claim_mirrored`, `audited`), et un achèvement partiel est journalisé. L'ordre des écritures est délibéré — la table de rôles est l'autorité que lisent les policies RLS, elle passe donc en premier ; une recopie de claim en échec signifie seulement que le JWT est en retard jusqu'au prochain rafraîchissement, d'où un rapport plutôt qu'un `500` trompeur.

L'**idempotence** était déjà correcte (une cible déjà super-admin court-circuite) et figure maintenant explicitement dans le contrat, avec en plus une vérification du format UUID de `target_user_id`.

---

## 3. Ce qui change structurellement

### 3.1 — Découpage hexagonal, vérifié par le compilateur

| Projet | Rôle | Dépend de |
|---|---|---|
| `MoaMat.Domain` | *Value objects*, invariants, règles d'habilitation d'écran, et les **ports** | *rien* |
| `MoaMat.Infrastructure` | Tout ce qui connaît Supabase : formes PostgREST, mapping, traduction d'erreurs, construction du client | Domain + `Supabase` |
| `MoaMat.Web` | PWA Blazor WASM et préoccupations propres au navigateur | Domain + Infrastructure |
| `MoaMat.UnitTests` | Tests unitaires du cœur | Domain + Infrastructure |

Un assembly `Application` séparé n'a **délibérément pas** été créé. L'orchestration est ici triviale — un écran appelle un port et affiche la réponse — donc un cinquième projet serait de la cérémonie, pas de la séparation. C'est l'arbitrage « sévérité adaptative », et il reste réversible le jour où un cas d'usage enchaînera plusieurs ports.

Les *records* du domaine ne sont **pas anémiques par accident** : ils portent le comportement qui leur revient réellement (`AppRole.IsAtLeast`, `InventoryItem.DescribeClubCodeAmbiguity`, `AccountAdministrationPolicy`). Ce qu'ils ne portent pas, c'est de la logique métier inventée — les règles du club de plongée vivent dans PostgreSQL, et modéliser par-dessus un faux agrégat côté client serait pire que des porteurs de données honnêtes.

### 3.2 — Lectures et écritures échouent différemment, exprès

- Une **lecture** en échec lève `DataAccessException`, porteuse d'un message déjà affichable tel quel. Les écrans ne capturent que ce type ; aucun écran ne référence PostgREST.
- Une **écriture** refusée renvoie un `OperationResult` en échec. Un refus est un résultat attendu sur lequel l'utilisateur doit agir, pas une condition exceptionnelle.

### 3.3 — Abstraction fuyante supprimée

`ItemService.GetChildAsync<T>() where T : BaseModel` obligeait tout consommateur de l'abstraction à connaître PostgREST — exactement le couplage que le découpage existe pour empêcher. Rien dans l'UI ne l'utilisait.

**Décision :** les six formes de spécialisation sont conservées, une classe par fichier, dans `MoaMat.Infrastructure/Supabase/Records/` (aucune connaissance du schéma n'est perdue), et **ne sont pas exposées par un port** tant qu'aucun écran de détail n'en a besoin. Le jour où cet écran arrive : ajouter des méthodes typées, ne pas ressusciter le générique. C'est signalé dans le README, section « Reste à faire ».

### 3.4 — Échec rapide sur la configuration

`Program.cs` se contentait de *journaliser* une erreur pour une clé `service_role` et continuait, et construisait volontiers un client depuis une URL vide. Désormais `SupabaseSettings.Validate()` lève sur une URL absente/malformée, une clé absente, ou une clé privilégiée. Refuser de démarrer est le bon comportement : une page blanche se rattrape, une clé de service publiée non.

`SupabaseKeyInspector` est une fonction pure, extraite de `Program.cs` et couverte par 8 tests. Un format de clé **non reconnu** passe volontairement : tout refuser casserait le jour où Supabase change un format, et le workflow de déploiement porte le second contrôle.

### 3.5 — Conventions

- **Une classe par fichier**, vérifié : aucun fichier ne déclare deux types de premier niveau, aucun type imbriqué privé ne subsiste.
- **Code en anglais** (types, membres, commentaires) ; **produit en français** (libellés d'écran, erreurs affichées, routes, identifiants SQL). La frontière est délibérée : ce qu'un membre du club lit reste en français, ce qu'un développeur lit est en anglais. Les codes de rôle (`lecture`, `gestion`, `admin`, `super-admin`) et de famille restent en français parce qu'ils sont identiques au bit près aux valeurs en base.
- `Directory.Build.props` : analyseurs actifs, `TreatWarningsAsErrors`, `NuGetAudit`.
- `Directory.Packages.props` : *Central Package Management*.
- `.editorconfig` : namespaces à portée de fichier, champs `_camelCase`, culture explicite (`CA1305`), capture large qui reste visible (`CA1031`).

### 3.6 — Supprimé

`Counter.razor`, `Weather.razor` et `wwwroot/sample-data/weather.json` — échafaudage du modèle Blazor, toujours `[Authorize]` et toujours liés depuis le menu de navigation d'une application de production.

---

## 4. Tests ajoutés

112 tests, tous verts. Choisis pour les décisions qui peuvent réellement être fausses, pas pour un pourcentage de couverture.

| Domaine | Cas |
|---|---|
| `AppRole` | tous les codes connus, tolérance casse/espaces, **inconnu → `None`** (jamais de sur-attribution), ordonnancement |
| `AccountAdministrationPolicy` | acteur non-admin, ciblage de soi-même, cible privilégiée, membre du CA, plafond d'attribution, super-admin |
| `UserInitials` | **le cas du plantage** (`@club.be`), ponctuation seule, vide, chiffres |
| `RelativeReturnUrl` | absolu, protocole-relatif, racine-relatif, antislash, `javascript:`, `data:`, `mailto:`, caractères de contrôle, trim |
| `LocationLabel` | **le cas de l'insertion vide**, longueur limite, trim |
| `InventoryFilter` | bornage sous 1 et au-delà du plafond, valeurs par défaut |
| `InventoryItem` | chaque raison d'ambiguïté, les deux ensemble, drapeau sans sous-raison, **sous-raison sans le drapeau reste silencieuse** |
| `ItemFamily` | code inconnu retombant sur lui-même, comparaison ordinale, codes distincts |
| `OperationResult` | message d'échec vide refusé, **`default` n'est pas un succès** |
| `SupabaseKeyInspector` | préfixe secret, préfixe publishable, JWT anon, JWT `service_role`, malformé, espaces |
| `SupabaseSettings` | URL vide, schéma invalide, clé absente, **une clé secrète empêche le démarrage** |
| `SqlDateConverter` | aller-retour, pas de composante horaire, pas de fuseau, nulls |

La suite tourne désormais en **barrière CI avant publication** (`.github/workflows/deploy.yml`).

`global.json` active le *runner* Microsoft.Testing.Platform — le SDK .NET 10 n'accepte plus le chemin VSTest par lequel les exécutables xUnit v3 seraient sinon pilotés.

---

## 5. Risques restants, non traités ici

1. **Pas de concurrence optimiste sur `public.item`.** `UpdateItemAsync` envoie la ligne entière ; deux personnes éditant la même bouteille, c'est du *last-write-wins* sans avertissement. Corriger demande une évolution de schéma (jeton de version, ou précondition sur `maj_le`), qui relève d'une migration de base, pas de cette refactorisation.
2. **Trimming Blazor WASM en Release contre le mapping par réflexion.** PostgREST mappe par réflexion Newtonsoft sur des propriétés annotées `[Column]`. Le trimming peut retirer des propriétés dont il ne voit pas l'usage. Pré-existant, inchangé par ce travail, et apparemment fonctionnel — mais c'est le genre de chose qui casse silencieusement à la prochaine montée de version. Mérite un `TrimmerRootDescriptor` ou un *smoke test* sur la sortie publiée qui lise réellement une ligne.
3. **Les scripts SQL et de migration gardent leurs commentaires français.** `db/*.sql` annote des identifiants français ; traduire la prose découplerait le commentaire de l'identifiant, et modifier des scripts déjà exécutés contre une base vivante présente un risque sans commune mesure avec le bénéfice. Volontairement hors périmètre — dites-le si vous voulez que ce soit fait dans un changement dédié.
4. **`Access-Control-Allow-Origin: *`** sur les Edge Functions est acceptable aujourd'hui parce que l'authentification passe par jeton porteur, jamais par cookie. Documenté dans `cors.ts`. Cela cesse d'être acceptable dès l'apparition d'`Allow-Credentials`.
5. **Pas de tests de composants** (bUnit). Les écrans sont minces maintenant que les règles en sont sorties, donc la valeur est moindre — mais le rendu de la politique par l'écran des comptes n'est pas testé.

---

## 6. Refactorisation optionnelle, seulement si elle se justifie

- **Pagination de l'écran inventaire.** La requête est bornée, donc le mode d'échec est « vous voyez silencieusement les 500 premiers » — mieux qu'avant, toujours pas juste. À faire quand l'inventaire approchera ce nombre.
- **`IReadOnlyList<T>` → `IAsyncEnumerable<T>`** pour le journal d'audit s'il dépasse un jour la taille d'un écran. Pas maintenant.

---

## 7. Vérifications effectuées

```
dotnet build MoaMat.slnx        →  0 avertissement, 0 erreur (avertissements = erreurs)
dotnet test  MoaMat.slnx        →  112 réussis, 0 échec, 0 ignoré
dotnet publish -c Release       →  succès
```

Non vérifié : le comportement à l'exécution contre un projet Supabase réel. Chaque changement d'adaptateur est une traduction mécanique d'un appel qui fonctionnait déjà, mais les écrans comptes, inventaire, lieux et journal d'audit devraient être parcourus une fois sur des données réelles avant fusion — les colonnes PostgREST renommées sont l'endroit où une coquille se cacherait, et aucun test unitaire ne peut l'attraper.
