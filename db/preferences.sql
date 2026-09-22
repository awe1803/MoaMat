-- =============================================================================
--  MoaMat — Préférences d'affichage rattachées au compte.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/roles.sql (fonction public.tg_set_updated_at) et
--  db/rls.sql. Ré-exécutable sans erreur.
--
--  Objectif : retenir DÉFINITIVEMENT les choix d'affichage d'un membre, là où
--  le stockage du navigateur ne suffit pas — il disparaît avec les données du
--  site, ne franchit pas un changement d'appareil et n'existe pas en navigation
--  privée. Aujourd'hui une seule préférence : « ne plus afficher l'invitation à
--  installer l'application ».
--
--  La préférence est portée par le COMPTE, pas par l'appareil : une case cochée
--  une fois vaut pour tous les navigateurs du membre, ce qui est précisément ce
--  qu'attend quelqu'un qui a répondu « ne plus jamais afficher ».
--
--  Aucune permission n'est exigée : chacun n'écrit que sa propre ligne, et rien
--  ici ne conditionne un accès aux données du club.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Table (une ligne par compte, créée à la première préférence enregistrée)
-- -----------------------------------------------------------------------------

create table if not exists public.preference_utilisateur (
    user_id                     uuid primary key references auth.users (id) on delete cascade,
    invite_installation_masquee boolean not null default false,
    created_at                  timestamptz not null default now(),
    updated_at                  timestamptz not null default now()
);

comment on table public.preference_utilisateur is
    'Préférences d''affichage d''un compte. Écriture uniquement via definir_invite_installation_masquee() ; lecture limitée à ses propres lignes.';
comment on column public.preference_utilisateur.invite_installation_masquee is
    'Vrai quand le membre a coché « ne plus afficher ce message » sur l''invitation à installer la PWA. Définitif : aucune purge, aucune expiration.';

drop trigger if exists set_updated_at on public.preference_utilisateur;
create trigger set_updated_at
    before update on public.preference_utilisateur
    for each row execute function public.tg_set_updated_at();

-- RLS : chacun ne lit que sa ligne. AUCUNE policy d'écriture — l'écriture passe
-- par la RPC ci-dessous, qui impose user_id = auth.uid() sans que le client ait
-- à l'envoyer.
alter table public.preference_utilisateur enable row level security;

drop policy if exists preference_utilisateur_sel on public.preference_utilisateur;
create policy preference_utilisateur_sel on public.preference_utilisateur
    for select to authenticated
    using (user_id = (select auth.uid()));

-- -----------------------------------------------------------------------------
--  2. Écriture (appelée par le client Blazor)
-- -----------------------------------------------------------------------------

create or replace function public.definir_invite_installation_masquee(p_masquee boolean)
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

    -- Idempotent : rejouer le même choix converge sur le même état. Décocher la
    -- case remet la valeur à false, l'invitation revient à l'ouverture suivante.
    insert into public.preference_utilisateur (user_id, invite_installation_masquee)
    values (v_user, coalesce(p_masquee, false))
    on conflict (user_id) do update
        set invite_installation_masquee = excluded.invite_installation_masquee;
end $$;

comment on function public.definir_invite_installation_masquee(boolean) is
    'Enregistre, pour l''appelant, le choix « ne plus afficher l''invitation à installer l''application ». Idempotent.';

revoke execute on function public.definir_invite_installation_masquee(boolean) from public, anon;
grant execute on function public.definir_invite_installation_masquee(boolean) to authenticated;

commit;
