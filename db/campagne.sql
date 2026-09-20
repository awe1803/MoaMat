-- =============================================================================
--  MoaMat — Campagnes de réépreuve de bouteilles : préparation d'un envoi
--  groupé chez un prestataire externe (ex. Apragaz), génération du bordereau,
--  pointage du retour, détection des bouteilles manquantes.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql, db/transform_item.sql, db/rls.sql,
--  db/audit.sql, db/item_etat.sql et db/item_bouteille.sql (a besoin de
--  public.item, public.item_bouteille, public.has_permission,
--  public.bouteille_tarif_apragaz et du trigger
--  public.tg_item_valider_transition_statut). Ré-exécutable sans erreur.
--
--  Fusion des tickets « entité Campagne » + « écran préparation » +
--  « écran retour ». Toutes les écritures passent par des fonctions
--  SECURITY DEFINER (même principe que public.set_compte_actif /
--  public.supprimer_compte, db/comptes.sql) : public.campagne et
--  public.campagne_ligne n'ont donc AUCUNE policy insert/update/delete —
--  la cohérence multi-table (transition de statut de la bouteille, mise à
--  jour du compteur de contrôle, calcul du statut de campagne) doit être
--  atomique et vérifiée côté base, jamais recomposée côté client.
--
--  Décisions actées avec le gestionnaire du produit :
--    - Pas de défaut entre hydraulique_huile / hydraulique_eau : la
--      prestation doit être saisie explicitement par ligne avant envoi.
--    - Une bouteille envoyée non pointée au retour est SIGNALÉE (liste des
--      manquantes) mais son statut n'est JAMAIS basculé automatiquement —
--      résolution manuelle par le gestionnaire.
--    - Le pointage exige un RÉSULTAT explicite par bouteille (conforme /
--      echec), sans défaut : une bouteille condamnée à la requalification
--      n'est jamais rebasculée en "en_stock" ni son échéance renouvelée —
--      elle reste où elle est, pour un déclassement manuel (résolution
--      manuelle par le gestionnaire, même principe que les manquantes).
--    - Conséquence assumée des deux points précédents : une bouteille
--      manquante ou condamnée reste "en_controle" tant qu'elle n'est pas
--      résolue manuellement, et la liste blanche de public.creer_campagne()
--      (voir Durcissement) l'exclut donc délibérément d'une NOUVELLE
--      campagne — jamais de ré-expédition silencieuse d'une bouteille dont
--      le sort n'a pas été tranché, condamnée en particulier.
--
--  Durcissement (revue de code) :
--    - public.creer_campagne() dédoublonne/trie le tableau de bouteilles et
--      refuse une bouteille inexistante, pas de la famille bouteille,
--      désactivée, ou dont le statut n'est pas dans la liste blanche
--      en_stock / en_attente_controle / hors_validite (exclut donc aussi,
--      explicitement : prêtée — pas physiquement disponible ; en
--      maintenance — déjà engagée ailleurs ; en_controle — résidu d'une
--      campagne précédente non résolue, manquante ou condamnée) — jamais
--      délégué au trigger de transition de statut (message clair, échec
--      précoce).
--    - Verrous "for update" sur public.campagne (envoyer_campagne,
--      pointer_retour_campagne, definir_prestation_campagne_ligne) contre
--      un double envoi / pointage concurrent, et pg_advisory_xact_lock(item_id),
--      ordonné par le tri ci-dessus, contre un deadlock entre créations
--      concurrentes portant sur des bouteilles communes.
--    - public.pointer_retour_campagne() exige un coût réel présent et >= 0
--      par ligne, une date de retour >= la date d'envoi, et p_lignes non vide
--      (un tableau vide basculerait toute la campagne sans rien pointer).
--      Refuse de re-pointer une ligne déjà pointée avec des valeurs
--      différentes ; un rejeu IDENTIQUE (retry réseau) est un VRAI no-op —
--      ni le compteur de contrôle ni le statut de la bouteille ne sont
--      retouchés, pour ne jamais écraser une décision manuelle prise
--      entretemps (ex. bouteille déclassée après un contrôle).
--    - public.envoyer_campagne() re-valide l'éligibilité de chaque bouteille
--      (même liste blanche que la création) au moment de l'envoi, pas
--      seulement à la création — une bouteille peut avoir changé de statut
--      entretemps via l'écran d'inventaire, pendant que la campagne était
--      en préparation.
--    - public.pointer_retour_campagne() est un VRAI no-op de bout en bout
--      (campagne.date_retour/modifie_le inchangés, aucune entrée d'audit) si
--      l'appel ne pointe rien de nouveau — pas seulement au niveau de chaque
--      ligne individuelle.
--    - Les trois opérations significatives (création, envoi, retour) sont
--      journalisées via public.audit_write() (db/audit.sql).
--    - "campagne.read" n'est PAS accordée au rôle "lecture" (db/permissions.sql) :
--      les coûts réels engagés sont plus proches d'une donnée financière
--      (domaine achat.*, admin+) que d'un référentiel de consultation.
--
--  Ce fichier pose :
--    1. public.campagne        — l'entité campagne (prestataire, statut,
--       bordereau, dates d'envoi/retour).
--    2. public.campagne_ligne  — une bouteille dans une campagne (prestation,
--       coût estimé/réel, certificat, date de retour individuelle).
--    3. public.creer_campagne()             — création en « preparation ».
--    4. public.definir_prestation_campagne_ligne() — saisie de la prestation
--       par ligne (obligatoire avant envoi).
--    5. public.envoyer_campagne()           — génère le bordereau, bascule
--       les bouteilles en « En contrôle ».
--    6. public.pointer_retour_campagne()    — pointage groupé du retour,
--       met à jour les compteurs de contrôle et rebascule en « En stock ».
--    7. public.v_campagne_ligne             — vue de lecture (code club).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Entité campagne
-- -----------------------------------------------------------------------------

create table if not exists public.campagne (
    id          bigint generated always as identity primary key,
    prestataire text not null,
    statut      text not null default 'preparation'
                check (statut in ('preparation', 'envoyee', 'retournee')),
    numero_bon  text,
    date_envoi  date,
    date_retour date,
    cree_le     timestamptz not null default now(),
    cree_par    uuid default auth.uid(),
    modifie_le  timestamptz not null default now()
);

comment on table public.campagne is
    'Campagne de réépreuve de bouteilles auprès d''un prestataire externe (ex. Apragaz) : préparation -> envoyée -> retournée. Écritures exclusivement via public.creer_campagne / envoyer_campagne / pointer_retour_campagne (aucune policy insert/update).';

create index if not exists ix_campagne_statut on public.campagne (statut);

-- -----------------------------------------------------------------------------
--  2. Ligne de campagne — une bouteille
-- -----------------------------------------------------------------------------

create table if not exists public.campagne_ligne (
    id                bigint generated always as identity primary key,
    campagne_id       bigint not null references public.campagne (id) on delete cascade,
    item_id           bigint not null references public.item (id),
    -- Mêmes 3 valeurs que public.ref_tarif_apragaz.type_prestation
    -- (db/item_bouteille.sql). NULL jusqu'à saisie explicite (Q hydraulique
    -- huile/eau ci-dessus) : jamais de valeur par défaut.
    type_prestation   text check (type_prestation in ('rr', 'hydraulique_huile', 'hydraulique_eau')),
    cout_estime_eur   numeric(12,2),
    cout_reel_eur     numeric(12,2),
    num_certificat    text,
    -- Résultat de la requalification — NULL jusqu'au pointage, aucune valeur
    -- par défaut à l'enregistrement (le gestionnaire doit choisir
    -- explicitement conforme/echec, jamais une bouteille condamnée ne doit
    -- pouvoir être pointée "par défaut" comme conforme). 'echec' : la
    -- bouteille n'est PAS rebasculée en "en_stock" ni son compteur de
    -- contrôle mis à jour (public.pointer_retour_campagne) — elle reste où
    -- elle est, pour un déclassement manuel via l'écran de statut normal.
    resultat          text check (resultat in ('conforme', 'echec')),
    date_retour_ligne date,
    unique (campagne_id, item_id)
);

comment on table public.campagne_ligne is
    'Une bouteille au sein d''une campagne. date_retour_ligne NULL alors que la campagne est "retournee" = bouteille manquante (jamais une colonne dédiée, purement dérivé — cf. public.v_campagne_ligne).';

create index if not exists ix_campagne_ligne_campagne on public.campagne_ligne (campagne_id);
create index if not exists ix_campagne_ligne_item      on public.campagne_ligne (item_id);

-- Une bouteille ne peut pas être engagée dans deux campagnes actives
-- (preparation/envoyee) simultanément — évite un double envoi. Vérification
-- par trigger plutôt que par index partiel : la condition porte sur le statut
-- de la campagne PARENTE, pas sur une colonne immutable de cette ligne.
--
-- pg_advisory_xact_lock(item_id) AVANT le select : sans lui, deux insertions
-- concurrentes pour la MÊME bouteille (ex. double clic sur « Créer » avant
-- que le premier appel n'ait committé) peuvent toutes les deux lire "aucun
-- conflit" avant que l'une des deux ne committe, et la bouteille se retrouve
-- engagée dans deux campagnes actives — exactement ce que ce trigger existe
-- pour empêcher. Le verrou est tenu jusqu'à la fin de la transaction
-- (relâché automatiquement, en commit comme en rollback) : la deuxième
-- insertion attend que la première ait committé (ou annulé) avant de lire,
-- et voit alors la ligne concurrente si elle a été committée.
--
-- Espace de clés global : pg_advisory_xact_lock(bigint) partage UN SEUL
-- espace de verrous avisoires par base, tous appelants confondus — aucune
-- autre partie de MoaMat n'en prend à ce jour (seul appel du dépôt). Si un
-- autre mécanisme venait un jour à en prendre aussi sur des entiers pouvant
-- coïncider avec un item_id, la seule conséquence serait un blocage
-- ponctuel superflu (jamais une incohérence de données : c'est un simple
-- mutex) — mais il faudrait alors namespacer via la variante à deux clés
-- (pg_advisory_xact_lock(int, int)) plutôt que de continuer à partager cet
-- espace à l'aveugle.
create or replace function public.tg_campagne_ligne_verifier_unicite_active()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    v_conflit bigint;
begin
    perform pg_advisory_xact_lock(new.item_id);

    select l.campagne_id into v_conflit
    from public.campagne_ligne l
    join public.campagne c on c.id = l.campagne_id
    where l.item_id = new.item_id
      and c.statut in ('preparation', 'envoyee')
      and l.campagne_id is distinct from new.campagne_id
    limit 1;

    if v_conflit is not null then
        raise exception 'La bouteille % est déjà engagée dans la campagne active %.', new.item_id, v_conflit
            using errcode = 'unique_violation';
    end if;

    return new;
end $$;

drop trigger if exists campagne_ligne_verifier_unicite_active on public.campagne_ligne;
create trigger campagne_ligne_verifier_unicite_active
    before insert on public.campagne_ligne
    for each row execute function public.tg_campagne_ligne_verifier_unicite_active();

-- -----------------------------------------------------------------------------
--  RLS — lecture seule pour les clients ; toute écriture passe par les
--  fonctions SECURITY DEFINER ci-dessous (aucune policy insert/update/delete).
-- -----------------------------------------------------------------------------

alter table public.campagne       enable row level security;
alter table public.campagne_ligne enable row level security;

drop policy if exists campagne_sel on public.campagne;
create policy campagne_sel on public.campagne
    for select to authenticated
    using (public.has_permission('campagne.read'));

drop policy if exists campagne_ligne_sel on public.campagne_ligne;
create policy campagne_ligne_sel on public.campagne_ligne
    for select to authenticated
    using (public.has_permission('campagne.read'));

-- -----------------------------------------------------------------------------
--  3. Création — un lot de bouteilles sélectionnées par échéance proche.
-- -----------------------------------------------------------------------------

create or replace function public.creer_campagne(p_prestataire text, p_item_ids bigint[])
returns bigint
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_campagne_id bigint;
    v_item_id     bigint;
    v_invalides   bigint[];
    v_inadmissibles bigint[];
begin
    if not public.has_permission('campagne.create') then
        raise exception 'Droit insuffisant : permission « campagne.create » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    if p_prestataire is null or btrim(p_prestataire) = '' then
        raise exception 'Prestataire obligatoire.' using errcode = 'check_violation';
    end if;

    if p_item_ids is null or array_length(p_item_ids, 1) is null then
        raise exception 'Aucune bouteille sélectionnée.' using errcode = 'check_violation';
    end if;

    -- Dédoublonné ET trié avant toute chose. Le tri sert deux buts à la fois :
    -- 1) les doublons du tableau d'entrée (ex. double sélection côté client)
    --    ne produiraient sinon PAS une erreur de contrainte lisible mais un
    --    conflit "unique_violation" opaque sur (campagne_id, item_id) à mi-
    --    boucle. 2) le trigger d'unicité (ci-dessus) prend un verrou avisoire
    --    par item_id : sans un ordre GLOBAL et déterministe, deux créations
    --    concurrentes portant sur des lots de bouteilles qui se chevauchent
    --    pourraient chacune verrouiller sa première bouteille puis attendre
    --    l'autre — un deadlock classique. Trier élimine ce risque : toute
    --    transaction concurrente acquiert ses verrous dans le même ordre
    --    croissant.
    select array_agg(distinct x order by x) into p_item_ids from unnest(p_item_ids) x;

    -- Bouteille inexistante, ou existante mais pas de la famille "bouteille"
    -- (pas de ligne item_bouteille) : ce mécanisme ne doit jamais pouvoir
    -- faire transiter le statut d'un item d'une AUTRE famille (gilet,
    -- détendeur…) — envoyer_campagne bascule aveuglément statut_code, qui est
    -- une colonne partagée par toutes les familles sur public.item.
    select array_agg(x.item_id order by x.item_id) into v_invalides
    from unnest(p_item_ids) as x(item_id)
    left join public.item i on i.id = x.item_id
    left join public.item_bouteille b on b.item_id = x.item_id
    where i.id is null or b.item_id is null;

    if v_invalides is not null then
        -- check_violation (pas foreign_key_violation) : c'est une règle
        -- métier ("cette sélection n'est pas éligible"), pas une violation
        -- de contrainte FK au sens strict — et le client catégorise
        -- foreign_key_violation comme un "conflit" générique ("cette valeur
        -- existe déjà"), un message trompeur ici (voir
        -- SupabaseFailureTranslator, qui route check_violation vers le
        -- message spécifique fourni par l'appelant).
        raise exception 'Sélection refusée : identifiant(s) invalide(s) ou non-bouteille : %.', v_invalides
            using errcode = 'check_violation';
    end if;

    -- Désactivée, ou statut hors de la liste blanche des statuts éligibles à
    -- une campagne : refusé ICI, avec un message clair désignant la
    -- bouteille en cause, plutôt que de laisser l'erreur surgir plus tard et
    -- confusément au moment de l'envoi (public.tg_item_valider_transition_statut
    -- refuserait alors la transition ENTIÈRE campagne pour une seule
    -- bouteille fautive, avec un message générique de statut terminal).
    --
    -- Liste blanche plutôt que simple exclusion des statuts terminaux — une
    -- bouteille doit être PHYSIQUEMENT au club et disponible pour être
    -- expédiée :
    --   - 'prete' exclue : prêtée à un membre, pas physiquement disponible ;
    --     l'inclure permettrait à un envoi de campagne d'écraser
    --     silencieusement le suivi de prêt en forçant "en_controle".
    --   - 'en_maintenance' exclue : déjà engagée dans un autre workflow
    --     physique interne, ne pas la double-réserver.
    --   - 'en_controle' exclue : c'est PRÉCISÉMENT le statut laissé par une
    --     campagne précédente pour une bouteille manquante (jamais pointée)
    --     OU condamnée (resultat = 'echec', voir public.pointer_retour_campagne) —
    --     dans les deux cas, la resituer dans le circuit d'expédition SANS
    --     résolution manuelle préalable (retour à en_stock si retrouvée /
    --     bonne, déclassement si perdue/condamnée) serait soit une expédition
    --     redondante pour une bouteille déjà en cours, soit — pour une
    --     bouteille condamnée — la ré-expédier vers le prestataire alors
    --     qu'elle ne devrait plus jamais y retourner.
    --   - Les statuts terminaux (perdu/volé/retiré du service) ne sont de
    --     toute façon pas dans la liste blanche.
    select array_agg(i.id order by i.id) into v_inadmissibles
    from public.item i
    where i.id = any(p_item_ids)
      and (not i.actif or i.statut_code not in ('en_stock', 'en_attente_controle', 'hors_validite'));

    if v_inadmissibles is not null then
        raise exception 'Sélection refusée : bouteille(s) désactivée(s) ou dans un statut non éligible à une campagne (doit être en_stock, en_attente_controle ou hors_validite ; une bouteille manquante ou condamnée à une campagne précédente doit d''abord être résolue manuellement) : %.', v_inadmissibles
            using errcode = 'check_violation';
    end if;

    insert into public.campagne (prestataire) values (p_prestataire)
        returning id into v_campagne_id;

    foreach v_item_id in array p_item_ids loop
        insert into public.campagne_ligne (campagne_id, item_id) values (v_campagne_id, v_item_id);
    end loop;

    perform public.audit_write(
        'campagne.created',
        'campagne',
        v_campagne_id::text,
        null,
        jsonb_build_object('prestataire', p_prestataire, 'item_ids', p_item_ids),
        '{}'::jsonb);

    return v_campagne_id;
end $$;

comment on function public.creer_campagne(text, bigint[]) is
    'Crée une campagne en statut "preparation" avec une ligne par bouteille sélectionnée (prestation non renseignée). Exige "campagne.create". Le tableau de bouteilles est dédoublonné et trié (ordre déterministe des verrous avisoires, cf. trigger d''unicité). Refuse une bouteille inexistante, qui n''est pas de la famille bouteille, désactivée, dans un statut hors liste blanche (en_stock / en_attente_controle / hors_validite uniquement — exclut prêtée, en maintenance, et en_controle qu''une campagne précédente ait laissé manquante ou condamnée), ou déjà engagée dans une autre campagne active (trigger public.tg_campagne_ligne_verifier_unicite_active). Journalisé (campagne.created).';

revoke execute on function public.creer_campagne(text, bigint[]) from public;
grant execute on function public.creer_campagne(text, bigint[]) to authenticated;

-- -----------------------------------------------------------------------------
--  4. Prestation par ligne — obligatoire avant envoi, pas de défaut huile/eau.
--     Calcule au passage le coût estimé via le référentiel tarifaire courant.
-- -----------------------------------------------------------------------------

create or replace function public.definir_prestation_campagne_ligne(p_ligne_id bigint, p_type_prestation text)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_statut text;
begin
    if not public.has_permission('campagne.update') then
        raise exception 'Droit insuffisant : permission « campagne.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    -- "for update of c" verrouille la ligne public.campagne (pas
    -- campagne_ligne) le temps de la transaction : sérialise avec la même
    -- opération sur une autre ligne de la même campagne, ET avec le verrou
    -- pris par envoyer_campagne (ci-dessous) sur cette même ligne campagne —
    -- sans quoi une saisie de prestation pourrait s'intercaler entre le
    -- comptage des lignes sans prestation et le passage effectif au statut
    -- "envoyee", laissant une incertitude sur l'état réellement envoyé.
    select c.statut into v_statut
    from public.campagne_ligne l
    join public.campagne c on c.id = l.campagne_id
    where l.id = p_ligne_id
    for update of c;

    if v_statut is null then
        raise exception 'Ligne de campagne introuvable : %.', p_ligne_id using errcode = 'no_data_found';
    end if;

    if v_statut <> 'preparation' then
        raise exception 'La prestation ne peut être modifiée que pendant la préparation.'
            using errcode = 'check_violation';
    end if;

    update public.campagne_ligne
    set type_prestation = p_type_prestation,
        cout_estime_eur = public.bouteille_tarif_apragaz(p_type_prestation, current_date)
    where id = p_ligne_id;
end $$;

comment on function public.definir_prestation_campagne_ligne(bigint, text) is
    'Renseigne la prestation (rr / hydraulique_huile / hydraulique_eau) d''une ligne de campagne encore en préparation et recalcule son coût estimé via public.bouteille_tarif_apragaz(). Exige "campagne.update".';

revoke execute on function public.definir_prestation_campagne_ligne(bigint, text) from public;
grant execute on function public.definir_prestation_campagne_ligne(bigint, text) to authenticated;

-- -----------------------------------------------------------------------------
--  5. Envoi — génère le bordereau, bascule les bouteilles en "En contrôle".
--     Refuse tant qu'une ligne n'a pas de prestation renseignée.
-- -----------------------------------------------------------------------------

create or replace function public.envoyer_campagne(p_campagne_id bigint, p_numero_bon text, p_date_envoi date)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_statut         text;
    v_lignes_sans_prestation integer;
    v_item_id        bigint;
    v_inadmissibles  bigint[];
begin
    if not public.has_permission('campagne.update') then
        raise exception 'Droit insuffisant : permission « campagne.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    -- "for update" : verrouille la ligne de campagne pour le reste de la
    -- transaction. Sans ça, deux envois concurrents du MÊME p_campagne_id
    -- (double clic sur « Envoyer », ou deux onglets) liraient tous les deux
    -- statut = 'preparation' avant qu'aucun des deux n'ait committé, et
    -- passeraient tous les deux la vérification — le second écraserait
    -- alors silencieusement numero_bon/date_envoi du premier. Avec le
    -- verrou, le second attend la fin du premier puis relit statut =
    -- 'envoyee' et est proprement refusé ci-dessous. Sérialise aussi avec
    -- public.definir_prestation_campagne_ligne (même verrou "for update of c").
    select statut into v_statut from public.campagne where id = p_campagne_id for update;

    if v_statut is null then
        raise exception 'Campagne introuvable : %.', p_campagne_id using errcode = 'no_data_found';
    end if;

    if v_statut <> 'preparation' then
        raise exception 'Seule une campagne en préparation peut être envoyée.'
            using errcode = 'check_violation';
    end if;

    if p_numero_bon is null or btrim(p_numero_bon) = '' then
        raise exception 'Numéro de bon obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date_envoi is null then
        raise exception 'Date d''envoi obligatoire.' using errcode = 'check_violation';
    end if;

    select count(*) into v_lignes_sans_prestation
    from public.campagne_ligne
    where campagne_id = p_campagne_id and type_prestation is null;

    if v_lignes_sans_prestation > 0 then
        raise exception 'Envoi refusé : % bouteille(s) sans prestation choisie.', v_lignes_sans_prestation
            using errcode = 'check_violation';
    end if;

    -- Re-validation d'éligibilité, EXACTEMENT la même liste blanche que
    -- public.creer_campagne() (voir son commentaire pour le détail de
    -- chaque exclusion — prêtée, en maintenance, en_controle résiduel) :
    -- une bouteille peut avoir changé de statut via l'écran d'inventaire
    -- normal PENDANT que la campagne était encore en préparation —
    -- creer_campagne() ne protège qu'à l'instant de la sélection, pas
    -- jusqu'à l'envoi. Sans ce contrôle, la boucle de bascule ci-dessous
    -- laisserait le trigger public.tg_item_valider_transition_statut échouer
    -- à mi-boucle sur un message générique ("Statut terminal
    -- irréversible…", 42501) plutôt que sur ce message précis et explicite,
    -- levé AVANT toute mutation.
    select array_agg(i.id order by i.id) into v_inadmissibles
    from public.item i
    join public.campagne_ligne l on l.item_id = i.id
    where l.campagne_id = p_campagne_id
      and (not i.actif or i.statut_code not in ('en_stock', 'en_attente_controle', 'hors_validite'));

    if v_inadmissibles is not null then
        raise exception 'Envoi refusé : bouteille(s) devenue(s) non éligible(s) depuis la préparation (désactivée(s) ou statut non éligible) : %.', v_inadmissibles
            using errcode = 'check_violation';
    end if;

    update public.campagne
    set statut = 'envoyee', numero_bon = p_numero_bon, date_envoi = p_date_envoi, modifie_le = now()
    where id = p_campagne_id;

    for v_item_id in select item_id from public.campagne_ligne where campagne_id = p_campagne_id loop
        update public.item
        set statut_code = 'en_controle',
            statut_motif = format('Envoyée en campagne de réépreuve n°%s (bordereau %s)', p_campagne_id, p_numero_bon),
            statut_date_effet = p_date_envoi
        where id = v_item_id;
    end loop;

    perform public.audit_write(
        'campagne.sent',
        'campagne',
        p_campagne_id::text,
        jsonb_build_object('statut', 'preparation'),
        jsonb_build_object('statut', 'envoyee', 'numero_bon', p_numero_bon, 'date_envoi', p_date_envoi),
        '{}'::jsonb);
end $$;

comment on function public.envoyer_campagne(bigint, text, date) is
    'Génère le bordereau (numéro + date) et fait passer la campagne de "preparation" à "envoyee". Refuse si une ligne n''a pas de prestation renseignée. Re-valide l''éligibilité de chaque bouteille (active, statut dans la liste blanche en_stock/en_attente_controle/hors_validite — même règle qu''à la création) au moment de l''envoi — pas seulement à la création (public.creer_campagne()) — avec un message précis, plutôt que de laisser le trigger de transition échouer à mi-boucle si une bouteille a changé de statut entretemps (ex. prêtée). Verrouille la ligne de campagne ("for update") pour empêcher un double envoi concurrent d''écraser silencieusement le bordereau. Bascule chaque bouteille en "en_controle" via le trigger existant public.tg_item_valider_transition_statut (motif, historisation, audit). Exige "campagne.update". SECURITY DEFINER : contourne la policy item_upd, comme public.item_bouteille_appliquer_hors_validite() — restreint aux seules bouteilles validées par public.creer_campagne(). Journalisé (campagne.sent).';

revoke execute on function public.envoyer_campagne(bigint, text, date) from public;
grant execute on function public.envoyer_campagne(bigint, text, date) to authenticated;

-- -----------------------------------------------------------------------------
--  6. Retour groupé — pointage, coûts, certificats ; bouteilles non listées
--     restent "en_controle" (manquantes, cf. public.v_campagne_ligne).
--     p_lignes : tableau JSON [{ "ligne_id", "date_retour", "cout_reel_eur",
--     "num_certificat" }, ...] pour les seules bouteilles effectivement
--     reçues dans cette session de pointage.
-- -----------------------------------------------------------------------------

create or replace function public.pointer_retour_campagne(p_campagne_id bigint, p_date_retour date, p_lignes jsonb)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_statut           text;
    v_date_envoi       date;
    v_ligne            jsonb;
    v_ligne_id         bigint;
    v_item_id          bigint;
    v_type_prestation  text;
    v_date_retour_ligne date;
    v_cout_reel        numeric;
    v_resultat         text;
    v_existant_date    date;
    v_existant_cout    numeric;
    v_existant_certif  text;
    v_existant_resultat text;
    v_pointees         bigint[] := array[]::bigint[];
    v_nouvelles        integer := 0;
begin
    if not public.has_permission('campagne.update') then
        raise exception 'Droit insuffisant : permission « campagne.update » requise.'
            using errcode = 'insufficient_privilege';
    end if;

    -- "for update" : même raisonnement que envoyer_campagne — sans ce
    -- verrou, deux pointages concurrents du MÊME campagne_id pourraient
    -- tous les deux lire un statut compatible avant qu'aucun n'ait committé.
    select statut, date_envoi into v_statut, v_date_envoi from public.campagne where id = p_campagne_id for update;

    if v_statut is null then
        raise exception 'Campagne introuvable : %.', p_campagne_id using errcode = 'no_data_found';
    end if;

    if v_statut = 'preparation' then
        raise exception 'Campagne non envoyée : rien à pointer.' using errcode = 'check_violation';
    end if;

    if p_date_retour is null then
        raise exception 'Date de retour obligatoire.' using errcode = 'check_violation';
    end if;

    if p_date_retour < v_date_envoi then
        raise exception 'Date de retour (%) antérieure à la date d''envoi (%) : refusée.', p_date_retour, v_date_envoi
            using errcode = 'check_violation';
    end if;

    -- Un tableau vide marquerait TOUTE la campagne "retournee" avec chaque
    -- bouteille signalée manquante d'un seul coup, sans qu'aucune bouteille
    -- n'ait réellement été pointée — un appel direct de l'API (hors écran,
    -- qui bloque déjà ce cas côté client) ne doit pas pouvoir déclencher ça
    -- par erreur ou par un tableau vide envoyé par accident.
    if jsonb_array_length(coalesce(p_lignes, '[]'::jsonb)) = 0 then
        raise exception 'Au moins une bouteille pointée est requise pour enregistrer un retour.'
            using errcode = 'check_violation';
    end if;

    for v_ligne in select * from jsonb_array_elements(p_lignes) loop
        v_ligne_id := (v_ligne ->> 'ligne_id')::bigint;
        v_date_retour_ligne := coalesce((v_ligne ->> 'date_retour')::date, p_date_retour);
        -- Arrondi à 2 décimales AVANT toute comparaison/écriture : la colonne
        -- cout_reel_eur est numeric(12,2), donc un rejeu identique envoyant
        -- des décimales au-delà de la précision de la colonne (ex. 19.905)
        -- comparerait sinon la valeur BRUTE non arrondie à la valeur déjà
        -- persistée (arrondie par la colonne) et refuserait à tort un rejeu
        -- pourtant identique.
        v_cout_reel := round((v_ligne ->> 'cout_reel_eur')::numeric, 2);
        v_resultat := v_ligne ->> 'resultat';

        if v_cout_reel is null or v_cout_reel < 0 then
            raise exception 'Coût réel manquant ou négatif pour la ligne %.', v_ligne_id
                using errcode = 'check_violation';
        end if;

        if v_resultat is null or v_resultat not in ('conforme', 'echec') then
            raise exception 'Résultat de requalification manquant ou invalide pour la ligne % (attendu conforme/echec).', v_ligne_id
                using errcode = 'check_violation';
        end if;

        if v_date_retour_ligne < v_date_envoi then
            raise exception 'Ligne % : date de retour (%) antérieure à la date d''envoi (%) : refusée.',
                v_ligne_id, v_date_retour_ligne, v_date_envoi
                using errcode = 'check_violation';
        end if;

        -- "into strict" (not a plain "into" + null check) on purpose: v_item_id
        -- is declared once for the whole loop, so a plain "into" would leave it
        -- holding the PREVIOUS iteration's value when this select matches
        -- nothing, silently updating the wrong bottle's counter instead of
        -- reporting the unknown line.
        begin
            select l.item_id, l.type_prestation, l.date_retour_ligne, l.cout_reel_eur, l.num_certificat, l.resultat
            into strict v_item_id, v_type_prestation, v_existant_date, v_existant_cout, v_existant_certif, v_existant_resultat
            from public.campagne_ligne l
            where l.id = v_ligne_id and l.campagne_id = p_campagne_id;
        exception
            when no_data_found then
                raise exception 'Ligne % introuvable pour la campagne %.', v_ligne_id, p_campagne_id
                    using errcode = 'no_data_found';
        end;

        -- Une ligne déjà pointée (date_retour_ligne non NULL) peut être
        -- RE-soumise SANS erreur uniquement si les valeurs sont identiques —
        -- c'est le rejeu idempotent d'un appel déjà appliqué (réseau flaky,
        -- retenté côté client). Une resoumission avec des valeurs
        -- DIFFÉRENTES est refusée : accepter silencieusement écraserait le
        -- certificat/coût déjà enregistrés et, pire, pourrait faire
        -- RÉGRESSER date_dernier_controle_*, rétrécissant l'échéance
        -- recalculée sur la foi d'une date erronée sans aucun avertissement.
        -- Une correction volontaire d'un retour déjà pointé doit passer par
        -- un autre mécanisme, pas par un simple re-pointage.
        if v_existant_date is not null and (
            v_existant_date is distinct from v_date_retour_ligne
            or v_existant_cout is distinct from v_cout_reel
            or v_existant_certif is distinct from (v_ligne ->> 'num_certificat')
            or v_existant_resultat is distinct from v_resultat
        ) then
            raise exception 'Ligne % déjà pointée le % avec des valeurs différentes : re-pointage refusé (correction à faire par un autre moyen).',
                v_ligne_id, v_existant_date
                using errcode = 'check_violation';
        end if;

        -- Rejeu IDENTIQUE d'une ligne déjà pointée : un VRAI no-op — on
        -- s'arrête ici, sans réécrire ni campagne_ligne, ni le compteur de
        -- contrôle, ni le statut de la bouteille. Rien de plus n'est fait
        -- volontairement : entre deux sessions, le gestionnaire peut avoir
        -- traité manuellement cette bouteille autrement (déclassée, perdue,
        -- réformée après un contrôle...) ; un simple rejeu réseau ne doit
        -- jamais écraser cette décision en la reforçant vers "en_stock".
        if v_existant_date is not null then
            v_pointees := array_append(v_pointees, v_ligne_id);
            continue;
        end if;

        update public.campagne_ligne
        set cout_reel_eur     = v_cout_reel,
            num_certificat    = v_ligne ->> 'num_certificat',
            resultat          = v_resultat,
            date_retour_ligne = v_date_retour_ligne
        where id = v_ligne_id;

        -- Une ligne est comptée "nouvelle" ici, jamais dans la branche de
        -- rejeu identique ci-dessus : c'est ce compteur qui décide, en fin de
        -- fonction, si l'appel a réellement apporté une information neuve —
        -- condition du VRAI no-op global (voir plus bas).
        v_nouvelles := v_nouvelles + 1;

        -- "echec" : bouteille condamnée à la requalification. On s'arrête
        -- ICI, délibérément — ni le compteur de contrôle (la bouteille n'a
        -- PAS été validée à ce contrôle, lui donner une nouvelle échéance
        -- serait faux) ni le statut de l'item ne sont touchés : la bouteille
        -- reste "en_controle", plus "manquante" (la ligne a bien une
        -- date_retour_ligne) mais signalée par
        -- CampaignLine.RequiresManualFollowUp pour un déclassement manuel via
        -- l'écran de statut normal (motif + autorité décisionnaire, comme
        -- toute transition terminale — hors du périmètre de cette fonction).
        if v_resultat = 'echec' then
            v_pointees := array_append(v_pointees, v_ligne_id);
            continue;
        end if;

        -- Un compteur = un contrôle : "rr" alimente le compteur optique,
        -- les deux méthodes hydrauliques alimentent le même compteur
        -- hydraulique (public.tg_item_bouteille_sync_echeance recalcule
        -- ensuite l'échéance automatiquement).
        if v_type_prestation = 'rr' then
            update public.item_bouteille
            set date_dernier_controle_optique = v_date_retour_ligne
            where item_id = v_item_id;
        else
            update public.item_bouteille
            set date_dernier_controle_hydraulique = v_date_retour_ligne
            where item_id = v_item_id;
        end if;

        update public.item
        set statut_code = 'en_stock',
            statut_motif = format('Retour de campagne de réépreuve n°%s', p_campagne_id),
            statut_date_effet = v_date_retour_ligne
        where id = v_item_id;

        v_pointees := array_append(v_pointees, v_ligne_id);
    end loop;

    -- Toute bouteille sans ligne dans p_lignes reste "en_controle" et sans
    -- date_retour_ligne : c'est exactement la détection des manquantes
    -- (public.v_campagne_ligne), aucune action supplémentaire ici (décision
    -- actée : signalement seul, pas de bascule de statut automatique).
    --
    -- Le passage à "retournee" est déclenché dès le PREMIER appel, même
    -- partiel : "retournee" signifie "au moins une session de pointage a eu
    -- lieu", pas "tout est revenu" — public.v_campagne_ligne / les lignes
    -- encore sans date_retour_ligne restent le signal fiable de complétude,
    -- pas le statut de la campagne. Un pointage ultérieur (bouteilles
    -- arrivées en retard) reste possible : cette fonction est re-appelable
    -- tant que des lignes restent à pointer, protégée par le garde-fou
    -- d'idempotence ci-dessus pour les lignes déjà pointées.
    --
    -- date_retour est écrasée par CHAQUE appel qui touche vraiment quelque
    -- chose : elle représente la date de la DERNIÈRE session de pointage
    -- ayant apporté du neuf, pas de la première — il n'existe pas de table
    -- d'historique par session (hors du périmètre actuel). Si la date de la
    -- toute première session doit être conservée, il faudra l'exposer
    -- autrement (ex. la première ligne de public.audit_log pour l'action
    -- "campagne.returned" de cette campagne) plutôt que de la dupliquer ici.
    --
    -- Un appel qui n'a RIEN pointé de nouveau (v_nouvelles = 0 : toutes les
    -- lignes soumises étaient déjà pointées avec des valeurs identiques,
    -- rejeu idempotent pur) est un VRAI no-op — y compris pour la campagne
    -- elle-même — SAUF pour le tout premier appel (v_statut = 'envoyee' à
    -- l'entrée), qui doit toujours acter la transition preparation->envoyee
    -- ->retournee même si, en théorie, p_lignes ne contenait que des lignes
    -- déjà pointées par un autre moyen. Sans cette garde, un simple rejeu
    -- réseau avancerait silencieusement campagne.date_retour à la date de la
    -- tentative en cours et dupliquerait l'entrée d'audit "campagne.returned"
    -- — deux effets de bord qu'un VRAI no-op ne doit jamais avoir.
    if v_statut <> 'retournee' or v_nouvelles > 0 then
        update public.campagne
        set statut = 'retournee', date_retour = p_date_retour, modifie_le = now()
        where id = p_campagne_id;

        perform public.audit_write(
            'campagne.returned',
            'campagne',
            p_campagne_id::text,
            jsonb_build_object('statut', v_statut),
            jsonb_build_object('statut', 'retournee', 'date_retour', p_date_retour, 'lignes_pointees', v_pointees),
            '{}'::jsonb);
    end if;
end $$;

comment on function public.pointer_retour_campagne(bigint, date, jsonb) is
    'Pointage groupé du retour d''une campagne : p_lignes non vide obligatoire (un tableau vide marquerait toute la campagne manquante sans rien pointer). Pour chaque ligne, enregistre coût réel (obligatoire, >= 0, arrondi à 2 décimales)/certificat/date (>= date d''envoi de la campagne)/résultat (obligatoire, conforme/echec, sans défaut). Si "conforme" : met à jour le compteur de contrôle de la bouteille (recalcule son échéance via les triggers existants) et la rebascule en "en_stock". Si "echec" : la ligne est enregistrée (n''apparaît plus comme manquante) mais le compteur de contrôle et le statut de la bouteille ne sont JAMAIS touchés — elle reste "en_controle", signalée par CampaignLine.RequiresManualFollowUp côté domaine pour un déclassement manuel (motif + autorité décisionnaire) via l''écran de statut normal. Une ligne déjà pointée ne peut être resoumise qu''avec des valeurs IDENTIQUES (rejeu idempotent, VRAI no-op : ni campagne_ligne, ni le compteur de contrôle, ni le statut de la bouteille ne sont réécrits) ; des valeurs différentes sont refusées. Un appel qui ne pointe RIEN de nouveau (uniquement des rejeux identiques) est un VRAI no-op de bout en bout : campagne.date_retour/modifie_le ne bougent pas et aucune entrée d''audit "campagne.returned" n''est écrite — sauf pour le tout premier appel, qui acte toujours la transition. Les lignes absentes de p_lignes restent "en_controle" — bouteilles manquantes, signalées mais jamais basculées automatiquement (décision actée). Fait passer la campagne en "retournee" dès le premier appel, même partiel ; re-appelable pour les lignes encore manquantes ; date_retour reflète la DERNIÈRE session de pointage ayant apporté du neuf, pas la première (pas d''historique par session). Verrouille la ligne de campagne ("for update"). Exige "campagne.update". Journalisé (campagne.returned).';

revoke execute on function public.pointer_retour_campagne(bigint, date, jsonb) from public;
grant execute on function public.pointer_retour_campagne(bigint, date, jsonb) to authenticated;

-- -----------------------------------------------------------------------------
--  7. Vue de lecture — code club résolu, pratique pour les écrans.
-- -----------------------------------------------------------------------------

drop view if exists public.v_campagne_ligne;
create view public.v_campagne_ligne
    with (security_invoker = true) as
    select
        l.id,
        l.campagne_id,
        l.item_id,
        i.code_club as item_code_club,
        l.type_prestation,
        l.cout_estime_eur,
        l.cout_reel_eur,
        l.num_certificat,
        l.resultat,
        l.date_retour_ligne
    from public.campagne_ligne l
    join public.item i on i.id = l.item_id;

comment on view public.v_campagne_ligne is
    'Lecture des lignes de campagne avec le code club de la bouteille résolu. security_invoker : RLS de campagne_ligne et item appliquée.';

grant select on public.v_campagne_ligne to authenticated;

commit;
