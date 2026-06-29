---
name: verify-plan
description: "Reconcile an implementation against the plan that specified it — flags dropped commitments (coverage), changed predicates/values (fidelity), and unplanned logic (scope-creep). Cheap, plan-grounded; run before opening a PR or per-commit during long implementations. Not a bug hunt — use /deep-review for emergent-interaction coverage."
argument-hint: "[plan file path] [diff range or commit]"
---

Reconcile an implementation against the plan that specified it. This is a **bounded, plan-grounded** three-way check — coverage, fidelity, scope-creep — using one model and no open-ended codebase sweep. It exists to catch the cheap, common failure: a *correct* plan whose implementation silently drifted (a precise filter shipped looser, the 4th of 4 wiring points never wired, a guard copied with a now-false precondition). It is **not** a bug hunt — for emergent interactions and novel bugs in code the plan never mentioned, use `/deep-review`.

**Cost model:** one model, plan-grounded, no codebase sweep. Run this *liberally* — every commit during a long implementation, and once before opening a PR — where `/deep-review` is run rarely. The whole point is to be cheaper than re-running `/deep-review`.

**What produces a reconcilable contract:** for sensitive-domain lifecycle/flow changes, `/deep-plan` is what fills the four plan-contract artifacts (Contract block, interaction matrix, dimension table, precondition diff) and adversarially refutes them before the plan is presented. The more complete that contract, the more this check can mechanically verify — a plan whose matrix has empty cells or whose derived quantities are untagged gives verify-plan less to reconcile against.

---

## Phase 0 — Resolve inputs

Parse arguments to determine the **plan** and the **diff**. (Input-resolution model adapted from `deep-review` Phase 0 — simplified; no PR-checkout machinery needed for the local/per-commit default.)

**Plan:** the goal is to reconcile against *this repository's current plan* — never to silently grab whatever plan happens to be newest. `/refine` writes plans to the repo-local `.claude/.plans/` (gitignored; no cross-repo collision), with the global `~/.claude/plans/` as a fallback for hand-authored plans. That global directory is shared across every repo on the machine, so "most recently modified" there can easily resolve to an unrelated project's plan and produce a flood of bogus Missing/Diverged/Unplanned findings — which is why the repo-local directory is preferred and every candidate is confirmed against the working tree before use.

Resolve in this order:

1. **Explicit path argument** → use it as-is.
2. **The current session's plan**, if this session implemented one — use that exact file. A plan you authored or executed this session is the authoritative target; prefer it over any directory scan.
3. **Otherwise, scan and *confirm before use*.** List candidates newest-first, repo-local directory first (`ls -t .claude/.plans/*.md ~/.claude/plans/*.md 2>/dev/null | head -5`), then confirm the top candidate actually belongs to this repo *before* reconciling: grep a handful of concrete path/module/class tokens from the plan body against the working tree (e.g. file paths it claims to create, package names, service classes). A plan that references files present here is this repo's; one whose tokens match nothing here is not.
   - If exactly one candidate is confirmed to belong to this repo, **state which plan was selected and why** and proceed.
   - If none can be confirmed, or **two or more** are plausible, or the newest plan's tokens don't resolve in this tree — **stop and ask the user** with `AskUserQuestion`, listing the candidate plans. Do **not** reconcile against an unconfirmed plan; a wrong plan makes every downstream finding noise.

Always echo the resolved plan path in the output so the user can catch a mis-selection immediately.

**Diff:**

- If a range/commit argument is given, use it (`<SHA>~1 <SHA>` for a single commit, `HEAD` for the latest commit, or an explicit range).
- Otherwise, default to the current branch vs the base branch (`origin/main` unless the repo uses another — check the conventions), including uncommitted changes (like deep-review local mode):
  ```bash
  git fetch origin <base>
  git diff origin/<base>          # committed + uncommitted
  git diff --stat origin/<base>
  git log --oneline origin/<base>..HEAD
  ```
- For **per-commit mode** (a single commit / `HEAD` argument): `git diff <SHA>~1 <SHA>`.
- **Include untracked files — without mutating the index.** `git diff` does not show untracked (`??`) files, so a plan that contracts *new* files would reconcile as all-Missing against a bare diff. Run `git status --short` to list `??` entries, then **read each untracked file directly from disk** with the Read tool during reconciliation. Do **not** stage them (`git add -N`) to force them into the diff: intent-to-add entries persist in the developer's index, pollute later `git status`, and can interfere with branch switching — a read-only check must leave the working tree exactly as it found it. A NEW-file commitment is verified against the file on disk, never only the diff.

**Extract the contract:**

- Extract the plan's **Contract block** (the `## Contract` numbered list — see the repo's plan-contract spec, `docs/agents/skills-config.md` › Docs layout › Planning; default `docs/planning/plan-contract.md`).
- **Also reconcile Artifacts 2–4 when the plan carries them**: the interaction matrix (each filled cell is a commitment — a cell that says `handled (step 4)` must actually be handled in the diff), the **dimension table** (each load-bearing quantity's tagged base/unit and cap, and the executable `require`/`check` seam that enforces it, must appear in the code — a cap with no seam in the diff is **Missing**; a quantity computed on the wrong base — e.g. a discount applied to `face` when the table says `residual` — is **Diverged**), and the precondition diff (one resolved row per touched-guard copy). Treat each as contract items of the same three-way check.
- If the plan has **no contract block**, fall back to extracting commitments from the prose yourself, and **note in the output** that adding a contract block (per the plan-contract spec) would make this check far more reliable.

---

## Phase 1 — Three-way reconciliation

Bounded. Do **NOT** open-ended bug-hunt — that's `/deep-review`. Work item by item against the contract.

1. **Coverage** — for each contract item, grep/read the diff to confirm it is implemented. Not present in the diff → **Missing**. (This catches the unwired 4th hook.)

2. **Fidelity** — where a contract item names an exact predicate / filter / value / set, confirm the code matches its **intent**, not just that *something* is there. Different → **Diverged**, reporting planned-vs-actual side by side (e.g. planned `status == PENDING` vs shipped `!isCreditDeduction`). (This catches the silently-weakened filter.)

3. **Scope-creep** — identify diff logic that **no** contract item or prose covers: new branches, gates, parameters, services. Flag as **Unplanned** for scrutiny. This is where smuggled-in wrong assumptions live — e.g. a guard copied from legacy code with a now-false precondition.

---

## Phase 2 — Output

A compact report with three buckets. Each entry cites the contract item and/or `file:line`, shows planned-vs-actual, and gives a one-line "why it matters / what to verify".

```markdown
## verify-plan: {plan name} ↔ {diff target}

**Plan:** {selected plan path} {(no contract block — prose fallback used) if applicable}
**Diff:** {range/commit/local}

### Missing (contracted, not implemented)
- [contract #N] <commitment> — not found in diff. <why it matters>

### Diverged (implemented differently than contracted)
- [contract #N] <commitment> — planned `X` vs actual `Y` (`file:line`). <why it matters>

### Unplanned (in diff, in no contract item)
- `file:line` — <logic> covered by no contract item. <what to verify>
```

End with the boundary line, verbatim:

> verify-plan checks plan↔diff fidelity only. It does not find bugs in code the plan never mentioned — run /deep-review for emergent-interaction and novel-bug coverage.

If all three buckets are empty, say so plainly and still print the boundary line.

---

## Execution model

- **Default — inline.** The invoking session performs the reconciliation directly. Cheapest; no sub-agent spawn.
- **`fresh` / per-commit mode.** Spawn **one** `general-purpose` Sonnet sub-agent with `{plan + single commit diff}` as its only context, for a context-isolated fresh-eyes pass. This directly counters the degraded-working-memory drift that accumulates across a long, many-commit implementation — the reviewer hasn't lived through the drift.
- **Never fan out to multiple agents.** Multiple agents erases the cost advantage over `/deep-review`; if you need that breadth, you want `/deep-review`, not this.
