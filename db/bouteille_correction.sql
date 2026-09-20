-- =============================================================================
--  MoaMat — Correction d'une fiche bouteille par le super-admin.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  Pré-requis : db/item_etat.sql, db/item_bouteille.sql, db/bouteille_evenement.sql.
--  Idempotent (create or replace).
--
--  Le super-admin peut rectifier ce que la saisie ou la reprise Access a mal
--  renseigné : type de gaz (famille d'usage), matière, filetage, double sortie,
--  statut, dates des derniers contrôles optique / hydraulique. Contrairement à une requalification,
--  une correction :
--    - peut FAIRE RECULER un compteur de contrôle (c'est une rectification, pas
--      un nouveau contrôle) et n'ajoute aucun événement à la chronologie ;
--    - exige un motif et est journalisée avec l'avant / après (audit_log) ;
--    - passe par le trigger de transition pour le statut (item_transition,
--      irréversibilité des statuts terminaux levée par status.terminal.override).
--  Les valeurs passées sont l'ÉTAT CIBLE complet : l'appelant renvoie les
--  valeurs actuelles pour les champs qu'il ne change pas (NULL = inconnu ;
--  p_statut NULL = statut inchangé).
-- =============================================================================

begin;

-- Ancienne signature (avant filetage / double sortie) : remplacée, pas surchargée.
drop function if exists public.corriger_bouteille(bigint, text, text, text, date, date, text, text);

create or replace function public.corriger_bouteille(
    p_item_id          bigint,
    p_famille          text,
    p_matiere          text,
    p_statut           text,
    p_date_optique     date,
    p_date_hydraulique date,
    p_motif            text,
    p_filetage         text,
    p_double_sortie    boolean,
    p_autorite         text default null)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_old           public.item_bouteille%rowtype;
    v_old_statut    text;
    v_aujourdhui    date := (now() at time zone 'Europe/Paris')::date;
    v_champs_change boolean;
    v_statut_change boolean;
    v_filetage      text := nullif(btrim(p_filetage), '');
begin
    if public.moamat_role_rank() < 4 then
        raise exception 'Correction réservée au super-admin.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_motif is null or btrim(p_motif) = '' then
        raise exception 'Motif de la correction obligatoire.' using errcode = 'check_violation';
    end if;

    if p_famille is not null and p_famille not in ('plongee', 'deco_o2', 'o2_secourisme', 'bloc_tampon') then
        raise exception 'Type de gaz invalide : %.', p_famille using errcode = 'check_violation';
    end if;

    if p_matiere is not null and p_matiere not in ('acier', 'alu', 'carbone') then
        raise exception 'Matière invalide : %.', p_matiere using errcode = 'check_violation';
    end if;

    if p_date_optique > v_aujourdhui or p_date_hydraulique > v_aujourdhui then
        raise exception 'Date de contrôle dans le futur : refusée.' using errcode = 'check_violation';
    end if;

    select * into v_old from public.item_bouteille where item_id = p_item_id for update;
    if not found then
        raise exception 'Bouteille introuvable : %.', p_item_id using errcode = 'no_data_found';
    end if;

    select statut_code into v_old_statut from public.item where id = p_item_id for update;

    v_champs_change := v_old.famille is distinct from p_famille
                    or v_old.matiere is distinct from p_matiere
                    or v_old.filetage is distinct from v_filetage
                    or v_old.double_sortie is distinct from p_double_sortie
                    or v_old.date_dernier_controle_optique is distinct from p_date_optique
                    or v_old.date_dernier_controle_hydraulique is distinct from p_date_hydraulique;
    v_statut_change := p_statut is not null and p_statut is distinct from v_old_statut;

    if not v_champs_change and not v_statut_change then
        raise exception 'Aucune modification à enregistrer.' using errcode = 'check_violation';
    end if;

    if v_champs_change then
        update public.item_bouteille
        set famille = p_famille,
            matiere = p_matiere,
            filetage = v_filetage,
            double_sortie = p_double_sortie,
            date_dernier_controle_optique = p_date_optique,
            date_dernier_controle_hydraulique = p_date_hydraulique
        where item_id = p_item_id;
    end if;

    -- Le trigger de transition valide (motif, date, autorité pour un statut
    -- terminal, pièce jointe Perte/Vol) puis journalise dans item_transition.
    if v_statut_change then
        update public.item
        set statut_code = p_statut,
            statut_motif = btrim(p_motif),
            statut_date_effet = v_aujourdhui,
            statut_autorite = p_autorite
        where id = p_item_id;
    end if;

    perform public.audit_write(
        'bouteille.correction',
        'item',
        p_item_id::text,
        jsonb_build_object('famille', v_old.famille, 'matiere', v_old.matiere,
                           'filetage', v_old.filetage, 'double_sortie', v_old.double_sortie,
                           'statut', v_old_statut,
                           'controle_optique', v_old.date_dernier_controle_optique,
                           'controle_hydraulique', v_old.date_dernier_controle_hydraulique),
        jsonb_build_object('famille', p_famille, 'matiere', p_matiere,
                           'filetage', v_filetage, 'double_sortie', p_double_sortie,
                           'statut', coalesce(p_statut, v_old_statut),
                           'controle_optique', p_date_optique,
                           'controle_hydraulique', p_date_hydraulique),
        jsonb_build_object('motif', btrim(p_motif)));
end $$;

comment on function public.corriger_bouteille(bigint, text, text, text, date, date, text, text, boolean, text) is
    'Correction super-admin d''une fiche bouteille : type de gaz (famille), matière, filetage, double sortie, statut, dates des derniers contrôles (état cible complet). Peut faire reculer un compteur ; motif obligatoire ; statut via la transition normale (item_transition). Journalisé avec avant/après (bouteille.correction).';

revoke execute on function public.corriger_bouteille(bigint, text, text, text, date, date, text, text, boolean, text) from public;
grant execute on function public.corriger_bouteille(bigint, text, text, text, date, date, text, text, boolean, text) to authenticated;

commit;
