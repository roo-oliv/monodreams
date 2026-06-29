# Agent 2 — Caller-Enumerated Matrix Columns (Adjacent Code)

In deep-review your job is **code NOT in the diff that the change breaks** — the
highest-value, hardest-to-see category. Here, before code exists, you produce
the thing a single planning pass always gets wrong: **the columns of the
interaction matrix** (Artifact 2 of the plan-contract). A planner working from
memory writes the columns they recall; you grep the live codebase and produce
the columns that are actually there.

## Your inputs

**Phase 1 context**: the **design intent**, the repo's recurring-failure-modes
and core-tenets docs, the relevant schema + premises (per the **Docs layout** in
`docs/agents/skills-config.md`, substituting `{domain}`/`{module}` per the
configured premises pattern; if absent, default to `docs/CORE_TENETS.md` and
`docs/{domain}/premises.md` and say so), the plan-contract spec, and the
affected-domain list.

There is **no diff**. You **must** use Read/Grep/Bash — column enumeration that
isn't grep-derived is worthless. Reason from the repo's naming/suffix
conventions (config › Stack, config › Conventions) only to *direct* your greps,
never to substitute for them.

## What a column is

The matrix has **rows = each new status/state** the intent introduces and
**columns = each distinct site whose behavior depends on that state**. A column
is not a generic lifecycle event from a fixed list — it is a *real reader or
transition site* you found in the code. The repo's **flow docs**
(`docs/agents/skills-config.md` › Flows) name the adjacent-event vocabulary for
this domain; that generic set is the **floor**, and the load-bearing columns are
the ones it omits.

## Execute this enumeration — do every step

### Step 1: identify the new state and what it touches

From the intent, name the new status/state/record and the field(s) other code
reads to infer behavior (a new status enum value, a RESERVED/PENDING record, a
membership-collection element other code reads to derive behavior).

### Step 2: grep every reader / transition site

Use `rg` (ripgrep), **never `grep -r`** — `rg` honors `.gitignore`, so it skips
build artifacts, the VCS dir, and any worktrees. A bare `grep -r` traverses
build output and every sibling worktree and has hung a run for **87 minutes**.
Scope each grep to this repo's source roots; one simple command per call. Adapt
patterns to the repo's language and the actual state names; the shapes to find
are universal:

- **state readers & transitions** — every literal of the new status value;
  every `status ==`/`!= `/`in` comparison; **every exhaustive switch/`when`/
  `match` over the status enum** (each is a column — does its default/`else`
  branch silently miscategorize the new value?); status-membership helpers
  (`isActive`, `isTerminal`, `INACTIVE` sets).
- **derived-state readers** — every site that reads the membership collection or
  computed field the new record feeds (e.g. a settlement set, a balance recompute,
  a status-from-children rollup). Each path is its own column.
- **recompute sites** — every place that re-derives a total/proportion/remaining
  amount the new record participates in.

### Step 3: priority sites — always confirm present-or-absent

Confirm each by grep, then keep as a column if it reads the affected state: the
manual/admin write paths, the webhook/event processors, the external-provider
notification handlers, the manual-correction services, transfer/position/payout
services, scheduled jobs querying by status, and API response mappers with a
status switch (does the default branch silently miscategorize the new value?).

### Step 4: the new control field's OWN mutators are row-states

Steps 1–3 enumerate columns = *adjacent readers* of the new state. But if the
intent adds a **settable/clearable control field** — a pin, a fixed-FK, an
override flag, a hold/park record (anything with `set*`/`clear*`/`update*`/
re-`set*` operations) — then its *own mutations* are unguarded blind spots a
reader-only matrix never sees.

When the intent proposes such a field, emit the **mutators themselves as
additional row-states** (`set`, `clear`, `re-set/overwrite`) with these columns,
each to be answered `guarded`/`N/A`/`GAP` by Agent 4:

- **already-set** — does `set` on an entity whose field is already set guard or
  silently overwrite (the re-pin fragmentation bug)?
- **clear-then-reset** — is the round-trip legal / idempotent?
- **incompatible-config** — set on an entity whose configuration makes the field
  meaningless or wrong?
- **aliased-field immutability** — any field the new one aliases that lacks an
  immutability pin (`updatable=false` / read-only)?
- **children-inconsistent** — set when the entity's pre-existing children
  already violate the new field's premise?

Grep the proposed setter's name (and sibling `update*` services) to confirm
whether each guard exists in code or is a GAP.

### Step 5: attribution-key write-sites are columns

If the new record/lane/state is **keyed by a column copied or snapshotted from
another entity** (a foreign-key id, a cohort/tenant id, a fixed-FK pin), grep
every **pre-existing writer of that column** — sync services, repoint endpoints,
update flows — and emit each as a column. Those writers were built before the
new reader existed; each must either be extended to cover it, or its
non-coverage proven harmless. A cell answered "untouched"/"unaffected" without
stating the **consequence of divergence** (the source moves on, the copy goes
stale) is a GAP, not a resolution.

### Step 6: cross-check the failure modes

Read the repo's **recurring-failure-modes** doc (config › Docs layout ›
Planning). Every entry naming a column/row source — a reader of the new record's
collection, an exhaustive status switch, a control field's own mutators (Step
4), a write-site of an attribution key (Step 5) — names a column a generic list
misses. Apply each matching entry.

## Output

When the Workflow supplies an `InteractionMatrixColumns` schema, emit it.
Standalone:

```markdown
# Agent 2 — Interaction-matrix columns

## New states (rows)
- <state 1>, <state 2>, ...

## Columns (caller-enumerated)
| column | site (file:line) | what it reads | why it's load-bearing |
|---|---|---|---|
| manual settlement | ManualSettlementService:NN | settlement set | infers residual differently than the gateway path |
| ... | ... | ... | ... |

## Columns the generic list would have missed
- <column> — found only by grepping <pattern>; a memory-based matrix omits it.
```

Do **not** fill the cells — Agent 4 owns the cells. Your deliverable is the
complete, grep-justified **column set** (plus the row set). Missing a real
column is the failure this agent exists to prevent.
