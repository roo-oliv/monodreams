---
name: refine
description: "Turns a raw request (free text, a plan file, or a link — GitHub issue/PR, Jira, Slack) into an APPROVED plan with a Contract block, deciding on its own whether to interview the user, whether to illustrate critical outcomes for confirmation, and whether the risk warrants /deep-plan before implementation. Replaces interactive plan mode; used standalone or as step 1 of /implement."
argument-hint: "[free text | plan file path | issue/PR/Jira/Slack URL]"
---

`refine` is the planning step of the `refine → implement → review-fix-loop` pipeline, but it works standalone. It takes an intent in any format, resolves the content, decides whether it needs to interview the user, writes a plan with the plan-contract artifacts, **decides on its own** whether the risk justifies running `/deep-plan`, and only then returns to the user for final approval. The output is a plan file stamped APPROVED — exactly the input `/implement` consumes.

This skill runs **inline in the main conversation** (it needs `AskUserQuestion`); it does not dispatch a Workflow directly — at most it invokes `/deep-plan`, which has its own Workflow.

This skill reads per-repo configuration from `docs/agents/skills-config.md` (written by the `setup` skill). It hardcodes nothing about stack, paths, sensitive domains, or conventions; where a section is missing it falls back to a stated default and says so.

---

## Phase 0 — Resolve the input

Classify the argument and materialize the **raw intent** (brief):

1. **Existing file path** → read it. If it is already a plan with a `## Contract`, treat it as a draft to refine (not as a raw brief).
2. **URL** — resolve by host:
   - `github.com/.../issues/N` → `gh issue view N --repo <owner/repo> --json title,body,comments`
   - `github.com/.../pull/N` → `gh pr view N --repo <owner/repo> --json title,body,comments`
   - Jira (`*.atlassian.net/browse/KEY-N`) → ToolSearch `+atlassian` and use the Atlassian MCP tools (authenticate if needed via `authenticate`).
   - Slack (`.../archives/<channel>/p<ts>`) → ToolSearch `+slack read thread` and use `slack_read_thread`.
   - Google Drive/Docs → ToolSearch `+drive read` and read the content.
   - Any other URL → ToolSearch `select:WebFetch` and fetch it.
3. **Free text** → it is the brief itself.

Echo the brief: summarize in 2–4 lines what you understood must be implemented and **where it came from** (link/file). If the brief is empty or the URL fails to resolve, stop and ask.

## Phase 1 — Codebase context

Same as deep-plan's Phase 1, but lighter:

1. List the **affected domains** from the brief. Detect each changed file's domain via **Domains** (`docs/agents/skills-config.md` — path-glob → domain). For each, read the premises file at the path from **Docs layout** (`docs/agents/skills-config.md`), substituting `{domain}`/`{module}` per the configured premises pattern, plus the schema doc and core-tenets doc if Docs layout lists them. If config is absent, default to `docs/CORE_TENETS.md` and `docs/{domain}/premises.md` and say so.
2. Dispatch 1–3 **Explore** agents in parallel to map the files/services/flows the change touches (breadth "medium"; "very thorough" if the brief is vague about where to work). You want: wiring points, existing guards, callers, tests that cover the flow today.
3. `rg`, never `grep -r` (repo scope, ignores worktrees/build).

## Phase 2 — Interview or not (judgment)

Interview **only if** there is material ambiguity: multiple reasonable interpretations of the scope, an undeclared invariant the implementation will depend on, an edge case with divergent plausible answers, or a tradeoff that changes the architecture (e.g. synchronous vs outbox). Use `AskUserQuestion` (up to 4 questions per round, max ~2 rounds — follow the premises interview script in the repo's rules dir if it has one (config › Docs layout › rules dir), e.g. its premises rule: "what existing invariants does this change assume?", "what breaks elsewhere if the premise is false?").

For every scenario the plan decides to **defer** ("out of scope", "we handle it later"), add the question: **"how often does the deferred case fire in production?"** A routine trigger will be re-litigated in review as if it were a gap (the deferred case is re-opened in review precisely because its trigger was routine) — either the plan handles it, or the deferral rationale goes into the prose with the frequency datum.

If the brief is unambiguous, **say so in one line and proceed** — don't fabricate questions. The deep-plan self-critique paradox applies here: ceremony on a simple task teaches the user to ignore the skill.

## Phase 3 — Author the plan

Write the plan following the plan-contract spec from **Docs layout** (`docs/agents/skills-config.md` › Planning › plan-contract spec). If config lists no plan-contract spec, default to the canonical four artifacts described below and say so.

- **Prose**: context, chosen approach (and discarded alternatives, with 1 line of why each), implementation phases if multi-phase.
- **`## Contract`** (always): a flat numbered list of atomic, individually verifiable commitments.
- **Interaction matrix** and **Precondition diff**: when the plan-contract triggers apply (new entity status/lifecycle, state machine, long-lived RESERVED/PENDING record; deleted/replaced method/flow or copied guard).
- **Metadata header** at the top of the file:

```markdown
# {title}

**Source:** {text | file | resolved URL}
**Suggested branch:** {type}/{kebab-slug}   ← follow the commit/PR conventions from config › Conventions
**Domains:** {list}
**Risk tier:** {direct | deep-plan} — {1-line justification}
**Status:** DRAFT
```

The Contract block, interaction matrix, and precondition diff are the domain-agnostic planning artifacts (also referenced by `/deep-plan` and `/verify-plan`): the Contract is the checklist later reconciled against the implementation; the matrix is new-status × adjacent-entity-event with each cell `handled`/`N/A`/`GAP`; the precondition diff covers every copy of a touched guard.

Save to `<repoRoot>/.claude/.plans/<kebab-slug>.md` (repo-local; auto-ignored by `.gitignore` via `.claude/*` — not versioned). This is the repo-local directory that `/verify-plan` (Phase 0) and the deep-plan PR gate scan, with a fallback to the global `~/.claude/plans/`. If the session's plan mode blocks the Write, present the plan in the conversation and write the file right after the Phase 6 approval.

## Phase 4 — Illustrate critical outcomes (judgment)

If the plan produces outcomes whose concrete shape the user needs to validate — a new or changed API/event payload, a value flow (who receives how much in which account), before/after of a data mutation, a new schema — **show the concrete example** (realistic JSON, before/after table, arithmetic in the smallest unit) and confirm with `AskUserQuestion`. Skip when the outcome is obvious from the contract itself (refactor with no behavior change, point fix).

## Phase 5 — Risk tier: deep-plan or direct (autonomous decision)

Decide **on your own** — don't ask — and state the decision with a 1–2 line justification. Run `/deep-plan` (via the Skill tool, passing the plan path as the intent file) when **any** of these hold:

1. **The deep-plan Phase 0 rule**: the change touches the repo's **Sensitive domains** (`docs/agents/skills-config.md` › Sensitive domains) **and** adds/modifies an entity status/lifecycle, introduces a state machine, creates a long-lived RESERVED/PENDING record, or replaces an existing flow/method. If no sensitive domains are listed, treat no domain as sensitive — this clause never fires; fall through to clause 2.
2. **High/critical bug likelihood** via other vectors: new async ordering (listener/outbox/webhook), concurrency/locking, a migration with a data backfill, a refactor that crosses ≥3 modules/domains, or a change to a derived-value computation (rounding, proration, caps).

Otherwise, go direct. On a genuine doubt between tiers in a sensitive domain, prefer deep-plan (false-heavy is cheap; false-light ships the gap).

When deep-plan runs: use its "Write artifacts into the plan" option to merge the refuted contract into the plan file, and **note in the header** `**Risk tier:** deep-plan (ran — contract merged)`. Residual GAPs become explicit approval items in Phase 6, never silently accepted. The standalone contract that deep-plan writes to `.claude/deep-plan/<branch>-<shortSha>.md` is committed by `/implement`'s wave 1 (it is what the PR gate reads).

## Phase 6 — Final approval (always)

Regardless of tier, **return to the user** with `AskUserQuestion`:

- Plan summary in ≤10 lines: what changes, contract size, risk tier, residual deep-plan GAPs (if any), suggested branch.
- Options: **Approve** / **Adjust** (collect the adjustment, return to Phase 3) / **Cancel**.

On approval, update the header to `**Status:** APPROVED — refine {date}` and finish by reporting:

```
Plan approved: .claude/.plans/<slug>.md
Suggested branch: <type>/<slug>
Next step: /implement .claude/.plans/<slug>.md
```

Write the branch and commit/PR language using the conventions from `docs/agents/skills-config.md` › Conventions (Conventional Commits, branch naming). When invoked **by `/implement`**, this approval already counts as the final confirmation — implement does not ask again.

---

## Rules

- **Enumerate from the codebase, not from memory** — wiring points and guards in the Contract come from grep/Explore, not recall.
- **Don't silently pick between alternatives** — competing interpretations go to the interview (Phase 2) or appear as a discarded alternative in the prose.
- **Minimal plan that solves it** — nothing speculative in the contract; every item traces to the brief.
- **Honest tier** — don't run deep-plan on a typo fix; don't light-pass a sensitive-domain lifecycle change.
