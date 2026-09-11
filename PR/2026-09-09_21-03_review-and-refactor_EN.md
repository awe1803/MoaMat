# MoaMat — .NET DDD / Hexagonal review and refactor

**Date:** 2026-09-09 21:03
**Branch:** `39-correction-base-bonnes-pratiques`
**Baseline:** `fbe4fd2` (merge of PR #37)
**Scope:** whole repository, brought in line with the DDD / Clean Architecture / .NET review grid, plus "one class per file" and "code in English".

---

## 1. Verdict

⚠️ **Risky before this change — ✅ Acceptable after it.**

The code that existed was clean, well documented and honest about where security really lived (the RLS policies). What made it risky was not sloppiness, it was **structure**: a single Blazor assembly in which business rules lived in component markup, every data-access method swallowed `catch (Exception)` and reported network outages as permission refusals, and nothing at all was covered by a test.

Three defects were reachable in production:

| | Defect | Impact |
|---|---|---|
| 1 | `NavMenu.Initials` indexed an empty string | Unhandled exception, blank navigation menu |
| 2 | `listUsers()` unpaginated in the super-admin edge function | Nomination by e-mail silently fails past the first page |
| 3 | Blank labels accepted into the location hierarchy | Unusable rows created in `lieu_*` |

All three are fixed, and each is pinned by a test or, for the edge function, by an explicit paginated lookup.

The build is green with **warnings as errors**, and **112 unit tests pass**.

---

## 2. Critical issues (production-impacting)

### 2.1 — Every provider error reported as "insufficient rights" 🔴

`ItemService`, `LieuService` and `CompteService` all did:

```csharp
catch (Exception)
{
    return AuthResult.Fail("Enregistrement refusé (droits insuffisants ou données invalides).");
}
```

**What happens if the network drops mid-save?** The user is told they lack permission. They ask an administrator for rights they already have; the administrator finds nothing wrong; nobody looks at connectivity. The exception was never logged, so there is no trace to look at either. `OperationCanceledException` was caught the same way, so a cancelled render reported a permission error too.

**Fixed:** `SupabaseCallGuard` + `SupabaseFailureTranslator` now classify the failure (refused / conflict / unreachable / unexpected), log the untranslated cause once through `ILogger`, and rethrow caller-requested cancellation untouched.

### 2.2 — Authorization rules living in component markup 🔴

`GestionComptes.razor.cs` carried the rules deciding who may change whose role and who may deactivate whom — as five inverted, negatively-named predicates (`RoleSelectDisabled`, `OptionDisabled`, `ActifToggleDisabled`, `IsElevated`, `IsCa`) mixed with rendering state. They mirror rules enforced in SQL, so a drift between the two is invisible until a user hits a refusal the UI said was allowed.

**Fixed:** `AccountAdministrationPolicy` in the domain, stated positively (`CanChangeRoleOf`, `CanAssignRole`, `CanChangeActivationOf`), with every branch pinned by a test — including the ones that must stay refused.

### 2.3 — Unbounded inventory read 🟠

`GetItemsAsync` had no `Limit`. On a phone at the dive site, over a growing inventory, the screen degrades until it stops loading. There is no paging in the UI either, so the cost is paid on every filter change.

**Fixed:** `InventoryFilter.MaxResults` (500 by default, 1000 max, clamped rather than rejected). UI paging is listed as remaining work.

### 2.4 — Open-redirect gap on sign-in 🟠

`Login.SafeReturnUrl` rejected `://` and a leading `//`, but not `\evil` (browsers normalise backslashes to slashes), not `javascript:` / `data:`, and not control characters used to smuggle a scheme.

**Fixed:** `RelativeReturnUrl`, a value object that cannot hold an unsafe value — anything suspicious degrades to the home page. 15 cases pinned.

### 2.5 — Blank labels reaching the database 🟠

`LieuService.AddSectionAsync(libelle)` called `libelle.Trim()` with no validation and no null guard: a whitespace-only input created a location row with an empty label, and `null` would have thrown.

**Fixed:** `LocationLabel`, a value object that cannot be constructed blank or over-long. The repository signature takes `LocationLabel`, so the invalid call no longer compiles.

### 2.6 — `NavMenu.Initials` crashed on an address with no local part 🔴

```csharp
var local = name.Split('@')[0];          // "" for "@club.be"
var parts = local.Split(...);            // empty
return ... : char.ToUpperInvariant(local[0]).ToString();   // IndexOutOfRangeException
```

**Fixed and moved** to `UserInitials.FromDisplayName`, defensive over empty, punctuation-only and local-part-less input.

### 2.7 — Edge function: unpaginated user lookup 🔴

`nominate-super-admin` resolved `target_email` through `asAdmin.auth.admin.listUsers()` with no paging. That call returns the **first page only** (50 users by default), so nominating anyone past it fails with "user not found" — on the single most privileged operation in the system, at the moment the club has grown enough for it to matter.

**Fixed:** paginated lookup, 1000 per page, capped at 50 pages, with the read error distinguished from "not found".

### 2.8 — Edge function: silent partial completion 🟠

**What happens if it crashes mid-process?** The function performs three non-transactional writes: the role row, the `app_metadata` claim mirror, and the audit entry. Neither of the last two had its result checked. A privileged grant could therefore complete **unaudited**, with the caller told everything went fine.

**Fixed:** both results are checked and reported (`claim_mirrored`, `audited`), and a partial completion is logged. The write order is deliberate — the role table is the authority the RLS policies read, so it goes first; a failed claim mirror only means the JWT lags until the next refresh, which is why it is reported rather than turned into a misleading `500`.

**Idempotence** was already correct (an already-super-admin target short-circuits) and is now explicit in the contract, alongside a UUID format check on `target_user_id`.

---

## 3. What changed structurally

### 3.1 — Hexagonal split, enforced by the compiler

| Project | Role | Depends on |
|---|---|---|
| `MoaMat.Domain` | Value objects, invariants, screen-authorization rules, and the **ports** | *nothing* |
| `MoaMat.Infrastructure` | Everything that knows Supabase: PostgREST shapes, mapping, error translation, client construction | Domain + `Supabase` |
| `MoaMat.Web` | Blazor WASM PWA and browser-specific concerns | Domain + Infrastructure |
| `MoaMat.UnitTests` | Unit tests over the core | Domain + Infrastructure |

A separate `Application` assembly was deliberately **not** created. Orchestration here is trivial — screens call a port and render the answer — so a fifth project would be ceremony, not separation. That is the "adaptive strictness" call, and it is reversible the day a use case needs more than one port call.

Domain records are **not** anemic-by-accident: they hold the behaviour that genuinely belongs to them (`AppRole.IsAtLeast`, `InventoryItem.DescribeClubCodeAmbiguity`, `AccountAdministrationPolicy`). What they do not hold is invented business logic — the diving-club rules live in PostgreSQL, and modelling a fake client-side aggregate over them would be worse than honest data carriers.

### 3.2 — Reads and writes fail differently, on purpose

- A failed **read** throws `DataAccessException`, carrying a message already safe to display. Screens catch that one type; no screen references PostgREST.
- A refused **write** returns a failed `OperationResult`. A refusal is an expected outcome the user must act on, not an exceptional condition.

### 3.3 — Leaky abstraction removed

`ItemService.GetChildAsync<T>() where T : BaseModel` forced any consumer of the abstraction to know about PostgREST — the exact coupling the layering exists to prevent. Nothing in the UI used it.

**Decision:** the six specialisation shapes are kept, one class per file, in `MoaMat.Infrastructure/Supabase/Records/` (so no schema knowledge is lost), and are **not exposed through a port** until a detail screen needs them. When that screen arrives, add typed methods; do not resurrect the generic. This is called out in the README under "Reste à faire".

### 3.4 — Fail fast on configuration

`Program.cs` used to *log* an error for a `service_role` key and carry on, and would happily build a client from a blank URL. Now `SupabaseSettings.Validate()` throws on a missing/malformed URL, a missing key, or a privileged key. Refusing to boot is the correct outcome: a blank page is recoverable, a shipped service key is not.

`SupabaseKeyInspector` is a pure function, extracted from `Program.cs` and covered by 8 tests. An **unrecognised** key format is allowed through on purpose — refusing everything unknown would break the day Supabase changes a format, and the deploy workflow carries the second check.

### 3.5 — Conventions

- **One class per file**, verified: no file declares two top-level types, no nested private type remains.
- **Code in English** (types, members, comments); **product in French** (screen labels, displayed errors, routes, SQL identifiers). The line is deliberate: what a club member reads stays French, what a developer reads is English. Role codes (`lecture`, `gestion`, `admin`, `super-admin`) and family codes stay French because they are byte-identical to database values.
- `Directory.Build.props`: analyzers on, `TreatWarningsAsErrors`, `NuGetAudit`.
- `Directory.Packages.props`: Central Package Management.
- `.editorconfig`: file-scoped namespaces, `_camelCase` fields, explicit culture (`CA1305`), broad-catch stays visible (`CA1031`).

### 3.6 — Removed

`Counter.razor`, `Weather.razor` and `wwwroot/sample-data/weather.json` — Blazor template scaffolding, still `[Authorize]`d and still linked from the navigation menu of a production application.

---

## 4. Tests added

112 tests, all passing. Chosen for the decisions that can actually be wrong, not for coverage percentage.

| Area | Cases |
|---|---|
| `AppRole` | every known code, case/whitespace tolerance, **unknown → `None`** (never over-grant), ordering |
| `AccountAdministrationPolicy` | non-admin actor, self-targeting, privileged target, board member, role handout ceiling, super-admin |
| `UserInitials` | **the crash case** (`@club.be`), punctuation-only, empty, digits |
| `RelativeReturnUrl` | absolute, protocol-relative, root-relative, backslash, `javascript:`, `data:`, `mailto:`, control characters, trimming |
| `LocationLabel` | **the blank-insert case**, boundary length, trimming |
| `InventoryFilter` | clamping below 1 and above the cap, defaults |
| `InventoryItem` | each ambiguity reason, both together, flag with no sub-reason, **sub-reason without the flag stays silent** |
| `ItemFamily` | unknown code falls back to itself, ordinal matching, distinct codes |
| `OperationResult` | empty failure message refused, **`default` is not a success** |
| `SupabaseKeyInspector` | secret prefix, publishable prefix, anon JWT, `service_role` JWT, malformed, whitespace |
| `SupabaseSettings` | blank URL, wrong scheme, missing key, **secret key stops start-up** |
| `SqlDateConverter` | round trip, no time component, no zone, nulls |

The suite now runs as a **CI gate before publishing** (`.github/workflows/deploy.yml`).

`global.json` opts into the Microsoft.Testing.Platform runner — the .NET 10 SDK no longer accepts the VSTest path that xUnit v3 executables would otherwise be driven through.

---

## 5. Remaining risks, not addressed here

1. **No optimistic concurrency on `public.item`.** `UpdateItemAsync` sends the whole row; two people editing the same cylinder means last-write-wins with no warning. Fixing it needs a schema change (a version token, or a `maj_le` precondition), which belongs in a database migration, not in this refactor.
2. **Blazor WASM release trimming vs. reflection-based mapping.** PostgREST maps through Newtonsoft reflection over `[Column]`-attributed properties. Trimming can remove properties it cannot see being used. Pre-existing, unchanged by this work, and apparently working — but it is the kind of thing that breaks silently on a package upgrade. Worth a `TrimmerRootDescriptor` or a published-output smoke test that actually reads a row.
3. **SQL and migration scripts keep French comments.** `db/*.sql` annotates French identifiers; translating the prose would decouple comment from identifier, and editing scripts that have already run against a live database carries risk disproportionate to the benefit. Deliberately out of scope — say the word if you want it done as its own change.
4. **`Access-Control-Allow-Origin: *`** on the edge functions is acceptable today because authentication is bearer-token, never cookie. Documented in `cors.ts`. It stops being acceptable the moment `Allow-Credentials` appears.
5. **No component tests** (bUnit). The screens are thin now that the rules moved out, so the value is lower — but the account screen's rendering of the policy is untested.

---

## 6. Optional refactoring, only if it earns its place

- **Paging in the inventory screen.** The query is bounded now, so the failure mode is "you silently see the first 500" — better than before, still not right. Worth doing when the inventory approaches that number.
- **`IReadOnlyList<T>` → `IAsyncEnumerable<T>`** for the audit trail if it ever grows past a screenful. Not now.

---

## 7. Verification performed

```
dotnet build MoaMat.slnx        →  0 warnings, 0 errors (warnings-as-errors on)
dotnet test  MoaMat.slnx        →  112 passed, 0 failed, 0 skipped
dotnet publish -c Release       →  succeeds
```

Not verified: runtime behaviour against a live Supabase project. Every adapter change is a mechanical translation of a call that already worked, but the account, inventory, locations and audit screens should be exercised once against real data before merging — the renamed PostgREST columns are the place a typo would hide, and no unit test can catch that.
