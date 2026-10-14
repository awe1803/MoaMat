-- =============================================================================
--  MoaMat — Normalisation des marques (constat A22 : incohérences de casse —
--  « SCUBAPRO » / « Scubapro » / « scubapro » — dans les données sources).
--  Cible : Supabase / PostgreSQL 15+
-- =============================================================================
--
--  À exécuter APRÈS db/model_item.sql, AVANT db/transform_item.sql (le
--  transform appelle public.moamat_normalise_marque()). Ré-exécutable.
--
--  Principe : la casse d'origine n'est PAS corrigée en base miroir (on ne
--  réécrit jamais les tables sources — même doctrine que §5 de db/MODELE.md
--  pour code_club). Seule la marque recopiée dans public.item (couche
--  normalisée) passe par la table de correspondance ci-dessous ; une valeur
--  absente de la table est conservée telle quelle (jamais de perte silencieuse).
--
--  Ce fichier pose :
--    1. public.ref_marque_normalisation — table de correspondance (clé = marque
--       brute en majuscules/sans espaces superflus -> marque canonique).
--    2. public.moamat_normalise_marque(text) — résolveur, utilisé par
--       db/transform_item.sql pour toutes les familles (générique, pas
--       seulement détendeurs).
--    3. Amorçage des variantes observées sur les détendeurs (ticket « reprise
--       détendeurs »).
-- =============================================================================

begin;

create table if not exists public.ref_marque_normalisation (
    cle      text primary key,
    marque   text not null,
    cree_le  timestamptz not null default now()
);

comment on table public.ref_marque_normalisation is
    'Table de correspondance « constat A22 » : variantes de casse/espacement d''une même marque -> graphie canonique. cle = upper(btrim(marque brute)). Alimente public.moamat_normalise_marque(), appelé par db/transform_item.sql.';

alter table public.ref_marque_normalisation enable row level security;

drop policy if exists ref_marque_normalisation_sel on public.ref_marque_normalisation;
create policy ref_marque_normalisation_sel on public.ref_marque_normalisation
    for select to authenticated
    using (public.has_permission('referentiel.read'));

drop policy if exists ref_marque_normalisation_ins on public.ref_marque_normalisation;
create policy ref_marque_normalisation_ins on public.ref_marque_normalisation
    for insert to authenticated
    with check (public.has_permission('referentiel.create'));

create or replace function public.moamat_normalise_marque(p text)
returns text
language sql
stable
set search_path = ''
as $$
    select coalesce(
        (select n.marque
           from public.ref_marque_normalisation n
          where n.cle = upper(btrim(p))),
        nullif(btrim(p), ''))
$$;

comment on function public.moamat_normalise_marque(text) is
    'Résout la graphie canonique d''une marque via public.ref_marque_normalisation (clé insensible à la casse/aux espaces). Valeur NULL/vide -> NULL. Valeur sans correspondance connue -> renvoyée telle quelle (trim seul), jamais perdue.';

insert into public.ref_marque_normalisation (cle, marque) values
    ('SCUBAPRO',         'Scubapro'),
    ('LA SPIROTECHNIQUE', 'La Spirotechnique'),
    ('MARES',            'Mares'),
    ('AQUALUNG',         'Aqualung'),
    ('WIKA',             'Wika')
on conflict (cle) do nothing;

commit;
