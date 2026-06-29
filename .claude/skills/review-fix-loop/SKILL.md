---
name: review-fix-loop
description: "Review→fix loop in clean context over an open PR: review (mirror of Anthropic's code-review action, or deep-review's lenses + facet enumeration when the change is complex / sensitive-domain / deep-planned), conciliate with comments and reviews already posted on the PR, post the consolidated review to GitHub, and a fixer agent addresses the points (commit+push, updates the description, replies when it diverges). Round 1 breadth, later rounds depth (validator + fix-diff + re-enumeration); repeats until exhaustion — 0 High/Blocker in a breadth round with a dry enumeration (cap 5 rounds) — plus a final Medium/Low round."
argument-hint: "[PR number | URL | empty = current branch's PR] [deep|standard] [max-rounds=N]"
disable-model-invocation: true
---

`review-fix-loop` is step 3 of the `refine → implement → review-fix-loop` pipeline, but works standalone on any open PR. The whole cycle runs inside a **Workflow** (`.claude/workflows/review-fix-loop.js`) — this invocation is the user's explicit opt-in, including authorizing the agents to **commit/push on the PR's branch** and **post comments on the PR** via `gh`. Each agent in the loop starts with clean context: the reviewer never sees the implementer's reasoning, the fixer never sees the reviewer's reasoning beyond the published review.

This skill reads per-repo configuration from **`docs/agents/skills-config.md`** (schema: `skills/setup/skills-config.template.md`). It hardcodes nothing about stack, build commands, sensitive domains, doc paths, or commit conventions; when a section is absent it falls back to a stated default and says so in its output.

---

## Step 1 — Resolve arguments

- Token `deep` or `standard` → forces the review mode (otherwise the workflow classifies it itself in round 1).
- `max-rounds=N` → override the cap (default **5**).
- `max-extensions=N` → override the dynamic cap extensions (default **2**; see Contract).
- PR: number, `#N`, or URL → extract the number. Empty → `gh pr view --json number,headRefName,state` on the current branch. If there is no open PR, stop and say so.

## Step 2 — Preflight

1. `gh pr view <N> --json number,title,state,headRefName,baseRefName,isDraft` — the PR must be `OPEN`.
2. Check out the PR's branch (`gh pr checkout <N>`) with a clean working tree — the fixers work on it. If there are uncommitted local changes, ask first.
3. If the repo's **Verify** command runs containerized tests (e.g. TestContainers, a local DB), confirm the runtime is up before the fixers start (e.g. `docker info`; a stopped daemon → suggest starting it). Read the verify command from `docs/agents/skills-config.md` › Verify; if absent, ask the user for the format/lint/build/test command.
4. Hints for the classifier:
   - Is there a deep-plan contract committed on this branch? Look under the planning path in **Docs layout** (`docs/agents/skills-config.md`), e.g. `.claude/deep-plan/*.md` (`git diff --name-only origin/main...HEAD | rg '<plan-contract glob>'`).
   - Which of the repo's **Sensitive domains** (`docs/agents/skills-config.md` › Sensitive domains) does the diff touch? Detect each changed file's domain via **Domains** (config — path-glob → domain). If no sensitive domains are listed, treat no domain as sensitive.
   - The repo's **flows** (`docs/agents/skills-config.md` › Flows — one doc per flow under the flows dir, default `docs/flows/`). Select each flow doc whose frontmatter `covers:` globs intersect the changed files — a flow doc with no `covers` always counts as touched (matches deep-review) — and pass them as `flows` so deep mode fans out one flow-lens agent per touched flow. If none match (or there are no flow docs), deep mode runs the universal lenses only.
   - Diff size (`gh pr diff <N> --name-only | wc -l`).

## Step 3 — Fire the workflow

Call `Workflow` with `scriptPath: .claude/workflows/review-fix-loop.js` and `args` (a real JSON object):

```json
{
  "prNumber": 123,
  "repoRoot": "<git rev-parse --show-toplevel>",
  "branch": "<headRefName>",
  "maxRounds": 5,
  "maxExtensions": 2,
  "maxEnumSurfaces": 6,
  "modeHint": "auto | deep | standard",
  "deepPlanContractOnBranch": true,
  "sensitiveDomains": ["payout", "billing"],
  "flows": [{ "name": "payment-pipeline", "doc": "<full text of docs/flows/payment-pipeline.md>" }],
  "diffFiles": 42,
  "timestamp": "<date +%Y-%m-%dT%H-%M>"
}
```

`sensitiveDomains` is the subset of touched domains that the config marks sensitive (empty if none). `flows` is the set of flow docs the change touches, copied from the flows dir (`[{ name, doc }]` where `doc` is the flow doc's full text, empty if none) — in deep mode the workflow runs the universal lenses always, plus one `flow-lens` agent per flow. Runs in the background; wait for the `<task-notification>`.

## Step 4 — Report

Read the result and lead with the outcome:

- `status: "clean"` — the round that zeroed High/Blocker, what the final Medium/Low round addressed, pushed commits, the link to each round's review comment.
- `status: "clean-partial"` — zeroed High/Blocker **but the confirming round ran with crashed lenses** (the `lensFailures` field lists round + lenses). The `clean` covers only the lenses that ran → it is **FALSE-clean** until confirmed (under throttling, an agent can exceed the StructuredOutput retry cap and fall to `null` in a parallel fan-out; a clean over a partial lens set is not exhaustion). **Before reporting it as done**, run a **confirmation pass over the lenses in `lensFailures`** — spawn 1 independent Agent per crashed lens (anchoring-free, code-grounded with file:line, reading the role file `.claude/skills/deep-review/agents/<lens>.md`); if any finds High/Blocker, treat it as a new round (fix + push). Only then is it truly `clean`.
- `status: "capped"` — the cap (with any extensions — the `extensionsUsed` field) was reached; the workflow validated the last round's fixes in a bounded way and posted a comment on the PR separating the buckets. Report all four: `openHighBlocker` (genuinely open), `fixedValidated` (fixed, bounded validation OK), `fixedUnreviewed` (fixed, fixer's claim without validation), `contestedUnreviewed` (diverged without re-assessment). Recommend the next human step per bucket.
- `status: "blocked"` — a step stalled (e.g. verify never went green); say where and what was already pushed.

**CI check (whenever there was a push — `clean`, `clean-partial`, or `capped`):** local verify catches neither flake nor environment divergence. Run `gh pr checks <N> --watch` (or poll with a ~20 min timeout). If CI is red because of the loop's pushes, run **one single** correction cycle in this session (surgical fix + targeted verify including the always-run gates from config › Verify + the module's tests + push) and re-check. If it stays red, report honestly with the failures — do not iterate indefinitely.

Always include: finding counts by severity per round **with the round type and the enumeration** (the trajectory — `breadth 7 → depth 3 (non-dry enum) → gate 0 (dry)` tells the story; a jump of Blockers in a late round signals a fix-regression), documented divergences (points where the fixer disagreed with the reviewer and replied on the PR), whether `gateNeverRan` (exhaustion not confirmed — recommend the manual gate via `/deep-review`), **non-empty `lensFailures`** (lenses that crashed in some breadth/gate round — require the confirmation pass above before trusting a `clean`/`clean-partial`), and whether the mode was standard or deep and why.

---

## Workflow contract (what `review-fix-loop.js` guarantees)

The round economics come from a real audit (a 10-round, 36-Blocker/High run): 93% of post-round-1 Blocker/High **were already visible** to earlier rounds (satisficing — each specialist finds 1 defect per surface and moves on), and only 2/28 were fix regressions (both caught the following round). So the loop is **breadth once, depth after**: re-sweeping everything every round pays for coverage that already exists and does not buy depth.

- **Classify** (round 1): deep if a deep-plan contract is on the branch, OR a **Sensitive domain** (config › Sensitive domains) is touched with a lifecycle/flow change, OR a large/delicate diff (agent's judgment); otherwise standard. If no sensitive domains are configured, default to standard unless a deep-plan contract is on the branch. `modeHint` forces it.
- **Review — round 1 and gate (breadth)**:
  - *standard* — 1 agent mirroring Anthropic's code-review action prompt (`pr-review-comprehensive`), spec in `.claude/skills/review-fix-loop/agents/standard-review.md`, all rounds (simple PRs don't pay the deep apparatus).
  - *deep* — fan-out of the repo's deep-review lenses (`.claude/skills/deep-review/agents/*.md`): the **universal lenses** (adjacent-code, derived-quantity, negative-space, contract×code, test-coverage) plus **one flow lens per entry in config › Flows** via deep-review's generic `flow-lens.md` mechanism + the consolidator (`consolidate.md` — demotion of Blocker/High only with evidence read in the code; fills `surface` per Blocker/High).
  - Both receive the **branch's plan-contract as the current spec** (fixes from earlier rounds update the semantics they minted into it) — code↔contract divergence and a derived quantity without a dimension row are findings, which keeps the reviewer from re-deriving semantics already decided.
- **Enumerate (every deep round with Blocker/High)**: for each hot surface (grouped by `surface`, cap `maxEnumSurfaces` default 6, Blockers first), an enumerator (`enumerate.md`) **exhausts the surface's facets in the same round** — cross-domain writers, scope complements, clock, formula legs, crash windows — with file:line evidence per facet and a `dry` verdict. It is the antidote to the one-facet-per-round pattern (one ~30-line query produced 8 Blocker/High across 8 rounds).
- **Review — rounds 2+ (depth)**: instead of a fresh full fan-out: (1) **bounded validator** adversarially judges each fix claimed by the previous round's fixer (scope closed to the ids; `stillOpen` re-enters); (2) **fix-diff reviewer** reviews only the delta of the fixer's commits (catches fix-introduced defects with zero latency); (3) **re-enumeration** of the surfaces of the just-fixed Blocker/High (the fix narrowed the surface — that's when the next layer becomes visible).
- **Conciliate**: an agent fuses the fresh review with **everything already posted on the PR** (human reviews/comments, bots, earlier rounds of this loop), dedups, re-assesses findings the fixer contested with an argument (does not re-raise without a new counter-argument), re-prices stale Mediums (age in the prompt), and **posts the consolidated review as a PR comment** (`gh pr comment` — the GitHub API rejects `gh pr review` on one's own PR) with an HTML round/SHA marker for idempotency. Space never cuts Blocker/High/Medium — only Lows, recorded in `droppedForSpace`.
- **Fix**: a fixer agent addresses Blockers+Highs, **aged Mediums (≥2 rounds in the queue — assigned, not optional)**, and opportunistic Mediums in the same files: implements, runs the repo's **Verify** command (config › Verify — incremental for per-round checks, always appending the always-run gates listed there), commits, pushes, **updates the PR description** (the PR-body sync rule from config › Conventions), and **replies on the PR** when it decides to diverge from a finding, with the why. Divergences are recorded for the next round's conciliator. A fix that changes a **predicate, temporal window, formula, or money semantics** updates the branch's plan-contract + the premise in the configured premises path (config › Docs layout) **in the same commit** (strike, don't append) — that's what breaks the generative loop where each round attacks the previous one's resolution. **A ratified divergence** (by the user or an accepted contestation) also becomes a contract amendment — ratification that lives only in a PR comment is re-mined by every fresh reviewer.
- **Termination by exhaustion**: clean requires 0 High/Blocker **in a breadth round** (round 1 or gate) **with a dry enumeration**. A depth round that zeroes H/B only schedules the **fresh-eyes gate** (full fan-out) the following round — quiet is not exhausted; the first-seen curve of the audited run debuted 7 Blocker/High in round 4. The gate earns +1 round beyond the cap (once) if needed. Once exhaustion is confirmed → **one last fix round** for the pending Medium/Low (with the full **Verify** command before the final push) and **no new review after it**. The fixer of the **last possible round** (round == effective cap) also runs the full Verify — any terminal push of the loop is fully verified, regardless of how it terminated.
- **Partial exhaustion (`clean-partial`)**: if the confirming round (breadth or gate) ran with **crashed lenses** — under throttling an agent can exceed the StructuredOutput retry cap and fall to `null` in the parallel fan-out — the status becomes `clean-partial` and `lensFailures` lists round + lenses. A `clean` over a partial lens set is FALSE-clean. The workflow does not try to re-run the lens itself (the crash tends to be deterministic in the same run); the orchestrator runs the **confirmation pass** (Step 4) out of band before declaring `clean`.
- **Dynamic cap**: if the cap-round review finds a **Blocker with a never-before-seen id** (not seen in any earlier round — the signature of a regression introduced by a fix, exactly when stopping is worst), the cap extends +1 round, up to `maxExtensions` (default 2). Re-disputing an already-seen id does **not** extend — a FLAT dispute trajectory is a case for a human, not more rounds.
- **Post-cap fidelity**: the last review (pre-fix) and the last fix are different instants — when capping, the workflow subtracts from `openHighBlocker` what the cap-round's fixer addressed and runs a **bounded validator** (1 agent, scope closed to the fixed ids; not a new review) that separates `fixedValidated` from re-opened. The cap comment and the result reflect the four buckets, never the pre-fix snapshot. If the loop capped with a pending gate (`gateNeverRan: true`), the comment says explicitly that exhaustion was NOT confirmed.
