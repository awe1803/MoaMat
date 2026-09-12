# MoaMat — Pending-account lifecycle & super-admin DB protection

**Date:** 2026-09-12 21:33
**Branch:** `38-gestion-des-comptes-utilisateurs`
**Scope:** `db/roles.sql`, `db/permissions.sql`, `db/rls.sql`, `db/comptes.sql`, `db/tests/rls_tests.sql`, `db/SECURITE.md`, `README.md`, `AppRole` / `UserAccount` / `AccountManagement.razor` (.NET domain + UI), unit tests.

---

## 1. Verdict

✅ **Acceptable.**

Most of the requested feature already existed on this branch (role table fed by an `on_auth_user_created` trigger, named permissions, RLS on every table, an append-only audit log). Two real gaps remained against the brief:

1. A newly registered account was assigned `lecture` directly, which **already carries real business-read permissions** — not the "no access until activated" behaviour the brief asks for.
2. Super-admin protection existed only as an RLS/permission rule (`role.assign_admin`, restricted to super-admin). There was no **hard, trigger-level** guarantee that a super-admin account can't be deleted, or that a demotion/deactivation can't leave zero active super-admins — the kind of invariant that should hold even against a direct SQL session or a compromised service-role key.

Both gaps are now closed with a 5th role (`en_attente`, rank 0, zero permissions) and three defence-in-depth triggers. Build is green, all 121 unit tests pass. The `en_attente` default plugs directly into the existing `has_permission()` / RLS machinery — no new policy shape was needed, only the absence of `role_permission` rows for that role.

A design checkpoint was raised with the tech lead before finalizing (see §6) — both answers are reflected in the code above.

---

## 2. Critical issues (production-impacting)

None found in the final state. Two were caught and fixed during this pass; documented as "would-have-been" issues:

### 2.1 — (caught before commit) Backfill would have silently locked out live accounts

The idempotent "catch-up" insert (`db/roles.sql`, comment block "Rattrapage") was first written to reuse `en_attente` for consistency with the new trigger default. On re-execution against an **already-provisioned** database, any account that — for whatever reason — never received a `utilisateur_role` row would flip from implicit `lecture` access to zero access, silently, the next time someone re-ran the script. Reverted to keep `lecture` for that specific catch-up path; only the trigger for *new* sign-ups uses `en_attente`. Confirmed with the tech lead (§6).

### 2.2 — (design decision, not a bug) Role-count vs. active-count for "last super-admin"

The first draft of `tg_protect_super_admin_update()` counted *rows* with `role = 'super-admin'`, not *active* ones. That technically satisfies "a super-admin role always exists" but not "a super-admin is always able to act" if the sole remaining one is banned. Fixed by joining `auth.users` and excluding banned accounts from the count — mirrors the equivalent check now added to `set_compte_actif()`.

---

## 3. Recommended improvements

- **`AccountManagement.razor` — pending accounts render with a misleading role.** The `<select>` bound to `account.Role.Code` only listed `AppRole.Assignable` (lecture/gestion/admin/super-admin); a pending account's code (`en_attente`) matched nothing, so the browser would have silently displayed the first option (`lecture`) as if that were the account's real role. Fixed by injecting a disabled `en_attente` placeholder option when `account.IsPending`, plus an explicit "en attente" badge. **Verify:** if this project has a screenshot-based visual test suite, re-run it against `/comptes` with a pending account in the fixture data.
- **`tg_protect_super_admin_update`'s actor-restriction branch is scoped to `current_setting('role', true) = 'authenticated'`.** This is intentional (mirrors how RLS treats `service_role`/`postgres` as trusted operators) but it is a string comparison against the Postgres role name set by PostgREST — if the project ever changes the PostgREST authenticator role name away from `authenticated`, this check silently stops firing. Low risk today (RLS still blocks the same scenario), but worth a one-line note in `db/SECURITE.md` if that role name is ever considered configurable. Not blocking.
- **`db/roles.sql` documents the bootstrap SQL for the first super-admin at the bottom of the file, unchanged.** Worth double-checking that a *fresh* project bootstrap (empty `auth.users`, running `roles.sql` for the first time) still works: the first nomination is an `INSERT ... ON CONFLICT DO UPDATE`, not an `UPDATE`, so none of the three new triggers fire on it (correct — confirmed by reading the trigger conditions, which only act on `DELETE`/`UPDATE`, never `INSERT`). No code change needed, flagging for reviewer awareness only.

---

## 4. Inline comments (equivalent to "GitLab comments")

| File | Line(s) | Comment |
|---|---|---|
| `db/roles.sql` | ~59 | `alter type ... add value if not exists 'en_attente' before 'lecture'` runs unconditionally on every execution of the script, even right after the `create type` that already includes it — correct (idempotent no-op), but worth a smoke-test on a database where the type predates this change, to confirm `ADD VALUE ... BEFORE` accepts an already-first value gracefully (it does, per Postgres semantics: no-op when `IF NOT EXISTS` finds the value already present, and `BEFORE`/`AFTER` are only evaluated when the value doesn't yet exist). |
| `db/roles.sql` | ~292-297 | The "active super-admin" count is duplicated (nearly verbatim) between `tg_protect_super_admin_update()` and `set_compte_actif()`. Both are short and self-contained, and extracting a shared `public.moamat_active_super_admin_count(exclude uuid)` helper would be a legitimate follow-up, but is not worth doing now for two three-line queries — flagging as optional, not required. |
| `db/tests/rls_tests.sql` | new §9 | Test account `a7` is created but never elevated, by design (it's the "stays pending forever" control). Comment already states this — good. |
| `src/MoaMat.Web/Pages/AccountManagement.razor` | select block | The disabled `en_attente` placeholder option is only rendered `@if (account.IsPending)`; once an admin assigns a real role the row re-renders without it on the next `ReloadAsync()` — verified this matches the existing reload-after-write pattern already used by `ChangeRoleAsync`. |

---

## 5. Tests added

- SQL (`db/tests/rls_tests.sql`, new §9, `en_attente` account `a7`):
  - default role after sign-up is `en_attente`, not `lecture`.
  - `en_attente` sees only its own `utilisateur_role` row; zero rows on `bouteille`, `personne`, `audit_log`, `compte_utilisateur`.
  - `en_attente` cannot self-activate (write denied).
  - a lone active super-admin cannot be demoted (role-trigger).
  - demoting a super-admin succeeds once a second active super-admin exists, then fails again once only one remains.
  - a super-admin's `utilisateur_role` row cannot be deleted, even by `postgres`.
  - a super-admin's `auth.users` account cannot be deleted, even by `postgres`.
  - deactivating the last active super-admin via `set_compte_actif` is refused, even when the caller holds a `super-admin` role but is itself banned.
- .NET (`AppRoleTests`, `AccountAdministrationPolicyTests`):
  - `AppRole.FromCode("en_attente")` maps to a role distinct from `AppRole.None`, rank 0.
  - `AppRole.Pending` is excluded from `AppRole.Assignable`.
  - an administrator can change the role of (i.e. activate) a pending account.

### Not covered — flagging for follow-up, not blocking

- No test exercises the **trigger's actor-restriction branch** (`current_setting('role', true) = 'authenticated'`) directly at the SQL level — it's covered indirectly through the existing RLS test ("admin NE PEUT PAS attribuer le role admin/super-admin"), since RLS filters the row out before the trigger even runs. A direct test would need to force `current_setting('role')` to `'authenticated'` while running *as* an unprivileged role, which the current `set local role authenticated` pattern already does — but no scenario currently reaches the trigger with a row that RLS would have let through and the trigger alone blocks. Consider adding one if this branch is ever relied upon as the *primary* defence rather than defence-in-depth.
- No Blazor component test (bUnit or similar) for the new pending-account badge/placeholder in `AccountManagement.razor` — this repository does not appear to have component-level UI tests today (only domain unit tests), so none were added, consistent with existing test coverage shape.

---

## 6. Checkpoint resolved with the tech lead

1. **Model shape for "pending":** distinct `en_attente` role (rank 0, zero permissions) vs. reusing `lecture` with a hidden flag → **confirmed: distinct role**, as implemented.
2. **Backfill behaviour for pre-existing accounts without a role row:** keep defaulting to `en_attente` (consistent with the new trigger) vs. keep `lecture` for backfill only → **confirmed: keep `lecture` for backfill**, only the trigger for new sign-ups uses `en_attente`. Code updated accordingly (§2.1 above).

---

## 7. Optional refactoring

None judged high-value enough to do proactively. The `db/roles.sql` file is getting long (367 lines) — splitting the super-admin-protection section (§6 in the file) into its own `db/superadmin_protect.sql`, executed right after `db/roles.sql`, would keep each file single-purpose and match the project's existing one-concern-per-file convention (`db/audit.sql`, `db/comptes.sql`, etc. are all separate). Not done here to avoid widening the diff and the execution-order documentation (README, SECURITE.md) beyond what was asked; flagging for a future pass if `roles.sql` keeps growing.
