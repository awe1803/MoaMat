# Edge Functions — MoaMat

Convention et procédure de déploiement des fonctions serverless Supabase
(Deno / TypeScript).

## Arborescence

```
supabase/
  config.toml                     Config CLI (pas de secret)
  functions/
    _shared/                      Code partagé (préfixe « _ » = non déployé seul)
      cors.ts
    .env.example                  Modèle de variables locales (jamais de secret commité)
    deno.json                     Lint / format / import map
    <nom-fonction>/
      index.ts                    Point d'entrée (Deno.serve)
    nominate-super-admin/         Fonction de référence (voir plus bas)
      index.ts
    notify-pending-account/       Notification push « nouveau compte en attente »
      index.ts
```

Règles :

- **un dossier = une fonction**, `index.ts` comme point d'entrée ;
- le code commun va dans `_shared/` (les dossiers préfixés `_` ne sont pas
  déployables individuellement) ;
- nom de dossier en `kebab-case`, verbe d'action (`nominate-super-admin`,
  `send-monthly-report`…) ;
- **aucun secret dans le dépôt** : les clés (`SUPABASE_SERVICE_ROLE_KEY`…) sont
  injectées par la plateforme en production, ou lues depuis `.env.local`
  (git-ignoré) en local ;
- toute fonction appelée par le navigateur gère le préflight `OPTIONS` (CORS)
  — cf. `_shared/cors.ts`. Exception : `notify-pending-account`, appelée
  uniquement par la base, est autonome (un seul fichier, collable dans
  l'éditeur du Dashboard) ;
- une fonction privilégiée **vérifie elle-même l'appelant** à partir du JWT
  (`Authorization: Bearer …`) avant d'utiliser la clé `service_role`.

## Prérequis

- [Supabase CLI](https://supabase.com/docs/guides/cli) installé
  (`scoop install supabase` / `brew install supabase/tap/supabase` /
  `npm i -g supabase`) ;
- [Deno](https://deno.com/) pour l'édition/lint local (optionnel) ;
- être authentifié : `supabase login` ;
- projet lié : `supabase link --project-ref <ref>` (référence dans
  Dashboard → Project Settings → General).

## Développement local

```bash
# Lancer la stack locale (Postgres + Auth + Edge runtime)
supabase start

# Servir les fonctions avec rechargement à chaud
supabase functions serve --env-file supabase/functions/.env.local

# Appel de test
curl -i --request POST 'http://127.0.0.1:54321/functions/v1/nominate-super-admin' \
  --header "Authorization: Bearer <JWT d'un utilisateur>" \
  --header 'Content-Type: application/json' \
  --data '{ "target_email": "membre@royalmoana.be" }'
```

## Déploiement

```bash
# 1. (une seule fois) définir les secrets propres aux fonctions, s'il y en a.
#    Les 3 variables SUPABASE_* sont déjà fournies par la plateforme :
#    ne rien pousser ici sauf besoin spécifique.
supabase secrets list
# supabase secrets set MA_CLE=... 

# 2. Déployer une fonction
supabase functions deploy nominate-super-admin

# 3. Déployer toutes les fonctions
supabase functions deploy

# 4. Vérifier
supabase functions list
```

Le hook d'authentification `custom_access_token_hook` (défini en SQL dans
`db/roles.sql`) doit être activé **une fois** côté hébergé :
Dashboard → Authentication → Hooks → *Custom Access Token* →
`public.custom_access_token_hook`.

## Fonction de référence — `nominate-super-admin`

Nomme ou transfère le siège de **Super-admin**, vacant à l'initialisation et
jamais attribué par défaut ni codé en dur.

| Aspect | Comportement |
|--------|--------------|
| Méthode | `POST` (préflight `OPTIONS` géré) |
| Auth appelant | JWT obligatoire ; rôle lu dans `public.utilisateur_role` |
| Siège vacant | un `admin` **ou** un `super-admin` peut faire la 1re nomination |
| Siège occupé | seul un `super-admin` peut nommer / transférer |
| Entrée | `{ "target_user_id": "<uuid>" }` ou `{ "target_email": "…" }` (+ `reason` optionnel) |
| Effet | upsert `public.utilisateur_role` → `super-admin` ; miroir `app_metadata.role` ; entrée `public.audit_log` (`superadmin.nominated`) |
| Sortie | `{ ok, target_user_id, previous_role, seat_was_vacant }` |

Amorçage recommandé du **tout premier** super-admin : la requête SQL
documentée en bas de `db/roles.sql` (exécutée par la personne qui administre
le projet Supabase). La fonction sert ensuite aux nominations suivantes.

## `notify-pending-account` — notifications push des inscriptions

Envoie une notification Web Push (mobile et desktop) aux comptes **habilités à
valider une inscription** (permission `role.assign`, soit admin et super-admin)
dès qu'un compte `en_attente` est créé.

| Aspect | Comportement |
|--------|--------------|
| Appelant | la base : trigger `notifier_compte_en_attente` (`db/notifications.sql`) via `pg_net`, **sans JWT** (`verify_jwt = false`) |
| Auth appelant | en-tête `x-moamat-webhook-secret` = secret `PUSH_WEBHOOK_SECRET` (comparaison à temps constant) |
| Entrée | `{ "user_id": "<uuid>" }`, relu avec `service_role` ; rien n'est envoyé si le compte n'est plus `en_attente` |
| Destinataires | RPC `destinataires_push_compte_en_attente()` : abonnements des comptes détenant **encore** `role.assign` et non désactivés |
| Effet | notification « Nouveau compte en attente » ; un clic ouvre `/comptes?role=en_attente` ; abonnements expirés (404 / 410) supprimés |
| Sortie | `{ ok, sent, expired, failed }` ou `{ ok, skipped }` |

### Mise en service (une fois par projet)

```bash
# 1. Clés VAPID (la paire sert à signer les envois)
npx web-push generate-vapid-keys

# 2. Secrets de la fonction (conserver WEBHOOK_SECRET : il va aussi dans Vault, étape 4)
WEBHOOK_SECRET="$(openssl rand -hex 32)"; echo "$WEBHOOK_SECRET"
supabase secrets set \
  PUSH_WEBHOOK_SECRET="$WEBHOOK_SECRET" \
  VAPID_PUBLIC_KEY=<publique> \
  VAPID_PRIVATE_KEY=<privée> \
  VAPID_SUBJECT=mailto:<adresse de contact>

# 3. Déploiement
supabase functions deploy notify-pending-account
```

4. Exécuter `db/notifications.sql`, puis enregistrer dans Vault l'URL de la
   fonction et **le même** secret que `PUSH_WEBHOOK_SECRET` (requêtes en tête
   du fichier SQL). Sans ces deux secrets Vault, aucune notification ne part.
5. Côté client, renseigner la **clé publique** uniquement :
   Variable de dépôt GitHub `PUSH_VAPID_PUBLIC_KEY` (déploiement) ou
   `Push:VapidPublicKey` dans `wwwroot/appsettings.json` (local). Vide = bouton
   masqué.
6. Chaque admin / super-admin active les notifications **sur chacun de ses
   appareils** via le bouton « Me notifier des nouvelles inscriptions » de
   l'écran `/comptes`. Sur iPhone / iPad (iOS 16.4+), l'app doit d'abord être
   ajoutée à l'écran d'accueil et ouverte depuis l'icône.

La déconnexion retire l'abonnement de l'appareil. Perdre `role.assign` (rôle
modifié ou supprimé, ou permission retirée du rôle dans la matrice) supprime
tous les abonnements du compte ; s'il retrouve le droit, il doit réactiver les
notifications sur chaque appareil.
