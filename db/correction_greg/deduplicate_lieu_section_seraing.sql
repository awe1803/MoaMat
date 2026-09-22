-- =============================================================================
--  MoaMat — Déduplication de public.lieu_section : "Seraing" / "SERAING"
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql (a besoin de public.lieu_section /
--  lieu_local / lieu_contenant / item / lieu_mapping).
--
--  public.lieu_section.libelle a une contrainte UNIQUE mais sensible à la
--  casse (unique (libelle) sur du text, sans citext) : "Seraing" et
--  "SERAING" ont donc pu coexister. Ce script fusionne toutes les variantes
--  de casse/espaces d'un même libellé en UNE section canonique ("Seraing"
--  si elle existe, sinon la plus ancienne des variantes).
--
--  Générique et sûr en profondeur : si les sections en double avaient
--  chacune un local ou un contenant de même nom, on ne se contente pas de
--  re-pointer section_id (ce qui violerait unique (section_id, libelle) /
--  unique (local_id, libelle)) — on fusionne aussi ces locaux/contenants,
--  et on re-pointe les items (item.lieu_contenant_id) et le mapping Access
--  (lieu_mapping.contenant_id) qui visaient un contenant absorbé.
--
--  Ré-exécutable sans erreur : si plus aucun doublon "Seraing" n'existe,
--  la boucle ne trouve rien à fusionner.
-- -----------------------------------------------------------------------------

begin;

do $$
declare
    v_label        text := 'Seraing';
    v_canonical_id bigint;
    v_dup_id       bigint;
    v_loc          record;
    v_match_loc_id bigint;
    v_cont         record;
    v_match_cont_id bigint;
begin
    -- Section canonique : le libellé bien casé s'il existe, sinon la plus
    -- ancienne variante (id le plus petit).
    select id into v_canonical_id
    from public.lieu_section
    where lower(trim(libelle)) = lower(trim(v_label))
    order by (libelle = v_label) desc, id
    limit 1;

    if v_canonical_id is null then
        raise notice 'Aucune section "%" trouvée, rien à faire.', v_label;
        return;
    end if;

    for v_dup_id in
        select id
        from public.lieu_section
        where lower(trim(libelle)) = lower(trim(v_label))
          and id <> v_canonical_id
    loop
        raise notice 'Fusion de la section % (id=%) dans % (id=%)',
            v_label, v_dup_id, v_label, v_canonical_id;

        for v_loc in select * from public.lieu_local where section_id = v_dup_id loop
            select id into v_match_loc_id
            from public.lieu_local
            where section_id = v_canonical_id
              and lower(trim(libelle)) = lower(trim(v_loc.libelle));

            if v_match_loc_id is null then
                -- Aucun local homonyme côté canonique : on le déplace tel quel.
                update public.lieu_local
                set section_id = v_canonical_id
                where id = v_loc.id;
            else
                -- Local homonyme : on fusionne les contenants, niveau par niveau.
                for v_cont in select * from public.lieu_contenant where local_id = v_loc.id loop
                    select id into v_match_cont_id
                    from public.lieu_contenant
                    where local_id = v_match_loc_id
                      and lower(trim(libelle)) = lower(trim(v_cont.libelle));

                    if v_match_cont_id is null then
                        update public.lieu_contenant
                        set local_id = v_match_loc_id
                        where id = v_cont.id;
                    else
                        -- Contenant homonyme : re-pointer items et mapping avant
                        -- de supprimer le doublon.
                        update public.item
                        set lieu_contenant_id = v_match_cont_id
                        where lieu_contenant_id = v_cont.id;

                        update public.lieu_mapping
                        set contenant_id = v_match_cont_id
                        where contenant_id = v_cont.id;

                        delete from public.lieu_contenant where id = v_cont.id;
                    end if;
                end loop;

                delete from public.lieu_local where id = v_loc.id;
            end if;
        end loop;

        delete from public.lieu_section where id = v_dup_id;
    end loop;

    -- Normalise la casse du libellé canonique.
    update public.lieu_section
    set libelle = v_label
    where id = v_canonical_id
      and libelle <> v_label;
end $$;

commit;

-- Pour prévisualiser SANS écrire, repérer d'abord les doublons :
--   select id, libelle
--   from public.lieu_section
--   where lower(trim(libelle)) = lower(trim('Seraing'))
--   order by id;
