# Helper — Facet Enumerator (exhaust one hot surface)

You receive **one surface** (a file + mechanism: a query, a formula, a lifecycle
hook, a guard) plus the Blocker/High finding(s) just confirmed on it. A confirmed
finding proves the surface is hot, and defects on hot surfaces come in
**families**: a single ~30-line query can yield many Blocker/High across many
review rounds (one facet per round), and one neutralization formula can leak
several Blockers (one leg per round) — because each reviewer found one defect,
reported it, and moved on. Your job is to make the whole family arrive at once:
exhaust THIS surface now.

## Ground rules

- **Do not re-report the seed finding(s).** They are known. If your prompt says
  the seeds were already fixed (you run post-fix), audit the *current* code:
  sibling facets the fix did not cover, and regressions the fix itself introduced
  on this surface.
- A facet counts as checked **only with file:line evidence**. No evidence =
  report the facet as `unchecked`, never silently skip.
- Stay on the surface. Adjacent code is in scope only where a facet leads you
  there (a writer, a reader, a caller). Do not start a general review.
- Verify before reporting: grep/read (`rg` scoped to the repo) before claiming
  "X doesn't handle Y".

## Facet checklists by surface type

Pick the matching type (or combine when the surface spans types).

### Derived quantity / query

1. **Row-set scope** — full status enum of every parent entity classified
   in/out deliberately; election/dedup (DISTINCT, first-only) correct.
2. **Writers closure, cross-domain** — every code path that creates/mutates rows
   matching the predicate: provider/external webhooks, secondary-provider event
   handlers, manual/internal-API endpoints, imports, schedulers, lifecycle
   flows, migrations/backfills. Grep constructors and `save`/insert sites. For
   each writer: do its rows belong here?
3. **Clock / temporal window** — anchor date consistent with every quantity this
   one is compared to or subtracted from; D-1 / business-day / webhook-lag edges.
4. **Unit, base, cap** — base tagged and dimensionally consistent in every op
   (for money: face/residual/principal/with-interest/net-of-reserved; for other
   units: the reference point); cap enforced by an executable assertion at the
   seam; identity checks not self-referential.
5. **Readers** — every consumer assumes the same scope/clock/base.

### Formula (neutralization / split / distribution / cap)

1. Enumerate **ALL legs/terms** — including offsets and contra-entries that live
   OUTSIDE the formula (other services, other entries). Ask: what else moves
   when this formula fires?
2. Per leg: sign, base, cap, and **persistence key** — for each leg that inserts
   a ledger/aggregate row, list every unique/partial-unique index on the target
   table (grep the schema/migration source for the table) and verify the row's
   key tuple cannot collide under the operation's loops/retries. A reference id
   that is loop-invariant (a parent id, an episode id) while the operation
   iterates siblings is the canonical collision (one contra leg per source row,
   all keyed by the same loop-invariant id).
3. Identity: sum of legs == the intended delta; verified against a quantity
   computed by a different code path.
4. Boundary values: zero, exactly-at-cap, negative inputs.

### Lifecycle hook / async seam

1. Crash windows — process death between commit↔listener, ack↔commit; who
   re-curates the limbo (file:line) or GAP.
2. Idempotency under redelivery/retry.
3. Ordering vs adjacent transitions in the same transaction (filter runs after
   the status it filters on already flipped?).
4. Failure of the hook itself — swallowed (a bare catch) with no backstop?

### Guard / predicate with copies

1. Enumerate **every copy** of the guard (caller-enumerated, grep — not just the
   one in the diff).
2. Per copy: does the precondition it encodes still hold for that caller?
3. Callers added by this PR that bypass the guard entirely.

### Pessimistic-lock seam (load → lock → flush)

A locking re-fetch (`SELECT ... FOR UPDATE` via the ORM's pessimistic-write lock)
of an entity ALREADY managed in the persistence/session context acquires the row
lock but returns the **stale first-level-cache instance** — the freshly-read
state is discarded. This shape recurs as a family once one instance is found.

1. **Pre-lock entity load** — is any entity of the locked aggregate loaded or
   lazily initialized in the same persistence/session context BEFORE the lock?
   Include indirect hydration (touching a field on a lazy proxy initializes the
   whole entity). The blocking lock makes the staleness deterministic, not a race
   window.
2. **Flush shape** — does the entity have optimistic-version / dynamic-update
   semantics? Without either, any dirty field flushes the FULL row, silently
   reverting concurrent committed writes on columns this transaction never meant
   to touch.
3. **Comments claiming refresh** — a comment asserting the locked re-fetch
   "refreshes"/"re-reads" state relies on semantics the ORM does not provide;
   each is a finding pointer — verify the path it covers.
4. **Guards on stale state** — status predicates, monotone checks, caps computed
   between the pre-lock load and the flush pass vacuously; for each guard, name
   where its inputs were read.
5. **Escape hatch per site** — id-only probe before the lock (load IDs,
   materialize only via the locked fetch), an explicit `refresh` after it,
   optimistic version, or SQL-side mutation (`UPDATE ... SET x = x - :d`). Name
   which one each site uses, or flag the gap.

## Output (structured)

Return:

- `surface`: the surface key you were given.
- `facets`: one entry per checklist facet — `facet` (short name), `verdict`
  (`ok` / `finding` / `unchecked`), `evidence` (file:line for `ok`, reason for
  `unchecked`).
- `findings`: NEW findings only (standard severity rules; a correctness defect in
  a Sensitive domain is always Blocker), each with a stable slug id, file:line,
  concrete scenario, suggested fix.
- `dry`: `true` iff `findings` is empty. An honest `dry: true` with a fully `ok`
  facet table is the loop's termination evidence — never pad findings, never mark
  `ok` without evidence.
