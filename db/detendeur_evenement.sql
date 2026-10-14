-- =============================================================================
--  MoaMat — Historique des interventions d'entretien d'un ensemble détendeur.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/transform_item.sql, db/rls.sql, db/audit.sql et
--  db/item_detendeur.sql (a besoin de public.item, public.item_detendeur,
--  public.has_permission, public.audit_write). Ré-exécutable sans erreur.
--
--  PAS de reprise de l'historique legacy (public.detendeur_intervention,
--  402 lignes) dans ce fichier — voir la note « Historique legacy » en tête de
--  db/item_detendeur.sql pour la raison (espaces d'id non audités, contrairement
--  à public.bouteille_requalification). Cette table ne reçoit donc, pour
--  l'instant, que les interventions saisies APRÈS la mise en service de ce
--  moteur, via public.enregistrer_intervention_detendeur() ci-dessous.
--
--  Ce fichier pose :
--    1. public.detendeur_evenement — table d'événements (append-only côté
--       client : écriture exclusivement par la fonction SECURITY DEFINER ci-dessous).
--    2. RLS lecture (item.read).
--    3. public.enregistrer_intervention_detendeur() — idempotent sur
--       p_request_id ; met à jour public.item_detendeur.date_derniere_intervention
--       SANS JAMAIS LA FAIRE RECULER (le trigger de db/item_detendeur.sql
--       recalcule ensuite l'échéance) ; refusée sur un ensemble désactivé ou
--       en statut terminal, ou sur une date future.
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Table d'événements
-- -----------------------------------------------------------------------------

create table if not exists public.detendeur_evenement (
    id             bigint generated always as identity primary key,
    item_id        bigint not null references public.item (id) on delete cascade,
    -- Un seul type aujourd'hui : contrairement aux bouteilles (contrôle
    -- optique/hydraulique, résultat conforme/echec, cf. db/bouteille_evenement.sql),
    -- une intervention d'entretien détendeur n'a pas de notion de résultat
    -- pass/fail dans ce ticket — seule la DATE compte pour l'échéance.
    type           text not null check (type in ('intervention')),
    date_evenement date not null,
    prestataire    text,
    cout_eur       numeric(12,2) check (cout_eur is null or cout_eur >= 0),
    remarque       text,
    -- Clé d'idempotence générée par le formulaire : un rejeu (timeout réseau
    -- puis nouvel envoi) ne crée pas de doublon.
    request_id     uuid unique,
    cree_le        timestamptz not null default now(),
    cree_par       uuid default auth.uid()
);

comment on table public.detendeur_evenement is
    'Chronologie des interventions d''entretien d''un ensemble détendeur. Écritures exclusivement via public.enregistrer_intervention_detendeur(). Pas de reprise de l''historique legacy (public.detendeur_intervention) — voir db/item_detendeur.sql.';

create index if not exists ix_detendeur_evenement_item
    on public.detendeur_evenement (item_id, date_evenement desc, id desc);

-- -----------------------------------------------------------------------------
--  2. RLS — lecture seule pour les clients (aucune policy insert/update/delete).
-- -----------------------------------------------------------------------------

alter table public.detendeur_evenement enable row level security;

drop policy if exists detendeur_evenement_sel on public.detendeur_evenement;
create policy detendeur_evenement_sel on public.detendeur_evenement
    for select to authenticated
    using (public.has_permission('item.read'));

-- -----------------------------------------------------------------------------
--  3. Enregistrer une intervention depuis la fiche détendeur.
--     Met à jour le compteur de dernière intervention JAMAIS EN ARRIÈRE : un
--     enregistrement antérieur à la dernière intervention déjà connue est
--     historisé (l'événement reste consigné) mais ne fait pas régresser
--     l'échéance. Le trigger public.tg_item_detendeur_sync_echeance
--     (db/item_detendeur.sql) recalcule ensuite l'échéance.
-- -----------------------------------------------------------------------------

create or replace function public.enregistrer_intervention_detendeur(
    p_item_id     bigint,
    p_date        date,
    p_prestataire text,
    p_cout_eur    numeric,
    p_remarque    text,
    p_request_id  uuid default null)
returns bigint
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_id   bigint;
    v_cout numeric(12,2) := round(p_cout_eur, 2);
begin
    if not public.has_permission('item.update') then
        raise exception 'Droit insuffisant : permission « item.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_date is null then
        raise exception 'Date de l''intervention obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date > (now() at time zone 'Europe/Paris')::date then
        raise exception 'Date de l''intervention dans le futur : refusée.' using errcode = 'check_violation';
    end if;

    if v_cout is not null and v_cout < 0 then
        raise exception 'Coût négatif : refusé.' using errcode = 'check_violation';
    end if;

    -- Verrou sur l'ensemble : sérialise deux saisies concurrentes (le calcul
    -- « jamais en arrière » ci-dessous lit puis écrit le compteur).
    perform 1 from public.item_detendeur where item_id = p_item_id for update;
    if not found then
        raise exception 'Ensemble détendeur introuvable : %.', p_item_id using errcode = 'no_data_found';
    end if;

    -- Une intervention renouvelle l'échéance : elle n'a pas de sens sur un
    -- ensemble désactivé ou en statut terminal (rebuté, perdu…) — même règle
    -- que public.enregistrer_requalification_bouteille.
    if exists (
        select 1 from public.item i
        join public.ref_statut s on s.code = i.statut_code
        where i.id = p_item_id and (not i.actif or s.est_terminal)
    ) then
        raise exception 'Intervention refusée : ensemble désactivé ou en statut terminal.'
            using errcode = 'check_violation';
    end if;

    -- Idempotence : un rejeu (même p_request_id) renvoie l'événement déjà
    -- créé, sans toucher au compteur ni à l'audit.
    insert into public.detendeur_evenement
        (item_id, type, date_evenement, prestataire, cout_eur, remarque, request_id)
    values
        (p_item_id, 'intervention', p_date, nullif(btrim(p_prestataire), ''), v_cout,
         nullif(btrim(p_remarque), ''), p_request_id)
    on conflict (request_id) do nothing
    returning id into v_id;

    if v_id is null then
        select id into v_id from public.detendeur_evenement where request_id = p_request_id;
        return v_id;
    end if;

    update public.item_detendeur
    set date_derniere_intervention = p_date
    where item_id = p_item_id
      and (date_derniere_intervention is null or date_derniere_intervention < p_date);

    perform public.audit_write(
        'detendeur.intervention',
        'item',
        p_item_id::text,
        null,
        jsonb_build_object('evenement_id', v_id, 'date', p_date, 'cout_eur', v_cout,
                           'prestataire', nullif(btrim(p_prestataire), '')),
        '{}'::jsonb);

    return v_id;
end $$;

comment on function public.enregistrer_intervention_detendeur(bigint, date, text, numeric, text, uuid) is
    'Historise une intervention d''entretien sur un ensemble détendeur et met à jour item_detendeur.date_derniere_intervention SANS JAMAIS LA FAIRE RECULER (échéance recalculée par trigger, db/item_detendeur.sql). Refuse une date future, un coût négatif, ou un ensemble désactivé/en statut terminal. Idempotent sur p_request_id. Exige « item.update ». Journalisé (detendeur.intervention).';

revoke execute on function public.enregistrer_intervention_detendeur(bigint, date, text, numeric, text, uuid) from public;
grant execute on function public.enregistrer_intervention_detendeur(bigint, date, text, numeric, text, uuid) to authenticated;

commit;
