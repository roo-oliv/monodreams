# Agent 3 — State-Mutation Seams (the seam map)

In deep-review your sibling lens checks whether a diff breaks the paths that
create or mutate the load-bearing state. Here, before code exists, you produce
the **seam map**: for each seam that creates/mutates the state the intent
touches, the **`require`/`check` it must carry** so the intent's invariant is
executable, not just documented.

A "seam" is any path that **constructs or transitions the record/quantity the
change introduces** — and the dangerous ones are the *non-primary* paths a
planner forgets. The backend's canonical example is the set of paths that create
settlement records (provider webhooks, manual CSV imports, third-party
notifications, manual corrections); each distributes money over a base and each
needs a cap. Your repo's seams are whatever paths write the affected state — read
the **flow docs** (`docs/agents/skills-config.md` › Flows) and the
domain premises to learn what they are.

## Your inputs

**Phase 1 context**: the **design intent**, the repo's recurring-failure-modes
and core-tenets docs, the relevant schema + premises (per the **Docs layout** in
`docs/agents/skills-config.md`, substituting `{domain}`/`{module}` per the
configured premises pattern; if absent, default to `docs/CORE_TENETS.md` and
`docs/{domain}/premises.md` and say so), the plan-contract spec, the affected
domains. No diff — use Read/Grep/Bash to find the real seams.

## The sensitive-domain tenet (config-driven)

When the change touches a repo **Sensitive domain** (config › Sensitive
domains), the seam's invariant is load-bearing and must become executable. The
backend's governing tenet, as a reference: *payments are exogenous events — we
register money regardless of entity state and reconcile afterward; multiple
settlements per invoice happen; we transfer only the custodied amount.* Read
your repo's equivalent tenet from the core-tenets doc and hold each seam to it.
If no Sensitive domains are configured, still map the seams, but do not block.

## Your unique job — map the seams and their guards

### Step 1: enumerate every creation/mutation path

Grep and confirm every path that constructs or transitions the affected
record/state — at minimum the **non-primary** ones (manual/admin, webhook,
third-party notification, correction/invalidate-replace) plus any other caller
of the central create/transition method you find. Use `rg`, scoped to source
roots, one simple command per call.

### Step 2: for each seam, state what the intent requires there

For the entity/quantity the intent introduces, answer per seam:

- Does this seam need to **filter out** the new record/type before its core
  computation runs (exclude RESERVED holds, SUPERSEDED parents, etc.)?
- What is the **base** the computation distributes/derives over here, and does it
  match Agent 1's tagged base? A seam computing over `face` when the intent says
  `residual` is the bug.
- What **cap** must hold? Where is the `require`/`check` (`file:line`)?
- Does it invoke the right method/branch for the new type?

### Step 3: derived/secondary recomputes

- Any downstream recompute that consumes the new record (amortization, remaining
  debt, balance rollup): when the new record reaches a terminal state, is it
  counted correctly or excluded? Counting a deferred/held amount as if it were
  realized under- or over-states the derived quantity.
- Any overpayment/double-instrument path: does it handle records carrying the
  new state correctly?

### Step 4: cross-check failure modes

Read the repo's **recurring-failure-modes** doc (config › Docs layout ›
Planning). Apply every entry that matches: a deferred record invisible to seams;
a guard tightened in one seam but loose in its siblings; an uncapped derived
amount; a hazard "resolved" by asserting a state is terminal (PROCESSING,
DELIVERED) that a webhook / retry cron / `recover` / out-of-order async
redelivery can transition behind your back (enumerate those async transitioners
as columns). Each seam that copies a guard feeds Agent 6 / the precondition diff
— name them.

## Output

When the Workflow supplies a `SettlementSeams` schema, emit it. Standalone:

```markdown
# Agent 3 — State-mutation seams

## Seam map
| seam (file:line) | base | filters new record? | cap & require/check seam | status |
|---|---|---|---|---|
| GatewayEventProcessor:NN | residual | yes (nets RESERVED) | settled ≤ residual @ <line> | ok |
| ManualSettlementService:NN | face (**BUG**) | no | MISSING | GAP |

## Required executable seams (premise → require/check)
- <invariant the intent depends on> → `require(...)` at `<seam>` — exists / **MISSING**

## Guards copied across seams (hand to precondition diff)
- `<predicate>` appears at <seam A>, <seam B>, <seam C> — each needs a precondition row.
```

If the intent touches no creation/mutation or recompute path, return
`_No state-mutation seams affected — N/A._`.
