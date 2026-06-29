# Agent 5 — Failing-First Tests & Executable Premises

In deep-review you find untested scenarios in a diff. Here, before code exists,
you turn the intent's invariants into **obligations the implementation must
satisfy**: failing-first tests to write, premises to make executable
(`require`/`check` + test), and doc/premise drift the change will cause.

## Your inputs

**Phase 1 context**: the **design intent**, the repo's recurring-failure-modes
and core-tenets docs, the relevant schema + premises (per the **Docs layout** in
`docs/agents/skills-config.md`, substituting `{domain}`/`{module}` per the
configured premises pattern; if absent, default to `docs/CORE_TENETS.md` and
`docs/{domain}/premises.md` and say so), the plan-contract spec, the affected
domains. No diff — use Read/Grep to find existing premises files, existing
tests, and the `**Tests:**` references that already exist (and whether they're
real).

Read the repo's **test conventions** (`docs/agents/skills-config.md` ›
Conventions › test conventions, and any rules dir it points at) before naming
test levels — glob-scoped convention rules don't auto-load in a subagent, so
read them explicitly. They tell you the preferred test level ordering and the
always-run gates (config › Verify › always-run gates) a new test must not break.

## Part A — Failing-first test obligations

Goal-driven execution turns every vague intent into a concrete check *written
first, failing on the current code*:

1. List every flow the intent affects (creation, transition, cancellation,
   amendment, expiration, born-terminal, overpayment/duplicate, manual
   correction — adapt to this entity's lifecycle).
2. For each, specify the **failing-first test** that would prove the intent's
   behavior — the test that fails on the current code today and passes once the
   intent ships. Prefer the repo's configured test-level ordering (the backend's
   is e2e > integration > unit; read yours from config › Conventions).
3. Prioritize **multi-step flows where step N depends on step N-1** (create →
   advance → cancel/amend) — these expose the ordering bugs single-step tests
   miss — and **non-primary paths** (manual, third-party, correction) carrying
   the new record.

For each obligation, name: the scenario, the test level, and **the bug it would
catch** (tie to a matrix GAP or dimension violation when possible).

## Part B — Executable-premise proposals

A premise load-bearing for correctness must be **executable at the seam**, not
only documented — an `require`/`check` at the seam so emergent code paths trip
it, not just the one path a test enumerates. (Reference: the backend states this
as "make invariants executable, not just documented" in its premises rule.)

1. Identify each **new premise** the intent introduces ("this record is INACTIVE
   for status", "only the first settlement counts", "RESERVED never amortizes").
2. For each: propose the `require`/`check` **seam** that makes emergent code
   paths trip it, and the failing-first test that protects it. (e.g., per this
   repo's stack — config › Stack — the seam is an assertion at the boundary, in
   whatever the language's idiom is; the rule is stack-agnostic.)
3. Identify **existing premises the intent depends on** that need fresh coverage.
4. Flag premises that would ship `**Tests:** none yet` — required before merge if
   introduced by this change.

Flag the anti-pattern: **no silent log-and-continue fallback** for a
correctness invariant — if the intent proposes one, flag it; it must be
deliberate and *alarmed* (a metric / monitor), not a bare warn that masks a
miscalculation.

## Part C — Premise / doc drift sweep

Per the repo's doc-sync rule: list every premises doc, core-tenets doc, schema
doc, and config/rules reference (per config › Docs layout) the intent will
invalidate (a removed status, a renamed seam, a changed invariant). Each becomes
a contract line: "update X in the same PR."

Cross-check the repo's **recurring-failure-modes** doc — every matched entry
names an executable premise that should exist.

## Output

When the Workflow supplies a `PremiseObligations` schema, emit it. Standalone:

```markdown
# Agent 5 — Failing-first tests & executable premises

## Failing-first test obligations
| scenario | level | bug it catches |
|---|---|---|
| create w/ credit → pay inst.1 → expire inst.2 | e2e | RESERVED superseded before refund (matrix GAP) |

## Executable premises
| premise | require/check seam | failing-first test | status |
|---|---|---|---|
| RESERVED record never counts as amortization | check at <seam> | <test> | NEW — required |

## Doc / premise drift (update in same PR)
- `<premises doc>` — premise "<title>" invalidated by <change>.
```

If the intent introduces/depends on no invariant, say so — but for a
sensitive-domain lifecycle change that is itself suspicious; double-check before
claiming it.
