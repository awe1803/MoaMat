# Code review — Item state machine / calculated availability / decision history

**Date:** 2026-09-12 21:14
**Scope:** `db/model_item.sql`, `db/item_etat.sql` (new), `db/transform_item.sql`, `db/tests/item_etat_tests.sql` (new), `db/SECURITE.md`, `db/MODELE.md`, `README.md`, and the C# wiring in `src/MoaMat.Domain/Inventory/*`, `src/MoaMat.Infrastructure/Supabase/*`.

## 1. Verdict

✅ **Acceptable**, ship with the caveats below tracked as follow-ups (not blockers). The state machine is modeled in the database (constraints, triggers, RLS) as required, not only in the UI; the four ticket acceptance criteria are met and covered by an automated test (`db/tests/item_etat_tests.sql`).

## 2. Critical Issues

None that block shipping. The items below are real correctness/behavior risks but are either pre-existing data-quality limits inherited from the legacy mirror, or a defensible scope choice — flagged for visibility, not blocking.

## 3. Recommended Improvements

- **`manquant` → `perdu` migration mapping loses reversibility silently.** `db/transform_item.sql` now maps legacy `gilet.est_manquant` to the new **terminal** status `perdu`. The bulk reprise is a raw `INSERT`, so it bypasses `tg_item_valider_transition_statut` entirely: every migrated "perdu" gilet has **zero** rows in `item_transition` explaining motif/date/authority. If such an item is later found, fixing it requires an admin `status.terminal.override` with a motif invented after the fact, and the decision trail mandated by R7bis.1 is empty for the whole migrated backlog. Confirmed intentional in this session's checkpoint, but worth a one-line note at reprise time (or a synthetic `item_transition` row stamped "migration") if the club ever needs to audit why a specific gilet was closed out this way.
- **`item_upd` RLS policy locks the whole row, not just the status, once terminal.** Editing `remarque` on an already-retired/lost/stolen item now also requires `status.terminal.override`. This was a deliberate simplification (confirmed) — worth documenting explicitly in `db/SECURITE.md` next to the R7bis.1 note so a future maintainer doesn't read it as a bug.
- **`item_a_pret_en_cours()` bridges to the legacy `pret` mirror by `code_club`.** Two items sharing an ambiguous/duplicated `code_club` (already flagged by `code_club_ambigu`) will both report `disponible = false` from a single open loan. Confirmed acceptable as a stopgap until a real item-scoped loan table exists; consider gating the bridge on `code_club_ambigu = false` if this becomes visible in practice.
- **Performance at scale.** `item_a_pret_en_cours()` does a `lower(btrim(...))` comparison against `public.pret` for every row of `v_item`, which defeats any plain b-tree index on `pret.bouteille_code`/`detendeur_code`/`gilet_code`. Non-issue at the current ~1,400-item volume (per `db/MODELE.md`), but add functional indexes (`lower(btrim(bouteille_code))`, etc.) if the mirror grows or listing latency becomes noticeable.

## 4. GitLab Comments

- `db/transform_item.sql:380` — confirm the `manquant → perdu` reclassification is intentional per the checkpoint answer; consider a one-line reprise note or synthetic `item_transition` entry for auditability.
- `db/item_etat.sql:223` — document in `db/SECURITE.md` that the terminal lock is row-wide, not status-only (deliberate, confirmed).
- `db/model_item.sql:491` — leave a TODO pointing at the future item-scoped loan model once it exists, to retire the `code_club` bridge.

## 5. Tests to Add

- Already added in `db/tests/item_etat_tests.sql`: motif/date required, piece-jointe required for Perdu/Volé, terminal irreversibility for `gestion` vs `admin` override, audit_log + item_transition journaling, and the A15/A16/A20 availability cases (échéance dépassée, prêt ouvert, en_maintenance).
- Follow-up once the real audit dataset is available: reconcile the A15/A16/A20 fixtures against the actual spreadsheet rows rather than the illustrative ones used here.
- Not covered yet: a duplicate-`code_club` regression test asserting the known double-unavailability limitation (documented above) behaves as expected rather than by accident.

## 6. Optional Refactoring

None high-value enough to warrant now — the design (transient `statut_*` input columns on `item`, validated and cleared by a single trigger, `item_transition` as an append-only ledger mirroring `audit_log`) is consistent with the codebase's existing patterns and doesn't need rework.
