-- =============================================================================
--  MoaMat — Recalcul de rattrapage des échéances bouteille
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/item_bouteille.sql et db/bouteille_evenement.sql (a
--  besoin de public.item, public.item_bouteille, public.bouteille_evenement,
--  public.bouteille_type_referentiel, public.bouteille_echeance).
--  Ré-exécutable sans erreur.
--
--  Pourquoi ce script existe : public.item.date_echeance n'est resynchronisé
--  par public.tg_item_bouteille_sync_echeance (db/item_bouteille.sql §7) que
--  lorsqu'UNE LIGNE de public.item_bouteille change, et cette synchronisation
--  se base uniquement sur les colonnes stockées date_dernier_controle_optique
--  / _hydraulique — pas sur la timeline. Une modification du référentiel
--  public.ref_periodicite_bouteille (nouvelle périodicité, correction d'une
--  date d'effet), un import de masse qui contourne ce trigger, ou un
--  événement de contrôle saisi dans public.bouteille_evenement sans mise à
--  jour des colonnes stockées, laissent donc les échéances déjà stockées
--  obsolètes.
--
--  Ce script recalcule TOUTES les bouteilles actives en reproduisant
--  exactement la règle affichée sur la fiche bouteille (voir
--  src/MoaMat.Web/Pages/CylinderSheet.razor.cs, chargement des détails) : le
--  dernier contrôle de chaque compteur vient de la timeline
--  public.bouteille_evenement quand elle en a un, avec repli sur la colonne
--  stockée sinon ; et un contrôle hydraulique compte aussi comme contrôle
--  optique quand il est le plus récent des deux (implique un examen visuel).
--  L'échéance de chaque compteur est ensuite celle de
--  public.bouteille_echeance (périodicité en vigueur à la date du dernier
--  contrôle, jamais celle du jour), et l'échéance retenue est la plus proche
--  des deux (echeance_min), même règle que public.v_item_bouteille.
--
--  Note : ce recalcul peut donc différer de public.v_item_bouteille /
--  public.tg_item_bouteille_sync_echeance tant que les colonnes stockées de
--  public.item_bouteille n'ont pas été alignées sur la timeline (cf.
--  db/aligner_controle_optique_sur_hydraulique.sql, qui ne couvre que
--  l'alignement optique/hydraulique entre elles, pas depuis la timeline).
-- -----------------------------------------------------------------------------

create or replace function public.bouteille_recalculer_echeances()
returns table (item_id bigint, ancienne_echeance date, nouvelle_echeance date)
language plpgsql
security definer
set search_path = ''
as $$
begin
    return query
    with dernier_controle_optique as (
        select e.item_id, max(e.date_evenement) as date_evenement
        from public.bouteille_evenement e
        where e.type = 'controle_optique'
        group by e.item_id
    ),
    dernier_controle_hydraulique as (
        select e.item_id, max(e.date_evenement) as date_evenement
        from public.bouteille_evenement e
        where e.type = 'controle_hydraulique'
        group by e.item_id
    ),
    derniers_controles as (
        select
            b.item_id,
            b.famille,
            b.matiere,
            coalesce(greatest(opt.date_evenement, hyd.date_evenement), b.date_dernier_controle_optique) as optique,
            coalesce(hyd.date_evenement, b.date_dernier_controle_hydraulique) as hydraulique
        from public.item_bouteille b
        left join dernier_controle_optique opt on opt.item_id = b.item_id
        left join dernier_controle_hydraulique hyd on hyd.item_id = b.item_id
    ),
    echeances as (
        select
            d.item_id,
            public.bouteille_echeance(
                public.bouteille_type_referentiel(d.famille, d.matiere), 'optique', d.optique
            ) as echeance_optique,
            public.bouteille_echeance(
                public.bouteille_type_referentiel(d.famille, d.matiere), 'hydraulique', d.hydraulique
            ) as echeance_hydraulique
        from derniers_controles d
    ),
    cible as (
        select
            i.id,
            i.date_echeance as ancienne,
            case
                when e.echeance_optique is null then e.echeance_hydraulique
                when e.echeance_hydraulique is null then e.echeance_optique
                else least(e.echeance_optique, e.echeance_hydraulique)
            end as nouvelle
        from public.item i
        join echeances e on e.item_id = i.id
        where i.actif
    ),
    applique as (
        update public.item i
        set date_echeance = c.nouvelle
        from cible c
        where i.id = c.id
          and i.date_echeance is distinct from c.nouvelle
        returning i.id
    )
    select c.id, c.ancienne, c.nouvelle
    from cible c
    left join applique a on a.id = c.id
    order by c.id;
end $$;

comment on function public.bouteille_recalculer_echeances() is
    'Rattrapage : force public.item.date_echeance = échéance recalculée depuis la timeline public.bouteille_evenement (même règle que la fiche bouteille dans l''appli, cf. CylinderSheet.razor.cs), pour toute bouteille active dont les deux divergent. Retourne une ligne pour TOUTE bouteille active (id, ancienne échéance, nouvelle échéance), corrigée ou non — comparer ancienne_echeance et nouvelle_echeance pour voir ce qui a changé. Ne touche pas statut_code : un rattrapage vers une échéance dépassée ne bascule pas la bouteille en hors_validite ici, cf. public.item_bouteille_appliquer_hors_validite à lancer ensuite si besoin.';

-- Même garde que public.item_bouteille_appliquer_hors_validite
-- (db/item_bouteille.sql §8) : SECURITY DEFINER contourne la policy RLS
-- item_upd, donc EXECUTE réservé à service_role — jamais à authenticated/anon,
-- sous peine de permettre à n'importe quel compte de réécrire date_echeance
-- sur n'importe quelle bouteille sans la permission item.update.
revoke execute on function public.bouteille_recalculer_echeances() from public, authenticated, anon;
grant execute on function public.bouteille_recalculer_echeances() to service_role;

-- Rattrapage immédiat : exécuter manuellement après une modification du
-- référentiel de périodicité, un import de masse, ou une saisie d'événement
-- qui n'a pas mis à jour les colonnes stockées. Ne fait rien si tout est déjà
-- à jour. Pour prévisualiser sans écrire, remplacer l'appel par :
--   with dernier_controle_optique as (
--       select e.item_id, max(e.date_evenement) as date_evenement
--       from public.bouteille_evenement e where e.type = 'controle_optique' group by e.item_id
--   ), dernier_controle_hydraulique as (
--       select e.item_id, max(e.date_evenement) as date_evenement
--       from public.bouteille_evenement e where e.type = 'controle_hydraulique' group by e.item_id
--   )
--   select
--       i.id as item_id,
--       i.date_echeance as prochaine_echeance,
--       coalesce(greatest(opt.date_evenement, hyd.date_evenement), b.date_dernier_controle_optique) as dernier_controle_optique,
--       coalesce(hyd.date_evenement, b.date_dernier_controle_hydraulique) as dernier_controle_hydraulique
--   from public.item i
--   join public.item_bouteille b on b.item_id = i.id
--   left join dernier_controle_optique opt on opt.item_id = i.id
--   left join dernier_controle_hydraulique hyd on hyd.item_id = i.id
--   where i.actif
--   order by i.id;
select * from public.bouteille_recalculer_echeances();
