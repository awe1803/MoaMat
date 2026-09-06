-- =============================================================================
--  MoaMat — Journal d'audit : table append-only + triggers sur tables sensibles.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/roles.sql, db/permissions.sql et db/rls.sql.
--  Ré-exécutable sans erreur.
--
--  public.audit_log est APPEND-ONLY :
--    * aucune policy INSERT / UPDATE / DELETE => les clients (anon,
--      authenticated) ne peuvent pas écrire directement ;
--    * l'alimentation se fait exclusivement par des triggers SECURITY DEFINER ;
--    * deux triggers BEFORE UPDATE / BEFORE DELETE lèvent une exception, y
--      compris pour le propriétaire de la table et pour service_role : aucune
--      modification ni suppression applicative n'est possible.
--
--  Consultation : réservée à la permission « audit.read » (admin + super-admin).
--
--  Événements tracés :
--    * changements de rôle           -> trigger sur public.utilisateur_role
--    * changements de statut terminal -> triggers sur bouteille (déclassement),
--      detendeur / gilet / petit_materiel / materiel_didactique (est_declasse),
--      pret (clôture).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Table
-- -----------------------------------------------------------------------------

create table if not exists public.audit_log (
    id           bigint generated always as identity primary key,
    occurred_at  timestamptz not null default now(),
    actor_id     uuid,               -- auth.uid() de l'auteur (NULL si tâche système)
    actor_email  text,
    action       text not null,      -- 'role.assigned' | 'role.changed' | 'role.revoked' | 'status.terminal'
    entity_table text not null,
    entity_id    text,
    before       jsonb,
    after        jsonb,
    context      jsonb
);

create index if not exists ix_audit_log_occurred_at  on public.audit_log (occurred_at desc);
create index if not exists ix_audit_log_action       on public.audit_log (action);
create index if not exists ix_audit_log_entity       on public.audit_log (entity_table, entity_id);

comment on table public.audit_log is 'Journal d''audit append-only. Écriture par triggers SECURITY DEFINER uniquement ; ni UPDATE ni DELETE (triggers de blocage). Lecture : permission audit.read.';

-- -----------------------------------------------------------------------------
--  2. Append-only : blocage des UPDATE / DELETE
-- -----------------------------------------------------------------------------

create or replace function public.tg_audit_append_only()
returns trigger
language plpgsql
as $$
begin
    raise exception 'public.audit_log est append-only : % interdit.', tg_op
        using errcode = 'insufficient_privilege';
end $$;

drop trigger if exists audit_log_no_update on public.audit_log;
create trigger audit_log_no_update
    before update on public.audit_log
    for each row execute function public.tg_audit_append_only();

drop trigger if exists audit_log_no_delete on public.audit_log;
create trigger audit_log_no_delete
    before delete on public.audit_log
    for each row execute function public.tg_audit_append_only();

-- -----------------------------------------------------------------------------
--  3. RLS — lecture seule, réservée à audit.read
-- -----------------------------------------------------------------------------

alter table public.audit_log enable row level security;

drop policy if exists audit_log_sel on public.audit_log;
create policy audit_log_sel on public.audit_log
    for select to authenticated
    using (public.has_permission('audit.read'));

-- (Volontairement AUCUNE policy insert/update/delete.)

-- -----------------------------------------------------------------------------
--  4. Helpers d'écriture
-- -----------------------------------------------------------------------------

-- E-mail de l'appelant, extrait du JWT si présent (sinon NULL).
create or replace function public.moamat_actor_email()
returns text
language sql
stable
set search_path = ''
as $$
    select nullif(
        coalesce(
            nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'email',
            ''
        ),
        ''
    )
$$;

create or replace function public.audit_write(
    p_action       text,
    p_entity_table text,
    p_entity_id    text,
    p_before       jsonb,
    p_after        jsonb,
    p_context      jsonb default '{}'::jsonb
)
returns void
language sql
security definer
set search_path = ''
as $$
    insert into public.audit_log (actor_id, actor_email, action, entity_table, entity_id, before, after, context)
    values (
        (select auth.uid()),
        public.moamat_actor_email(),
        p_action,
        p_entity_table,
        p_entity_id,
        p_before,
        p_after,
        coalesce(p_context, '{}'::jsonb)
    )
$$;

comment on function public.audit_write(text, text, text, jsonb, jsonb, jsonb) is 'Insère une entrée dans public.audit_log. SECURITY DEFINER : contourne l''absence de policy INSERT.';

-- -----------------------------------------------------------------------------
--  5. Trigger : changements de rôle
-- -----------------------------------------------------------------------------

create or replace function public.tg_audit_role_change()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    perform public.audit_write(
        case tg_op
            when 'INSERT' then 'role.assigned'
            when 'UPDATE' then 'role.changed'
            else 'role.revoked'
        end,
        'utilisateur_role',
        coalesce(new.user_id, old.user_id)::text,
        case when tg_op <> 'INSERT' then jsonb_build_object('role', old.role) end,
        case when tg_op <> 'DELETE' then jsonb_build_object('role', new.role) end,
        jsonb_build_object('op', tg_op)
    );
    return null;
end $$;

drop trigger if exists audit_role_change on public.utilisateur_role;
create trigger audit_role_change
    after insert or update or delete on public.utilisateur_role
    for each row execute function public.tg_audit_role_change();

-- -----------------------------------------------------------------------------
--  6. Trigger générique : changement de statut terminal
--     TG_ARGV[0] = nom de la colonne booléenne / date à surveiller.
-- -----------------------------------------------------------------------------

create or replace function public.tg_audit_terminal_status()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_col     text := tg_argv[0];
    v_old     jsonb := to_jsonb(old) -> v_col;
    v_new     jsonb := to_jsonb(new) -> v_col;
begin
    if v_old is distinct from v_new then
        perform public.audit_write(
            'status.terminal',
            tg_table_name,
            (to_jsonb(new) ->> 'id'),
            jsonb_build_object(v_col, v_old),
            jsonb_build_object(v_col, v_new),
            jsonb_build_object('op', tg_op, 'column', v_col)
        );
    end if;
    return null;
end $$;

drop trigger if exists audit_terminal_status on public.bouteille;
create trigger audit_terminal_status
    after update on public.bouteille
    for each row execute function public.tg_audit_terminal_status('est_declassee');

drop trigger if exists audit_terminal_status on public.detendeur;
create trigger audit_terminal_status
    after update on public.detendeur
    for each row execute function public.tg_audit_terminal_status('est_declasse');

drop trigger if exists audit_terminal_status on public.gilet;
create trigger audit_terminal_status
    after update on public.gilet
    for each row execute function public.tg_audit_terminal_status('est_declasse');

drop trigger if exists audit_terminal_status on public.petit_materiel;
create trigger audit_terminal_status
    after update on public.petit_materiel
    for each row execute function public.tg_audit_terminal_status('est_declasse');

drop trigger if exists audit_terminal_status on public.materiel_didactique;
create trigger audit_terminal_status
    after update on public.materiel_didactique
    for each row execute function public.tg_audit_terminal_status('est_declasse');

drop trigger if exists audit_terminal_status on public.pret;
create trigger audit_terminal_status
    after update on public.pret
    for each row execute function public.tg_audit_terminal_status('est_cloture');

commit;
