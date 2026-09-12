-- =============================================================================
--  MoaMat — Machine à états de l'item : transitions de statut, historique
--  décisionnaire, verrouillage des statuts terminaux.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql, db/transform_item.sql, db/rls.sql et
--  db/audit.sql (a besoin de public.has_permission, de public.item /
--  public.ref_statut, des policies génériques posées par db/rls.sql — qu'il
--  complète pour la seule table public.item — et de public.audit_write /
--  public.moamat_actor_email). Ré-exécutable sans erreur.
--
--  Fusion des tickets « machine à états » + « calcul de disponibilité » +
--  « historique décisionnaire ». Principe directeur (A16) : le statut — et la
--  disponibilité qui en découle — est CALCULÉ, jamais saisi. Aucune case à
--  cocher manuelle de disponibilité n'existe nulle part dans le modèle
--  (db/model_item.sql §7bis : public.item_est_disponible()).
--
--  Ce fichier pose :
--    1. public.item_transition   — historique append-only des transitions de
--       statut (motif, date d'effet, autorité décisionnaire, pièce jointe).
--    2. Le trigger public.tg_item_valider_transition_statut, qui, à CHAQUE
--       changement de public.item.statut_code :
--         - exige un motif ET une date d'effet ;
--         - exige une pièce jointe justificative pour Perdu / Volé ;
--         - refuse tout retour depuis un statut terminal (Retiré du service /
--           Perdu / Volé) sans la permission « status.terminal.override »
--           (R7bis.1) ;
--         - exige l'autorité décisionnaire pour toute transition VERS un
--           statut terminal ;
--         - journalise la transition dans public.item_transition.
--    3. Le verrouillage RLS symétrique : la policy générique « item_upd »
--       posée par db/rls.sql est remplacée par une version qui, en plus de
--       « item.update », exige « status.terminal.override » dès lors que la
--       ligne ciblée est actuellement dans un statut terminal — cf. exigence
--       du ticket : « imposée à la fois par policy RLS et par trigger ».
--       (Le changement de statut terminal -> terminal audité en détail par
--       audit.sql reste inchangé et continue de s'appliquer en parallèle.)
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Historique décisionnaire des transitions de statut
--     APPEND-ONLY, comme public.audit_log (db/audit.sql) : aucune policy
--     insert/update/delete pour les clients ; alimentation exclusive par le
--     trigger SECURITY DEFINER ci-dessous ; UPDATE / DELETE bloqués même pour
--     le propriétaire de la table.
-- -----------------------------------------------------------------------------

create table if not exists public.item_transition (
    id                bigint generated always as identity primary key,
    item_id           bigint not null references public.item (id) on delete cascade,
    ancien_statut     text references public.ref_statut (code),
    nouveau_statut    text not null references public.ref_statut (code),
    motif             text not null,
    date_effet        date not null,
    -- Organisme de contrôle / CA / Gestionnaire matériel — obligatoire pour
    -- toute transition VERS un statut terminal (cf. trigger ci-dessous) ;
    -- NULL pour une transition non terminale.
    autorite_decision text check (autorite_decision is null
                                   or autorite_decision in ('organisme_controle', 'ca', 'gestionnaire_materiel')),
    -- Chemin Supabase Storage de la pièce justificative (obligatoire pour
    -- Perdu / Volé) — pas de bucket dédié ici : réutilise le même modèle que
    -- db/storage.sql (chemin texte, résolution/permissions côté bucket).
    piece_jointe_url  text,
    decide_par        uuid,
    decide_par_email  text,
    cree_le           timestamptz not null default now()
);

comment on table public.item_transition is
    'Historique append-only des transitions de statut d''un item : motif, date d''effet, autorité décisionnaire, pièce jointe. Alimenté uniquement par public.tg_item_valider_transition_statut.';

create index if not exists ix_item_transition_item      on public.item_transition (item_id, cree_le desc);
create index if not exists ix_item_transition_nouveau    on public.item_transition (nouveau_statut);

create or replace function public.tg_item_transition_append_only()
returns trigger
language plpgsql
as $$
begin
    raise exception 'public.item_transition est append-only : % interdit.', tg_op
        using errcode = 'insufficient_privilege';
end $$;

drop trigger if exists item_transition_no_update on public.item_transition;
create trigger item_transition_no_update
    before update on public.item_transition
    for each row execute function public.tg_item_transition_append_only();

drop trigger if exists item_transition_no_delete on public.item_transition;
create trigger item_transition_no_delete
    before delete on public.item_transition
    for each row execute function public.tg_item_transition_append_only();

alter table public.item_transition enable row level security;

drop policy if exists item_transition_sel on public.item_transition;
create policy item_transition_sel on public.item_transition
    for select to authenticated
    using (public.has_permission('item.read'));

-- (Volontairement AUCUNE policy insert/update/delete : seul le trigger
--  SECURITY DEFINER ci-dessous peut écrire.)

-- Vue de lecture pratique (libellés de statut résolus).
drop view if exists public.v_item_transition;
create view public.v_item_transition
    with (security_invoker = true) as
    select
        t.id,
        t.item_id,
        t.ancien_statut,
        sa.libelle as ancien_statut_libelle,
        t.nouveau_statut,
        sn.libelle as nouveau_statut_libelle,
        t.motif,
        t.date_effet,
        t.autorite_decision,
        t.piece_jointe_url,
        t.decide_par,
        t.decide_par_email,
        t.cree_le
    from public.item_transition t
    left join public.ref_statut sa on sa.code = t.ancien_statut
    join      public.ref_statut sn on sn.code = t.nouveau_statut;

comment on view public.v_item_transition is
    'Historique des transitions de statut, libellés résolus. security_invoker : RLS de item_transition appliquée.';

grant select on public.v_item_transition to authenticated;

-- -----------------------------------------------------------------------------
--  2. Validation + journalisation de toute transition de statut
--
--     Les paramètres de la transition (motif, date d'effet, autorité, pièce
--     jointe) voyagent dans le MÊME UPDATE que statut_code, portés par les
--     colonnes transitoires public.item.statut_motif / statut_date_effet /
--     statut_autorite / statut_piece_jointe_url (db/model_item.sql). Le
--     trigger les valide, les recopie dans public.item_transition, puis les
--     remet à NULL : ce ne sont pas des champs d'état durable.
-- -----------------------------------------------------------------------------

create or replace function public.tg_item_valider_transition_statut()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_old_terminal boolean;
    v_new_terminal boolean;
begin
    if new.statut_code is not distinct from old.statut_code then
        return new;
    end if;

    if new.statut_motif is null or btrim(new.statut_motif) = '' then
        raise exception 'Changement de statut refusé : motif obligatoire.'
            using errcode = 'check_violation';
    end if;

    if new.statut_date_effet is null then
        raise exception 'Changement de statut refusé : date d''effet obligatoire.'
            using errcode = 'check_violation';
    end if;

    select est_terminal into v_old_terminal from public.ref_statut where code = old.statut_code;
    select est_terminal into v_new_terminal from public.ref_statut where code = new.statut_code;

    if coalesce(v_old_terminal, false) and not public.has_permission('status.terminal.override') then
        raise exception 'Statut terminal irréversible : permission status.terminal.override requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if new.statut_code in ('perdu', 'vole') and new.statut_piece_jointe_url is null then
        raise exception 'Changement de statut refusé : pièce jointe justificative obligatoire pour Perdu/Volé.'
            using errcode = 'check_violation';
    end if;

    if coalesce(v_new_terminal, false) and new.statut_autorite is null then
        raise exception 'Changement de statut refusé : autorité décisionnaire obligatoire pour un statut terminal.'
            using errcode = 'check_violation';
    end if;

    insert into public.item_transition (
        item_id, ancien_statut, nouveau_statut, motif, date_effet,
        autorite_decision, piece_jointe_url, decide_par, decide_par_email)
    values (
        new.id, old.statut_code, new.statut_code, new.statut_motif, new.statut_date_effet,
        new.statut_autorite, new.statut_piece_jointe_url,
        (select auth.uid()), public.moamat_actor_email());

    -- Colonnes transitoires : remises à NULL, la transition est actée.
    new.statut_motif := null;
    new.statut_date_effet := null;
    new.statut_piece_jointe_url := null;
    new.statut_autorite := null;

    return new;
end $$;

comment on function public.tg_item_valider_transition_statut() is
    'BEFORE UPDATE OF statut_code sur public.item : motif/date obligatoires, pièce jointe Perte/Vol, irréversibilité des statuts terminaux (sauf status.terminal.override), autorité décisionnaire obligatoire vers un statut terminal, journalisation dans public.item_transition.';

drop trigger if exists item_valider_transition_statut on public.item;
create trigger item_valider_transition_statut
    before update of statut_code on public.item
    for each row execute function public.tg_item_valider_transition_statut();

-- -----------------------------------------------------------------------------
--  3. RLS — verrouillage symétrique des statuts terminaux
--     Remplace la policy générique « item_upd » (db/rls.sql) pour la seule
--     table public.item : en plus de « item.update », toute UPDATE sur une
--     ligne DÉJÀ dans un statut terminal exige « status.terminal.override ».
--     Entrer dans un statut terminal reste une transition normale (gestion+),
--     seul le retour / la modification APRÈS coup est verrouillé — cf.
--     ticket : « États terminaux irréversibles, sauf action explicite
--     Super-admin/Admin ».
-- -----------------------------------------------------------------------------

drop policy if exists item_upd on public.item;
create policy item_upd on public.item
    for update to authenticated
    using (
        public.has_permission('item.update')
        and (
            not coalesce((select r.est_terminal from public.ref_statut r where r.code = statut_code), false)
            or public.has_permission('status.terminal.override')
        )
    )
    with check (public.has_permission('item.update'));

commit;
