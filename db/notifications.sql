-- =============================================================================
--  MoaMat — Notifications push (Web Push) : nouveau compte en attente.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/roles.sql, db/permissions.sql, db/rls.sql, db/audit.sql
--  et db/comptes.sql. Ré-exécutable sans erreur.
--
--  Objectif : prévenir sur mobile / desktop les comptes habilités à VALIDER une
--  inscription dès qu'un compte « en_attente » est créé.
--
--  Destinataires : UNIQUEMENT les comptes dont le rôle détient la permission
--  « role.assign » (celle qui permet d'activer un compte en attente en lui
--  affectant un rôle — admin et super-admin aujourd'hui). Le rôle n'est jamais
--  écrit en dur : si la matrice db/permissions.sql évolue, les destinataires
--  suivent. Cette règle est appliquée à TROIS endroits :
--    * à l'abonnement   : public.enregistrer_abonnement_push() refuse un
--                         appelant sans « role.assign » ;
--    * à l'envoi        : public.destinataires_push_compte_en_attente() ne
--                         renvoie que les abonnements dont le propriétaire
--                         détient ENCORE « role.assign » et n'est pas désactivé ;
--    * à la perte du droit : public.purger_abonnements_push_sans_droit(),
--                         déclenchée quand le rôle d'un compte est modifié ou
--                         supprimé, ou quand la matrice retire « role.assign »,
--                         supprime les abonnements du compte (section 4).
--
--  Chaîne d'envoi :
--      INSERT public.utilisateur_role (role = en_attente)       [db/roles.sql]
--        └─ trigger notifier_compte_en_attente
--             └─ pg_net : POST asynchrone vers l'Edge Function
--                supabase/functions/notify-pending-account
--                  └─ Web Push (clés VAPID) vers chaque abonnement habilité
--
--  Le trigger ne peut JAMAIS faire échouer une inscription : configuration
--  absente, extension manquante ou erreur quelconque => simple WARNING. pg_net
--  met la requête en file et ne l'émet qu'après COMMIT : une inscription
--  annulée ne notifie personne.
--
--  Configuration (une fois par projet, secrets dans Supabase Vault — jamais
--  dans ce dépôt) :
--
--      select vault.create_secret(
--          'https://<ref>.supabase.co/functions/v1/notify-pending-account',
--          'moamat_notify_pending_account_url');
--      select vault.create_secret(
--          '<même valeur que le secret PUSH_WEBHOOK_SECRET de l''Edge Function>',
--          'moamat_notify_pending_account_secret');
--
--  Tant que ces deux secrets n'existent pas, aucune notification n'est émise.
-- =============================================================================

begin;

create extension if not exists pg_net with schema extensions;

-- -----------------------------------------------------------------------------
--  1. Abonnements Web Push (un par navigateur / appareil)
-- -----------------------------------------------------------------------------

create table if not exists public.abonnement_push (
    id          bigint generated always as identity primary key,
    user_id     uuid not null references auth.users (id) on delete cascade,
    endpoint    text not null unique,
    p256dh      text not null,
    auth        text not null,
    user_agent  text,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now(),
    constraint abonnement_push_endpoint_https check (endpoint like 'https://%'),
    constraint abonnement_push_longueurs check (
        length(endpoint) <= 2048 and length(p256dh) <= 256 and length(auth) <= 256
        and (user_agent is null or length(user_agent) <= 512)
    )
);

create index if not exists abonnement_push_user_idx on public.abonnement_push (user_id);

comment on table public.abonnement_push is
    'Abonnements Web Push des comptes habilités à valider les inscriptions (permission « role.assign »). Écriture uniquement via enregistrer_abonnement_push() / supprimer_abonnement_push().';
comment on column public.abonnement_push.endpoint is
    'URL du service push du navigateur (FCM, Apple, Mozilla…). Unique : un navigateur n''a qu''un abonnement, rattaché au dernier compte qui l''a activé.';

drop trigger if exists set_updated_at on public.abonnement_push;
create trigger set_updated_at
    before update on public.abonnement_push
    for each row execute function public.tg_set_updated_at();

-- RLS : chacun ne voit que ses propres abonnements. AUCUNE policy d'écriture :
-- insertion et suppression passent par les RPC ci-dessous, qui revérifient la
-- permission. service_role (Edge Function) contourne la RLS par nature.
alter table public.abonnement_push enable row level security;

drop policy if exists abonnement_push_sel on public.abonnement_push;
create policy abonnement_push_sel on public.abonnement_push
    for select to authenticated
    using (user_id = (select auth.uid()));

-- -----------------------------------------------------------------------------
--  2. Abonnement / désabonnement (appelés par le client Blazor)
-- -----------------------------------------------------------------------------

create or replace function public.enregistrer_abonnement_push(
    p_endpoint   text,
    p_p256dh     text,
    p_auth       text,
    p_user_agent text default null
)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_user uuid := (select auth.uid());
begin
    if v_user is null then
        raise exception 'Session requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if not public.has_permission('role.assign') then
        raise exception 'Notifications réservées aux comptes habilités à valider les inscriptions (permission « role.assign »).'
            using errcode = 'insufficient_privilege';
    end if;

    if coalesce(p_endpoint, '') not like 'https://%'
       or coalesce(p_p256dh, '') = ''
       or coalesce(p_auth, '') = '' then
        raise exception 'Abonnement push invalide.'
            using errcode = 'invalid_parameter_value';
    end if;

    -- Idempotent. Sur conflit, l'abonnement est RATTACHÉ à l'appelant : un
    -- endpoint identifie un navigateur, pas une personne. Si un autre compte
    -- s'est connecté auparavant sur le même appareil, c'est désormais
    -- l'appelant (habilité, vérifié ci-dessus) qui reçoit les notifications.
    insert into public.abonnement_push (user_id, endpoint, p256dh, auth, user_agent)
    values (v_user, p_endpoint, p_p256dh, p_auth, left(p_user_agent, 512))
    on conflict (endpoint) do update
        set user_id    = excluded.user_id,
            p256dh     = excluded.p256dh,
            auth       = excluded.auth,
            user_agent = excluded.user_agent;
end $$;

comment on function public.enregistrer_abonnement_push(text, text, text, text) is
    'Enregistre (ou rattache à l''appelant) l''abonnement Web Push du navigateur courant. Exige « role.assign ». Idempotent.';

revoke execute on function public.enregistrer_abonnement_push(text, text, text, text) from public, anon;
grant execute on function public.enregistrer_abonnement_push(text, text, text, text) to authenticated;

create or replace function public.supprimer_abonnement_push(p_endpoint text)
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
    -- Aucune permission exigée : chacun peut toujours retirer SES abonnements
    -- (y compris un compte qui vient de perdre le droit). Idempotent.
    delete from public.abonnement_push
    where endpoint = p_endpoint
      and user_id = (select auth.uid());
end $$;

comment on function public.supprimer_abonnement_push(text) is
    'Supprime l''abonnement Web Push du navigateur courant s''il appartient à l''appelant. Idempotent.';

revoke execute on function public.supprimer_abonnement_push(text) from public, anon;
grant execute on function public.supprimer_abonnement_push(text) to authenticated;

-- -----------------------------------------------------------------------------
--  3. Destinataires (lus par l'Edge Function, service_role uniquement)
-- -----------------------------------------------------------------------------

create or replace function public.destinataires_push_compte_en_attente()
returns table (endpoint text, p256dh text, auth text)
language sql
stable
security definer
set search_path = ''
as $$
    select a.endpoint, a.p256dh, a.auth
    from public.abonnement_push a
    join public.utilisateur_role ur on ur.user_id = a.user_id
    join public.role_permission rp  on rp.role = ur.role
                                   and rp.permission_code = 'role.assign'
    join auth.users u               on u.id = a.user_id
    where u.banned_until is null or u.banned_until <= now()
$$;

comment on function public.destinataires_push_compte_en_attente() is
    'Abonnements push dont le propriétaire détient ENCORE « role.assign » et n''est pas désactivé. Réservée à service_role (Edge Function notify-pending-account).';

revoke execute on function public.destinataires_push_compte_en_attente() from public, anon, authenticated;
grant execute on function public.destinataires_push_compte_en_attente() to service_role;

-- -----------------------------------------------------------------------------
--  4. Purge à la perte du droit
--     SECURITY DEFINER : l'admin qui rétrograde un compte ne voit pas (RLS) les
--     abonnements de ce compte. La fonction d'envoi filtre déjà ; cette purge
--     supprime réellement les abonnements dès que le droit disparaît, quelle
--     qu'en soit la cause :
--       * rôle modifié (écran /comptes, SQL, Edge Function) ;
--       * ligne public.utilisateur_role supprimée (compte révoqué) ;
--       * « role.assign » retiré d'un rôle dans la matrice public.role_permission
--         (DELETE / UPDATE : trigger différé ; TRUNCATE + réinsertion de
--         db/permissions.sql : appel explicite en fin de ce script).
--     Le client (PushNotificationService) désabonne ensuite le navigateur dont
--     l'abonnement n'existe plus en base : rien n'est réactivé en silence si le
--     compte retrouve le droit plus tard.
-- -----------------------------------------------------------------------------

create or replace function public.purger_abonnements_push_sans_droit(p_user uuid default null)
returns void
language sql
security definer
set search_path = ''
as $$
    delete from public.abonnement_push a
    where (p_user is null or a.user_id = p_user)
      and not exists (
          select 1
          from public.utilisateur_role ur
          join public.role_permission rp on rp.role = ur.role
                                        and rp.permission_code = 'role.assign'
          where ur.user_id = a.user_id
      )
$$;

comment on function public.purger_abonnements_push_sans_droit(uuid) is
    'Supprime les abonnements push des comptes (de p_user seul, ou de tous si NULL) qui ne détiennent plus « role.assign ». Interne : appelée par triggers et scripts, jamais par le client.';

revoke execute on function public.purger_abonnements_push_sans_droit(uuid) from public, anon, authenticated;

-- 4.1 Rôle d'un compte modifié ou supprimé.
create or replace function public.tg_purge_abonnement_push_sans_droit()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    perform public.purger_abonnements_push_sans_droit(old.user_id);
    if tg_op = 'UPDATE' and new.user_id is distinct from old.user_id then
        perform public.purger_abonnements_push_sans_droit(new.user_id);
    end if;
    return null;
end $$;

comment on function public.tg_purge_abonnement_push_sans_droit() is
    'Supprime les abonnements push d''un compte dont le rôle est modifié ou supprimé et qui ne détient plus « role.assign ».';

drop trigger if exists purge_abonnement_push_sans_droit on public.utilisateur_role;
create trigger purge_abonnement_push_sans_droit
    after update of role, user_id or delete on public.utilisateur_role
    for each row
    execute function public.tg_purge_abonnement_push_sans_droit();

-- 4.2 « role.assign » retiré d'un rôle dans la matrice. Trigger DIFFÉRÉ au
--     COMMIT : une transaction qui retire puis remet la permission ne purge
--     rien à tort.
create or replace function public.tg_purge_abonnement_push_matrice()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    perform public.purger_abonnements_push_sans_droit(null);
    return null;
end $$;

comment on function public.tg_purge_abonnement_push_matrice() is
    'Supprime, au COMMIT, les abonnements push des comptes dont le rôle a perdu « role.assign » dans public.role_permission.';

drop trigger if exists purge_abonnement_push_matrice on public.role_permission;
create constraint trigger purge_abonnement_push_matrice
    after delete or update on public.role_permission
    deferrable initially deferred
    for each row
    when (old.permission_code = 'role.assign')
    execute function public.tg_purge_abonnement_push_matrice();

-- 4.3 Rattrapage : abonnements devenus sans droit avant ce script, ou par un
--     TRUNCATE de la matrice (db/permissions.sql), que les triggers ne voient pas.
select public.purger_abonnements_push_sans_droit(null);

-- -----------------------------------------------------------------------------
--  5. Déclenchement à la création d'un compte en attente
-- -----------------------------------------------------------------------------

create or replace function public.tg_notifier_compte_en_attente()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_url    text;
    v_secret text;
begin
    select ds.decrypted_secret into v_url
    from vault.decrypted_secrets ds
    where ds.name = 'moamat_notify_pending_account_url';

    select ds.decrypted_secret into v_secret
    from vault.decrypted_secrets ds
    where ds.name = 'moamat_notify_pending_account_secret';

    if v_url is null or v_secret is null then
        -- Notifications non configurées sur ce projet : rien à faire.
        return new;
    end if;

    -- Seul l'identifiant part : l'Edge Function relit le compte avec la clé
    -- service_role, rien de ce corps n'est pris pour argent comptant.
    perform net.http_post(
        url                  := v_url,
        body                 := jsonb_build_object('user_id', new.user_id),
        headers              := jsonb_build_object(
                                    'Content-Type', 'application/json',
                                    'x-moamat-webhook-secret', v_secret),
        timeout_milliseconds := 5000
    );

    return new;
exception
    when others then
        -- Une notification ne doit JAMAIS bloquer une inscription.
        raise warning 'Notification « compte en attente » non émise pour % : %', new.user_id, sqlerrm;
        return new;
end $$;

comment on function public.tg_notifier_compte_en_attente() is
    'Appelle (pg_net, asynchrone) l''Edge Function notify-pending-account à la création d''un compte « en_attente ». Ne fait jamais échouer l''inscription.';

drop trigger if exists notifier_compte_en_attente on public.utilisateur_role;
create trigger notifier_compte_en_attente
    after insert on public.utilisateur_role
    for each row
    when (new.role = 'en_attente')
    execute function public.tg_notifier_compte_en_attente();

commit;
