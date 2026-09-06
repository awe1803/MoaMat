-- =============================================================================
--  MoaMat — Row Level Security : policies explicites sur toutes les tables.
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/roles.sql et db/permissions.sql (et db/schema.sql /
--  db/initial_load.sql). Ré-exécutable sans erreur.
--
--  Principe : RLS activée sur TOUTES les tables métier + système, SANS
--  EXCEPTION. Aucune table n'est exposée sans policy explicite. La policy
--  permissive de démarrage « moamat_dev_all » (db/schema.sql) est supprimée.
--
--  Les policies ne testent jamais le rôle en dur : elles délèguent à
--  public.has_permission('<domaine>.<action>') (db/permissions.sql).
--
--  Rôle « service_role » (Edge Functions, tâches serveur) : contourne la RLS
--  par nature (attribut BYPASSRLS). Rôle « anon » (non authentifié) : aucune
--  policy ne le vise => accès nul sur tout le métier.
--
--  Table publique du journal d'audit : sa RLS est définie dans db/audit.sql
--  (créée dans le même fichier que la table).
-- =============================================================================

begin;

-- -----------------------------------------------------------------------------
--  1. Tables métier — 4 policies (select/insert/update/delete) par table,
--     générées à partir de la correspondance table -> domaine de permission.
-- -----------------------------------------------------------------------------

do $$
declare
    v_map jsonb := $json$[
        {"t": "bouteille",                    "d": "bouteille"},
        {"t": "bouteille_sortie_inventaire",  "d": "bouteille"},
        {"t": "bouteille_requalification",    "d": "bouteille"},
        {"t": "bouteille_membre",             "d": "bouteille"},
        {"t": "detendeur",                    "d": "detendeur"},
        {"t": "detendeur_sortie_inventaire",  "d": "detendeur"},
        {"t": "detendeur_intervention",       "d": "detendeur"},
        {"t": "gilet",                        "d": "gilet"},
        {"t": "gilet_sortie_inventaire",      "d": "gilet"},
        {"t": "petit_materiel",               "d": "petit_materiel"},
        {"t": "materiel_didactique",          "d": "materiel_didactique"},
        {"t": "piece_detachee",               "d": "piece_detachee"},
        {"t": "compresseur_releve",           "d": "compresseur"},
        {"t": "ref_action_compresseur",       "d": "referentiel"},
        {"t": "ref_gaz",                      "d": "referentiel"},
        {"t": "ref_local_didactique",         "d": "referentiel"},
        {"t": "ref_regle_requalification",    "d": "referentiel"},
        {"t": "ref_site",                     "d": "referentiel"},
        {"t": "ref_site_web",                 "d": "referentiel"},
        {"t": "ref_tarif_requalification",    "d": "referentiel"},
        {"t": "achat_facture",                "d": "achat"},
        {"t": "achat_reception",              "d": "achat"},
        {"t": "achat_2022_non_integre",       "d": "achat"},
        {"t": "devis_ligne",                  "d": "devis"},
        {"t": "fournisseur",                  "d": "fournisseur"},
        {"t": "personne",                     "d": "personne"},
        {"t": "pret",                         "d": "pret"},
        {"t": "utilisateur",                  "d": "utilisateur_legacy"}
    ]$json$;
    v_item jsonb;
    v_t    text;
    v_d    text;
begin
    for v_item in select * from jsonb_array_elements(v_map)
    loop
        v_t := v_item ->> 't';
        v_d := v_item ->> 'd';

        execute format('alter table public.%I enable row level security', v_t);
        execute format('drop policy if exists moamat_dev_all on public.%I', v_t);

        execute format('drop policy if exists %I on public.%I', v_t || '_sel', v_t);
        execute format(
            'create policy %I on public.%I for select to authenticated using (public.has_permission(%L))',
            v_t || '_sel', v_t, v_d || '.read');

        execute format('drop policy if exists %I on public.%I', v_t || '_ins', v_t);
        execute format(
            'create policy %I on public.%I for insert to authenticated with check (public.has_permission(%L))',
            v_t || '_ins', v_t, v_d || '.create');

        execute format('drop policy if exists %I on public.%I', v_t || '_upd', v_t);
        execute format(
            'create policy %I on public.%I for update to authenticated using (public.has_permission(%L)) with check (public.has_permission(%L))',
            v_t || '_upd', v_t, v_d || '.update', v_d || '.update');

        execute format('drop policy if exists %I on public.%I', v_t || '_del', v_t);
        execute format(
            'create policy %I on public.%I for delete to authenticated using (public.has_permission(%L))',
            v_t || '_del', v_t, v_d || '.delete');
    end loop;
end $$;

-- -----------------------------------------------------------------------------
--  2. public.utilisateur_role — chacun voit sa ligne ; l'attribution des rôles
--     est réservée à role.assign (admin+) ; les rôles élevés (admin,
--     super-admin) exigent role.assign_admin (super-admin). Nul ne peut
--     modifier sa propre ligne (anti-élévation).
-- -----------------------------------------------------------------------------

alter table public.utilisateur_role enable row level security;

-- Le hook custom_access_token_hook s'exécute en tant que supabase_auth_admin :
-- il doit pouvoir lire le rôle de n'importe quel utilisateur (modèle officiel
-- des Auth Hooks Supabase).
drop policy if exists utilisateur_role_auth_admin_read on public.utilisateur_role;
create policy utilisateur_role_auth_admin_read on public.utilisateur_role
    for select to supabase_auth_admin
    using (true);

drop policy if exists utilisateur_role_sel on public.utilisateur_role;
create policy utilisateur_role_sel on public.utilisateur_role
    for select to authenticated
    using (user_id = (select auth.uid()) or public.has_permission('role.read'));

drop policy if exists utilisateur_role_ins on public.utilisateur_role;
create policy utilisateur_role_ins on public.utilisateur_role
    for insert to authenticated
    with check (
        public.has_permission('role.assign')
        and (role not in ('admin', 'super-admin') or public.has_permission('role.assign_admin'))
    );

drop policy if exists utilisateur_role_upd on public.utilisateur_role;
create policy utilisateur_role_upd on public.utilisateur_role
    for update to authenticated
    using (
        public.has_permission('role.assign')
        and user_id <> (select auth.uid())
        and (role not in ('admin', 'super-admin') or public.has_permission('role.assign_admin'))
    )
    with check (
        public.has_permission('role.assign')
        and (role not in ('admin', 'super-admin') or public.has_permission('role.assign_admin'))
    );

drop policy if exists utilisateur_role_del on public.utilisateur_role;
create policy utilisateur_role_del on public.utilisateur_role
    for delete to authenticated
    using (
        public.has_permission('role.assign')
        and user_id <> (select auth.uid())
        and (role not in ('admin', 'super-admin') or public.has_permission('role.assign_admin'))
    );

-- -----------------------------------------------------------------------------
--  3. Catalogue de permissions — lecture : admin+ ; écriture : super-admin.
-- -----------------------------------------------------------------------------

alter table public.permission enable row level security;
drop policy if exists permission_sel on public.permission;
create policy permission_sel on public.permission
    for select to authenticated using (public.has_permission('permission.read'));
drop policy if exists permission_manage on public.permission;
create policy permission_manage on public.permission
    for all to authenticated
    using (public.has_permission('permission.manage'))
    with check (public.has_permission('permission.manage'));

alter table public.role_permission enable row level security;
drop policy if exists role_permission_sel on public.role_permission;
create policy role_permission_sel on public.role_permission
    for select to authenticated using (public.has_permission('permission.read'));
drop policy if exists role_permission_manage on public.role_permission;
create policy role_permission_manage on public.role_permission
    for all to authenticated
    using (public.has_permission('permission.manage'))
    with check (public.has_permission('permission.manage'));

-- -----------------------------------------------------------------------------
--  4. Contrôle : aucune table de public. ne doit rester sans RLS.
--     Lève une exception si une table oubliée est détectée.
-- -----------------------------------------------------------------------------

do $$
declare
    v_missing text;
begin
    select string_agg(c.relname, ', ')
    into v_missing
    from pg_class c
    join pg_namespace n on n.oid = c.relnamespace
    where n.nspname = 'public'
      and c.relkind = 'r'
      and c.relrowsecurity = false
      -- on ignore les tables gérées par une extension (ex. PostGIS)
      and not exists (
          select 1 from pg_depend d
          where d.objid = c.oid and d.deptype = 'e'
      );

    if v_missing is not null then
        raise exception 'RLS manquante sur les tables public : %', v_missing;
    end if;
end $$;

commit;
