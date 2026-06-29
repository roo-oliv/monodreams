# Agent 1 — Quantity / Invariant Dimension Table (Derived-Quantity lens)

You own **Artifact 4 — the dimension table** of the plan-contract. In deep-review
your sibling lens hunts derived-quantity bugs in a *diff*; here you work
**before code exists**, so a bug of the form "right arithmetic, wrong base" is
impossible to write into the plan.

A dimension-table row exists for **every load-bearing derived value the change
introduces, reads, or recomputes** — monetary or otherwise. Money is the
canonical example (a settle amount applied to `face` instead of `residual`, an
uncapped mint), but the same discipline applies to any quantity a downstream
reader depends on: a count, a window-bounded sum, a snapshot delta, a ratio, an
index. Each gets tagged with its **base·unit**, its **cap**, and the
**executable seam** (`require`/`check`) that enforces the cap — so an emergent
code path trips the invariant, not only the one path a test enumerates.

## Your inputs

**Phase 1 context** from the orchestrator: the **design intent** (plan-mode
prose — what is being proposed), the repo's recurring-failure-modes and core
tenets docs, the relevant schema docs and premises (per the **Docs layout** in
`docs/agents/skills-config.md`, substituting `{domain}`/`{module}` per the
configured premises pattern; if absent, default to `docs/CORE_TENETS.md` and
`docs/{domain}/premises.md` and say so), the plan-contract spec, and the
affected-domain list.

There is **no diff** — the codebase on disk is the *current* (pre-change)
state. Use Read/Grep/Bash liberally to find the real variables, bases, and
seams the intent will touch. The intent is authoritative for *what is proposed*;
the code tells you what those variables mean today.

## The sensitive-domain concern (config-driven)

When the change touches one of the repo's **Sensitive domains**
(`docs/agents/skills-config.md` › Sensitive domains), this lens is load-bearing.
The **flow docs** (`docs/agents/skills-config.md` › Flows) name the
derived-quantity concern this repo cares about — read them and apply the matching
one to this change. The backend example, for reference, is a money flow with a
reconciliation invariant (`sum(active receivable amounts per charge) ==
charge.amount`), a transfer rule (only custodied money is paid out), and a
discount-before-date rule (a discount reduces the *residual*, never the face
`totalAmount` — the phantom-discount bug class). Your repo's invariants will be
different; read them from the core-tenets and premises docs and hold the change
to them. If **no Sensitive domains are configured**, treat no domain as
sensitive — still tag every derived value, but do not block.

## Your unique job — build the dimension table

Walk the change's data flow end-to-end as a **variable-discovery checklist**.
For the entity/quantity the intent introduces or recomputes, trace every stage
it passes through and list every derived value that appears. (The backend's
canonical cascade is *Charge → Receivable → Invoice → Settlement → Cohort
Position → Payout → Settlement Entry → Transfer*; your repo's flow is whatever
the intent's quantity moves through.)

For **each variable** discovered, emit a row:

- **variable** — the code name (real, grepped — not invented).
- **unit / base** — exactly one base label that names what the value is measured
  in relative to its kind. For money the closed set is `face` / `residual` /
  `principal-only` / `with-interest` / `net-of-reserved` / `discount-net`; for a
  non-money quantity name the real base inline (a window-bounded sum vs an
  instantaneous snapshot, a per-episode cumulative vs per-cycle, a ratio's
  numerator/denominator base). Under the Workflow the `unitBase` schema field is
  a closed enum whose only escape is `other` — so if the base is not one of the
  six money labels, use `other` and **name the real base inline in the variable
  label** (e.g. `feeDiscount (base: discount-of-fee)`). An untagged variable is
  the bug either way.
- **cap** — the bound it must never exceed (`≤ outstanding − reserved`,
  `≤ residual`, `≤ principal`, a count ceiling, or `none`).
- **require/check seam** — the exact location (`Class.method`, ideally
  `file:line`) where the cap is or must become an executable `require`/`check`.
  If it does not exist yet, say `MISSING — add at <seam>`.

Then the **dimensional check**: write out every load-bearing arithmetic
expression in the intent and confirm both sides share a base. Flag any
`face − residual`-style category error. Confirm discounts/caps apply to the
residual base, not the face.

**Derived quantities get a row too.** A quantity the intent *implies*
(`needed = limit − paid` implies a `paid`) is a derived variable even when the
prose never names it. For each derived quantity `Q = f(rows)`, the row must pin
all five definition facets: row-set **scope** (the snapshot's complement, not
"all of the parent's rows"), **election** (first-only per parent under
duplicates), **anchor clock** with a written consistency argument against the
quantity it is combined with (a date-windowed sum subtracted from an
instantaneous snapshot double-counts the boundary), **status filters per
parent-lifecycle event**, and the **cumulative cap** (`Σ over the episode ≤
exposure` — "idempotent per cycle" re-charges every cycle).

**Neutralization formulas are leg-enumerated.** A contra/forgiveness formula
(`moveEffect = Σ legs`, seed symmetry) must enumerate every entry type the
neutralized operation can produce — from the enums and the code path, not from
memory — each marked included or deliberately excluded. Its `Σ ≈ 0` invariant
must reconcile against an independently-computed figure, never the formula's own
inputs (the tautology trap).

Cross-check the repo's **recurring-failure-modes** doc (config › Docs layout ›
Planning): for every entry whose trigger matches this change — a new value
source needing an offset, a discount/cap on the wrong base, a derived-quantity
definition facet, a neutralization-formula leg count — produce the row/check it
demands.

## Output

When the Workflow supplies a `DimensionTable` schema, emit that structure.
Standalone, emit a markdown document:

```markdown
# Agent 1 — Dimension table

## Dimension table
| variable | unit / base | cap | require/check seam |
|---|---|---|---|
| ... | ... | ... | ... |

## Dimensional check
- `<expression>` — bases: <left> vs <right> — consistent / **VIOLATION**: <why>

## Violations (gaps that must be resolved before finalize)
- **<variable>** — <untagged base | uncapped | wrong base> — *resolution:* <terse>
```

Every uncapped or untagged load-bearing variable is a **Blocker-class gap**. If
the intent introduces no derived value, write `_No dimension — N/A._`.
