---
name: deep-plan
description: "Planning-time mirror of deep-review: fills and adversarially refutes a plan-contract (interaction matrix, dimension table, precondition diff) from design intent + the live codebase, before code exists. Auto-engages in plan mode for sensitive-domain status/lifecycle/flow-replacement changes; self-tiers to a light inline pass for trivial or non-sensitive plans."
argument-hint: "[intent file path] [cheaper]"
---

`deep-plan` is `deep-review` run **before** code exists. Where deep-review takes a *diff* and emits *findings*, deep-plan takes a *design intent* (plan-mode prose) + the *live codebase* and emits a **filled, refuted plan-contract** — the four artifacts in the repo's plan-contract spec (`docs/agents/skills-config.md` › Docs layout › Planning; default `docs/planning/plan-contract.md`): Contract block, Interaction matrix, dimension table, Precondition diff — with every matrix cell answered, every derived quantity's base tagged, and every guard copy diffed. That artifact is exactly what `/verify-plan` later reconciles the implementation against.

**Why it exists.** The review side is industrial (lenses + consolidator + facet enumeration). The planning side was a single pass — so the matrix ships with the columns the planner remembered and empty cells, the dimension table with the variables they happened to name, the precondition diff covering the one guard copy they recalled. The same bug class — a deferred/RESERVED record invisible downstream — shipped twice and still needed many review rounds the second time: the missing piece is institutional learning at plan time, not another reviewer. deep-plan points the same specialists that catch these bugs in a diff at the *plan*.

Invocation:

- `/deep-plan` — use the current plan-mode draft (the prose in this session) as the design intent.
- `/deep-plan path/to/draft.md` — read the design intent from a file.
- Append `cheaper` (aliases: `cheap`, `simple`, `eco`, `economy`) for tiered model routing. Default is all-Opus.

This skill reads the repo's `docs/agents/skills-config.md` for everything stack-specific (where docs live, which domains are sensitive, lenses, conventions). When a section is absent it falls back to a stated default and says so in its output.

---

## Model Routing

**Default (full):** all phases and agents use **Opus** with extended thinking.

**Economy mode** (`cheaper`/`cheap`/`simple`/`eco`/`economy`) — mirrors deep-review's tiers, since the agents are the same specialists reframed:

| Phase / Agent | Model | Reason |
|---|---|---|
| Agent 1 — Dimension Table | **Opus** (extended thinking) | Deep multi-stage reasoning; base/cap correctness |
| Agent 2 — Caller-Enumerated Matrix Columns | **Sonnet** (extended thinking) | Structured grep + enumerate pattern |
| Agent 3 — State-Mutation Seams | **Opus** (extended thinking) | Complex multi-path mutation logic |
| Agent 4 — Matrix Cells / Lifecycle | **Sonnet** (extended thinking) | Sequential lifecycle against an explicit checklist |
| Agent 5 — Failing-First Tests & Premises | **Sonnet** | Semi-structured obligation analysis |
| Agent 6 — Refuter | **Opus** (extended thinking) | Adversarial; independence is the whole value |

Consolidation (Phase 6) and the Gate always use **Opus** regardless of mode.

---

## Phase 0: Scope tiering

Parse arguments for the **model tier** (detect and strip `cheaper`/etc.). Then classify the change — this decides whether deep-plan fans out at all.

Read the repo's **Sensitive domains** (`docs/agents/skills-config.md` › Sensitive domains). A change touching ANY of them is the bug-prone class this skill exists for. **If none are listed, treat no domain as sensitive — take the light path and never block** (and say so in the output).

**The self-critique paradox: do not over-critique easy tasks.** A heavy enumerate→refute→gate loop on a one-line config tweak is noise that teaches the user to ignore the skill. Tier honestly.

Classify the design intent:

- **TRIVIAL / NON-SENSITIVE** → **light inline pass.** The intent does not touch a Sensitive domain, or touches one only cosmetically (rename, doc, dependency bump, a single non-stateful field). No fan-out. Do Phase 1 context-gathering yourself, then fill the artifacts the change actually needs (often just the Contract block) inline, in one pass. Note explicitly: "Light pass — no sensitive-domain lifecycle change detected." (When the config lists no Sensitive domains at all, every plan takes this path — say "Light pass — no Sensitive domains configured.")

- **SENSITIVE-DOMAIN + STATE/LIFECYCLE/FLOW-REPLACEMENT** → **heavy pass (Workflow).** The intent touches a Sensitive domain (config › Sensitive domains) **and** adds/modifies an entity status or lifecycle, introduces a state machine, or replaces an existing flow/method. Run the deterministic Workflow `.claude/workflows/deep-plan.js` via the Workflow tool (Phases 2–6 below execute inside it). **Always pass `args: { intent, domains, repoRoot }`** where `repoRoot` is the current worktree root (`git rev-parse --show-toplevel`) — agents scope every search to it, and resolve their role files as `${repoRoot}/.claude/skills/deep-plan/agents/…`. (Pass a normal object: the runtime delivers Workflow `args` as a JSON-encoded *string*, and `deep-plan.js` defensively `JSON.parse`s it, so no wrapper is needed — an un-parsed string silently zeroes the intent and runs the whole pass on a blank brief.) **Preflight first** (see Concurrency below): quarantine stale planning artifacts so no agent anchors on another run's file.

When genuinely unsure between tiers, prefer the heavy pass for sensitive domains and say why — a false-heavy is cheap; a false-light ships the gap.

---

## Phase 1: Context gathering (both tiers)

Gather and pass to every agent (read paths from `docs/agents/skills-config.md` › Docs layout, substituting `{domain}`/`{module}` per the configured premises pattern; if a path is absent, default to `docs/CORE_TENETS.md` + `docs/{domain}/premises.md` and say so):

1. **The design intent** — the plan-mode prose (or the intent file). This is the analogue of deep-review's diff: the source of truth for *what is being proposed*. There is no diff; the codebase on disk is the *current* (pre-change) state.
2. **The recurring-failure-modes doc** (config › Docs layout › Planning) — **mandatory when present.** Every entry whose trigger matches the change is a question the plan must answer. This is the mechanism that makes a new plan consult past lessons. If the repo has no such doc, note its absence and rely on the core tenets + premises.
3. **The core-tenets doc** (config › Docs layout › Core tenets; default `docs/CORE_TENETS.md`) — invariants every agent checks the intent against.
4. **Affected domains** — detect each changed/affected file's domain via **Domains** (config › Domains — path-glob → domain). For each, read its schema doc and premises (config › Docs layout).
5. **Touched flows** — from the flows dir (config › Flows, default `docs/flows/`), read the flow doc of every flow the intent touches (frontmatter `covers:` globs). A flow doc is the dedicated core-tenet for that flow — its invariants and load-bearing quantities are exactly what the dimension table and lifecycle matrix must hold the change to.
5. **The plan-contract spec** (config › Docs layout › Planning; default `docs/planning/plan-contract.md`) — the artifact spec the output must conform to.
6. **The repo conventions** — `docs/agents/skills-config.md` itself plus any rules dir it points at (config › Docs layout › rules dir).

**Pass to all agents:** the design intent, the recurring-failure-modes doc, the core-tenets doc, relevant schema + premises docs, the touched flow docs, the plan-contract spec, the affected-domain list. Agents have full file tools (Read, Grep, Bash) and **must use them** — unlike deep-review, the codebase is the *current* state, so an agent's job is to discover, in real code, the callers/guards/readers the intent will collide with.

---

## Phase 2: ENUMERATE (heavy pass)

Before analysis, build the real enumeration the plan-contract needs — this is what a single planning pass skips. Two sweeps (Agent 2 owns the first, Agent 3/Agent 1 feed the second):

- **Interaction-matrix columns.** Grep every site that reads or transitions the state the new record touches (the membership collection it feeds, the central create/transition method, status-from-children rollups, `findByStatus`, exhaustive `when(status)`/`switch`/`match` blocks, status-membership helpers). Each distinct reader becomes a **column**. A generic adjacent-event list is the floor, not the column set — see the plan-contract spec Artifact 2.
- **Guard copies.** For every guard/gate/predicate the intent tightens or copies, grep **all** copies across the codebase. Each copy becomes a precondition-diff row.

Output of this phase is the *column set* and the *copy set* — concrete, codebase-derived, feeding Phases 3–4.

## Phase 3: ANALYZE (heavy pass)

Launch the specialists. Each reads its role file under `.claude/skills/deep-plan/agents/` and produces a **structured contract slice**, not prose findings:

| Agent | Role file | Owns |
|---|---|---|
| 1 — Dimension Table | `agents/dimension-table.md` | Artifact 4: variable→unit/base→cap→seam (money is the canonical kind; any load-bearing derived value qualifies) |
| 2 — Caller-Enumerated Matrix Columns | `agents/matrix-columns.md` | Artifact 2 *columns* (the enumeration) |
| 3 — State-Mutation Seams | `agents/state-mutation-seams.md` | every state-creation/mutation seam + the `require`/`check` each needs |
| 4 — Matrix Cells / Lifecycle | `agents/lifecycle-matrix.md` | Artifact 2 *cells*: `handled`/`N/A`/`GAP` per (state × event) |
| 5 — Failing-First Tests & Premises | `agents/test-coverage.md` | failing-first test obligations + executable-premise proposals + premise/doc drift |
| 6 — Refuter | `agents/refuter.md` | (Phase 4 — runs against the draft only) |

Agents 1 and 3 carry the **flow-specific concern** — they read the repo's **flow docs** (config › Flows) for the invariants this repo's key flows depend on and hold the change to them. The backend's flows (e.g. a `payment-pipeline` doc tagging every value's base/cap; a `dsa-lifecycle` doc listing a new status × every adjacent entity event) are the *example* of what the flow docs contain; your repo's are whatever it declares.

## Phase 4: REFUTE (heavy pass) — loop-until-dry

Agent 6 (the refuter) gets **only the draft contract**, not the reasoning that produced it — anchoring-free. Its job: find one unhandled scenario, one dimension/cap violation, or one wrongly-`handled` cell. Refute-or-promote. Each refutation carries an **`attacks` tag** — `resolution` (it breaks a fill/fix the draft already contains) vs `new-surface` (it names territory the draft lacks) — which the verdict aggregates per round to tell fix-attack equilibrium apart from undiscovered surface. The Workflow runs **`REFUTERS_PER_ROUND` refuters in parallel per round** (each assigned a **distinct attack lens** — quantity/dimensional · async-ordering/lifecycle · premise/invariant · completeness/wrong-cell — plus the list of already-open roots, so the N refuters cover N near-independent surfaces instead of colliding on the same targets or re-raising a known GAP), and loops until **2 consecutive rounds surface nothing new** or it hits `MAX_REFUTE_ROUNDS`; each surfaced gap reopens the relevant cell/row and forces Phase 3's owner to resolve it. Independence is the point — it breaks the cascade where planner and reviewer share a blind spot.

**Resolution re-refute (when the loop ends at the round cap).** Ending at `MAX_REFUTE_ROUNDS` right after a resolve means the **final resolver patch was never attacked** — every earlier patch was re-attacked by the next round, but the last one ships unexamined. The Workflow runs **one targeted pass** (`RESOLUTION_REREFUTERS`, default 2, `ARGS.reRefuters` overrides) scoped to ONLY those final resolutions — not a full re-sweep — and integrates any break with a single resolver call. The regress ends there by design: the verdict reports the outcome ("all held" is a real signal; "N fresh, integrated" means the re-refute's own patch ships unattacked — weigh it accordingly, and reach for an independent re-run if it matters).

## Phase 5: GATE (heavy pass) — programmatic completeness

The Workflow's `Gate` phase flags any of:

- an interaction-matrix cell that is **empty or `GAP` without written justification**;
- a load-bearing premise (correctness-critical) with **no `require`/`check` seam or no failing-first test**;
- a touched guard with **no per-copy precondition diff**.

It runs a **bounded justify loop** (`GATE_JUSTIFY_ROUNDS`): each pass is fed the gate violations *plus* the deterministic missing-cell list (`missingCells`), so when a refuter grew the matrix with a new column/state the justify pass fills the **whole** new row/column, not just the cell the refuter named. After the loop, any residual GAP is **recorded in the result (`gate: FAIL`, `residualGaps`, `violations`) and surfaced at the top of the body — never thrown.** deep-plan never blocks (Phase 7): a throw here once lost ~3M tokens / 2h on 91 empty cells a single justify pass left after matrix growth, *before synthesize ever ran*. A flagged contract is incomparably more useful than a 0-byte output. (Seed mode keeps its own throw for the gate-detects-incompleteness test.)

## Phase 6: SYNTHESIZE (heavy pass)

The contract is rendered **deterministically in JS from the gated draft**, not retyped by an LLM. `renderVerdict()` builds the verdict header — a count of any **`### ⚠️ Contradiction`** themes the consolidator flagged (surfaced FIRST — the gate checks completeness, not *consistency* between two commitments, so a contract that says "source X from A" beside "source X from B" passes the gate; this leads with it); the substantive-GAP count plus its **clustering** into distinct code seams + the Síntese decision-theme count (so the headline reads ~decisions, not ~cells — most cells are one un-built surface repeated across rows); the refute-convergence status with the **per-round trajectory shape** (`[11, 11, 8]` → FLAT = surface far from exhausted, re-run; `[11, 5, 1]` → DECAYING = a round or two more would close it) plus the **final-round mix** of resolution-attacks vs new-surface (a FLAT shape with a high resolution share means the loop is *generative* — each resolver patch mints new attack surface — not that the original surface is unexplored) and the **resolution re-refute outcome**; the reframed gate verdict; and a `### ⚠️ Unresolved` list of every GAP cell + unresolved dimension violation. `renderArtifacts()` renders the four artifacts (Contract block + Interaction matrix + dimension table + Precondition diff + premises) **losslessly** — every draft row appears verbatim, with no output-token ceiling. The consolidator (`agents/consolidate.md`) writes **only** the narrative synthesis (`## Síntese` — clustering the GAPs/refutations into the BLOCKERs the planner must decide), sandwiched between the header and the artifacts. Retyping the artifacts through the LLM is exactly what dropped 16 of 70 contract items on a real run (the recurring consolidation-lossiness class); moving the render to JS removes that lossy step entirely.

**The state axis is canonicalized before render** (`canonicalizeMatrixStates`, applied inside every `applyPatch`): a downstream fill/refute/justify agent paraphrases an enumerated state into a terser convention ("status=STOPLOSS" vs the verbose "StoreStopLoss.status = STOPLOSS (…)"), which `normCode`'s full-label fallback can't collapse — so on a real run the matrix DOUBLED to 18 states / 576 cells (9 / 288 real), the gate-justify pass filled ~288 phantom cells, and the GAP count read 2× the truth. The canonicalizer collapses verbose/terse variants of the same logical state (suffix-anchored loose key), remaps the cells, and merges collisions (GAP wins, so a merge never hides open work).

**Three programmatic checks** still run over the rendered body — but as a **regression guard on the engine's render**, not a cure for LLM-retype lossiness (which is now gone): (1) `consolidationFidelity()` (DROP class — anchor absent everywhere), (2) `contractBlockCoverage()` (BURIED class — a commitment outside the numbered `## Contract` block; it anchors on the heading whose text *begins* with "contract", not the document title `## deep-plan contract — …`), (3) `structuralCounts()` (SHORTFALL class — a collection rendering *fewer* rows than the draft holds). With deterministic render these are always clean; a hit means `renderArtifacts` itself dropped something (an engine bug), so it is **logged + surfaced in the result, never patched by an LLM repair pass.** The narrative is `stripPreamble`-trimmed to its first heading (a model can leak task narration before it). Residuals are reported in the run result (`consolidationOmissions`, `contractBuried`, `contractBlockMissing`, `structuralCounts`, `structuralShortfalls`).

---

## Phase 7: Verdict (advisory-loud) and next step

deep-plan **never blocks** plan finalization — plan mode is used for one-liners too, and a hard gate there needs a fragile escape hatch. Instead it surfaces residual gaps prominently, then asks what to do with `AskUserQuestion`.

The **heavy (Workflow) pass already emits its own verdict header** — `## deep-plan contract — …` followed by a `### Verdict` block (contradiction count if any, substantive-GAP count + clustering, refute-convergence status + per-round trajectory shape + final-round resolution/new-surface mix + resolution re-refute outcome, reframed gate verdict) and a `### ⚠️ Unresolved` list, all rendered by `renderVerdict()`. Present that body as-is; you only add a one-line **Mode·Tier** banner above it (metadata the Workflow can't know):

```markdown
**Mode**: {Full Opus | Economy (tiered)} · **Tier**: Heavy (Workflow)
```

For a **light inline pass** (no Workflow), prepend the full header yourself:

```markdown
## deep-plan: {plan name or intent summary}

**Mode**: {Full Opus | Economy (tiered)} · **Tier**: Light inline
**Affected domains**: {list}
**Residual GAPs**: {count — 0 means the contract is complete}
```

If there are residual GAPs, list them first, each as `⚠️ GAP: <state> × <event/variable> — <why unresolved>`.

Then `AskUserQuestion` — options:

1. **Write artifacts into the plan** — append the filled Contract / Matrix / Dimension table / Precondition diff to the plan file (or the in-session plan draft), replacing any hand-filled versions. When committing a standalone contract, write it to **`.claude/deep-plan/<branch>-<shortSha>.md`** (`git rev-parse --abbrev-ref HEAD` + `git rev-parse --short HEAD`) — branch+commit makes it unique per run and is exactly what the PR-gate reads.
2. **Save gap report to file** — write to a **run-unique** path `.claude/deep-plan/<branch>-<shortSha>/report.md` (or the repo's own scratch dir — never a shared `/tmp/deep-plan-{name}.md`; a flat name in a shared temp dir is the contamination vector that wrecked a real run).
3. **Proceed anyway** — leave the plan as-is; the GAPs are recorded in the conversation. (Used when a GAP is a deliberate, justified exclusion.)
4. **Nothing** — keep it in the conversation output.

The real choke point is downstream: the `gh pr create` PreToolUse hook (the repo's PR-gate, installed under `.claude/`) blocks sensitive-domain branches whose plan-contract is incomplete or whose `/verify-plan` is not clean. deep-plan at plan time is advisory; the hook at PR time is the gate.

---

## Key rules

- **Enumerate from the codebase, never from memory.** The single defining difference from a one-pass planner: matrix columns and guard copies are *grepped*, not recalled. A column set that matches your memory is suspect — grep anyway.
- **The intent is the source of truth for what is proposed; the codebase is the current state.** When the intent contradicts what the code can support, that contradiction is a finding, not something to silently reconcile.
- **The dimension axis is king.** A load-bearing derived value with no tagged base or no cap seam is a Blocker-class gap, exactly as in deep-review. Money is the canonical case; a count, a window-bounded sum, or a ratio with the wrong base is the same bug.
- **Refute independently.** The refuter must not see the analysis reasoning — only the draft contract. Sharing context defeats it.
- **Tier honestly (self-critique paradox).** Don't fan out on trivial or non-sensitive plans; don't light-pass a sensitive-domain lifecycle change.

---

## Concurrency, contamination & search hygiene

These are not optional polish — each maps to an observed failure when running deep-plan across multiple worktrees.

- **Scope every search to the repo root with `rg`, never `grep -r`.** `rg` honors `.gitignore`, so it skips build output, the VCS dir, and **worktrees** — a bare `grep -r` from a worktree traverses build artifacts and every sibling worktree and has hung a run for **87 minutes**. This applies to the light inline pass too. Never read or search `/tmp`, `..`, `~`, or absolute paths outside the current repo. Keep each Bash call one simple command (a fragile `rg … | head; echo; find …` one-liner hangs on an unbalanced quote). Bound any DB/MCP query (`LIMIT`).

- **Contamination guard.** Running multiple deep-plans concurrently, an agent that searches broadly can read **another run's** artifact (a stale `deep-plan-*.md`, a sibling worktree's plan-contract) and silently re-anchor the whole contract to the wrong feature (observed: an "apuração" plan came back 100% about "retention-bonus"). The workflow's `ctx()` injects a guard telling agents the inline intent is the only source of truth — reinforce it in the intent text when two live branches touch the same domain: *"this change is X, NOT Y; ignore any artifact mentioning Y."*

- **Preflight quarantine.** Before a heavy run, move stale flat artifacts out of the search path: `mkdir -p ~/.deepplan-quarantine && mv /tmp/deep-plan-*.md ~/.deepplan-quarantine/ 2>/dev/null || true`. With `rg`-scoping this is belt-and-suspenders, but it's cheap insurance.

- **Run-unique outputs.** Key any saved artifact by branch + short SHA (Phase 7) — `<branch>-<shortSha>` is unique per worktree+commit; append `-<unixTimestamp>` if you keep multiple runs on the same commit. (The org/repo is already encoded by the repo path.) A flat shared-temp name re-contaminates the next run. Note: unique *naming* alone is insufficient — the real fix is that agents no longer read shared temp or sibling worktrees at all (`rg`-scoping above); a uniquely-named stale file is still found by an unscoped `grep`.

- **Matrix axis↔cell integrity: every (state,column) is keyed by its leading CODE (`normCode`), not its full label.** The matrix had a latent bug that only surfaced at scale: refuters/resolvers write cells under drifting label variants — `"S1"` vs `"S1 PERFORMANCE_BONUS_RETAINED"`, `"C1"` vs `"C1 RETENTION mint hook"` — and the gate keyed by exact string. A 4-refuter × 5-round run wrote cells under 68 column-labels (→ 34 codes) and 22 state-labels (→ 15 codes), so the gate saw **374 phantom-empty grid pairs** and a single 59k justify pass re-created 374 duplicates while the real analysis sat in 105 orphaned cells. The fix: `gateCheck` and `applyPatch` key cells by `normCode`, and `applyPatch` reconciles any cell's state/column code back into the axes — so the gate validates the *real* analyzed surface, label variants collapse, and the `refKey` dedup/dry-streak can finally fire. **The gate-violation count tracks refuter invocations under the bug (6→1, 6→84, 20→374); fix the data structure, don't cap the loop.**
- **Refute breadth and depth: 4 lensed refuters/round (`REFUTERS_PER_ROUND`, `ARGS.refutersPerRound` overrides), capped at 3 rounds (`MAX_REFUTE_ROUNDS`), early-exit after 2 dry rounds (`DRY_ROUNDS_TO_STOP`).** History: a 4×5 run *did* reach the full r1+r2 cluster union in one run, but at ~1.8× cost, still no convergence (flat 16/12/15/15/15), and the 374-violation gate above — because more refuters *amplified* the then-unfixed label drift. Once that amplifier was fixed twice over (`normCode` keying + `canonicalizeMatrixStates`) and a coherent matrix was confirmed, breadth was **re-raised deliberately 2→4** — the post-mortem's own invitation. The lever is *breadth, not depth*: the FLAT `11/11/8` trajectory showed coverage-per-round (the number of distinct attack angles) was the binding constraint, and refuters run in **parallel** so breadth is ~free on wall-clock (it trades tokens for coverage); raising *rounds* (depth) just burns serial wall-clock without converging. To keep added refuters from colliding, each takes a **distinct lens** (quantity/dimension · async-ordering · premise · completeness). The ceiling is the resolver patch size (all fresh funnel into ONE patch → StructuredOutput bloat), not thinking — so it is a band, not "more is better". Each round still feeds refuters the **already-open roots** (GAP cells + unresolved violations) so they push to new territory. For *coverage* still prefer an independent re-run over raising rounds — see **One run does not exhaust** below.

- **Terseness prevents crashes.** Oversized drafts make the resolver/consolidator's `StructuredOutput` call return nothing and waste the run (~600 KB observed). The schemas cap verbose fields and the prompts demand `file:line + one clause`. Don't relax that.

- **The iterative resolver returns a PATCH, not the whole draft.** Re-emitting the full — and growing — draft every refute/gate round is what hit the 64k output-token ceiling: on a real run the round-5 refuter found its *strongest* refutation (a creation-time amortization gap) and the resolver then returned nothing, silently dropping it. The refute-resolve and gate-justify steps now emit only the cells/rows/premises/columns they add or change (`DRAFT_PATCH`), which `applyPatch()` merges by key. The merge is additive/override-only, so a thin or malformed patch degrades to "gate still fails", never to silent corruption. The initial **fill** is now a PATCH too (Lever 1): the matrix specialist (Agent 4) already answers every cell into the draft, so the fill no longer re-emits the matrix — it returns only the precondition rows, the Contract block, and any cells the deterministic `missingCells` check still reports missing. Re-emitting all ~N cells was the run's long pole (~35 min on a 28×14 matrix) and itself risked the StructuredOutput crash; cartesian completeness is enforced by the gate's justify loop, not by costly re-emission. `applyPatch` + `gateCheck` + `missingCells` are exercised deterministically via the script's `seedPatch` / `seedDraft` / `seedMissing` test modes.

---

## One run does not exhaust — the multi-run protocol

A single heavy pass samples a *fraction* of the finding space, and the refute loop reaching `MAX_REFUTE_ROUNDS` is **not** a signal it found everything. The evidence across four dogfood runs of the *same* plan: r1 and r2 agreed on only ~4 core clusters while each caught 4–6 the other missed; r3 at 4×5 reached the r1+r2 union but never went dry (flat 16/12/15/15/15); r4 at half r3's budget (18 agents, ~3.1M tokens) **still surfaced 6 new confirmed premises including a blocker**. The variance lives in the refuter's *attack-angle sampling*, not in loop depth — so cranking `MAX_REFUTE_ROUNDS` buys little; an *independent* re-run buys a lot.

For a high-stakes sensitive-domain plan, **run deep-plan 2+ times and union the findings**, rather than trusting one deep pass:

1. Each run already writes a run-unique deliverable `.claude/deep-plan/<branch>-<shortSha>.md` (add `-<timestamp>` for same-commit re-runs). Keep them side by side.
2. **Union, don't intersect:** a finding present in *any* run is in scope. Reconcile the Contract blocks, the matrix `GAP` cells, and the premises across runs; a commitment in run B but not run A is a real gap run A missed, not noise.
3. **Strike revoked items — never append a revision beside the commitment it supersedes.** When a later run *overturns* an earlier decision, the union must delete or visibly strike the superseded item, not carry both. A real run opened with **7 contradictions — all 7** were a stale unioned commitment sitting beside the revision that replaced it: union debt, not new findings.
4. **Scan the unioned document for contradictions at reconciliation time** — two commitments giving conflicting directives for the same mechanism/seam, the same check the consolidator runs in-run. The gate never catches these (completeness, not consistency), so the hand-reconciliation is the only place this happens between runs.
5. **The first run fed with the unioned contract is the designated re-refute of the union** — not an optional extra. Contradictions it flags are the *expected output* of union debt; they render first in the verdict — resolve them before reading any other finding.
6. Treat a run that surfaces *zero* new load-bearing findings as the real convergence signal — two such runs in a row is "dry" at the run level, the analogue of the in-run dry-streak. A cheaper leading indicator: when a run spends more of its top findings *revoking* earlier decisions than discovering new surface, the design is near its fixpoint even while the in-run refute trajectory stays FLAT.

This is breadth at the *run* level: cheaper and higher-yield than raising **round depth** (`MAX_REFUTE_ROUNDS`), which the FLAT trajectory shows never converges. Per-round **breadth** (`REFUTERS_PER_ROUND`, now 4 lensed) is the one in-run knob worth raising — it adds near-independent attack surfaces per round at ~no wall-clock cost — and `ARGS.refutersPerRound` dials it per-run for an unusually large change. But for *coverage* of the finding space, an independent re-run still beats raising any in-run knob; reach for it first, and read the new run's `### Verdict` trajectory shape to decide whether another is warranted.
