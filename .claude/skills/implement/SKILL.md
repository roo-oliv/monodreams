---
name: implement
description: "Full implementation pipeline: resolve the input (free text, plan file, or link), secure an APPROVED plan via /refine, then drive a wave-based implementation workflow (fresh agent per wave + persistent ledger) that commits, verifies, and opens the PR — with autonomous decisions documented. At the end it auto-chains /review-fix-loop (disable with 'no-review')."
argument-hint: "[free text | plan path | URL] [no-review]"
disable-model-invocation: true
---

`implement` drives an approved plan all the way to an open Pull Request, then chains `/review-fix-loop`. The implementation runs inside a **Workflow** (`.claude/workflows/implement.js`) — this skill invocation is the user's explicit opt-in to the multi-agent orchestration, including express authorization for the workflow's agents to **commit and push the working branch** (that branch only; never the base branch, never force).

Context architecture: **waves + ledger**. The script splits the plan into waves; each wave is a *fresh* agent that reads the plan + the progress ledger, implements only its slice, verifies, commits, pushes, and updates the ledger. No agent accumulates context beyond its own wave — this is the substitute for `/compact` (not invocable inside workflow agents) — and a crash is resumable from the ledger + branch.

This skill reads the repo's `docs/agents/skills-config.md` for its stack-specific inputs (Verify command, Sensitive domains, Docs layout, Conventions). When a section is absent, the workflow's agents fall back to the stated default and say so.

---

## Step 1 — Parse arguments

- Detect and strip the `no-review` token (aliases: `noreview`, `sem-review`) → set `NO_REVIEW`.
- The remainder is the input: free text, a file path, or a URL.

## Step 2 — Secure an APPROVED plan

1. If the input is a path to a plan with `**Status:** APPROVED` (or a plan approved **in this session** via refine/plan mode) → use it.
2. Otherwise (text, URL, DRAFT plan, or nothing) → **invoke the `refine` skill** (Skill tool) with the input. Refine interviews if needed, decides deep-plan vs. direct, and ends with the user's final approval — that approval is the confirmation to implement; don't ask again.
3. If refine ends in Cancel, stop.

Extract from the plan: path, suggested branch, domains, risk tier, and the path to the standalone deep-plan contract (typically `.claude/deep-plan/<branch>-<shortSha>.md`), if one exists.

## Step 3 — Preflight

Run, and **stop with a clear diagnostic** if anything fails (do not improvise):

1. **Clean working tree**: `git status --porcelain` empty. If there are uncommitted changes, ask the user (stash / commit / abort) — never discard.
2. **Branch**: if on the base branch, create the branch the plan suggests (`git checkout -b <branch>`). If already on a feature branch different from the suggested one, confirm with the user which to use. Record the final name — every workflow agent validates against it.
3. **Up-to-date base**: `git fetch origin <baseBranch>` (default `main`; use the repo's default branch).
4. **Test prerequisites**: if the repo's **Verify** command (`docs/agents/skills-config.md` › Verify) needs a runtime to be up (a database, container daemon, emulator), confirm it is running first; if it's down, suggest starting it and wait. If config is absent, skip this check.
5. **Ledger**: derive `slug` = branch with `/`→`-`; ledger at `<repoRoot>/.claude/.implement/<slug>/ledger.md` (absolute path, expanding `repoRoot` = `git rev-parse --show-toplevel`; create the directory). `.claude/.implement/` must be gitignored — the ledger lives in the repo but is **never** versioned. If the user gave **execution directives** (constraints, "if X happens, do Y", landmine warnings — e.g. "ambiguity in a sensitive domain → blocked, not a decision"), seed the ledger now with a `## Directives (from the user)` section containing them: it is the context-injection channel into the workflow, and the agents treat it with **precedence over autonomous decisions**. Work already done outside the workflow (e.g. a manual "Wave 0") also goes here, marked complete with its commits. If a ledger with completed waves already exists for this branch, it's a **resume**: the workflow continues from the pending waves, preserving existing directives.

## Step 4 — Launch the workflow

Call `Workflow` with `scriptPath: .claude/workflows/implement.js` and `args` (a real JSON object, not a string):

```json
{
  "planPath": "<path to the approved plan>",
  "branch": "<branch>",
  "baseBranch": "<repo default branch, usually main>",
  "repoRoot": "<git rev-parse --show-toplevel>",
  "ledgerPath": "<repoRoot>/.claude/.implement/<slug>/ledger.md (absolute path)",
  "timestamp": "<date +%Y-%m-%dT%H-%M>",
  "deepPlanContractPath": "<path or null>",
  "prTitleHint": "<type(scope): description, in the repo's commit language>",
  "maxWaves": 8
}
```

The workflow runs in the background; wait for the `<task-notification>`. Track via `/workflows` if the user asks.

## Step 5 — Post-workflow

Read the result and report to the user **leading with the outcome**:

- `status: "pr-opened"` → PR URL, waves executed, commits, and the **Autonomous decisions** section (each point where a user input would have been requested: options considered + chosen path + why — also present in the PR description for the user to review and request changes).
- `status: "blocked" | "verify-failed" | "blocked-gate"` → what was committed/pushed so far, the blocked wave/step and the reason. Do **not** chain the review. If it's `blocked-gate` (a deep-plan gate hook blocked `gh pr create`), the path is to complete the plan-contract — never instruct the override token.

If `status: "pr-opened"` and **not** `NO_REVIEW` → **invoke the `review-fix-loop` skill** (Skill tool) with the PR number. When it finishes, consolidate both stages into a single final message.

---

## Workflow contract (what `implement.js` guarantees)

- **Setup**: validates the branch, seeds/resumes the ledger, splits the plan into ≤`maxWaves` waves (by plan phase, or by logical commit groups per the repo's commit conventions), and commits the deep-plan contract into `.claude/deep-plan/` when one exists (it's the artifact the PR gate hook reads on a sensitive-domain branch).
- **Implement**: one wave at a time, fresh agent per wave, with a single retry on `blocked`. Each wave: implements only its scope → runs the repo's **Verify** command incrementally (scoped to what it changed) plus the always-run gates from config → commits (per the repo's Conventions) → pushes the branch → updates the ledger with progress and decisions. **Closing gate**: a wave does not close with a contract item lacking a named test (or an explicit justification) nor with a new premise at `Tests: none yet` — untested commitments are the cheapest review findings to prevent at the source. **Dimension-row-on-mint**: a derived load-bearing quantity (monetary or otherwise) the plan doesn't specify gets a retroactive dimension row in the contract + a tested premise, in the same commit.
- **Verify**: the repo's full **Verify** command with a fix loop (≤3 attempts), then a `verify-plan` reconciliation (Missing/Diverged/Unplanned/UntestedPremises vs. Contract — the 4th bucket is the mechanical grep for `Tests: none yet` in the branch's premises) with 1 fix round; residuals become an explicit PR section, never silence.
- **PR**: rebase onto `origin/<baseBranch>` (re-verifies if the rebase brought changes), push, and `gh pr create` with an extensive body in the repo's PR language following its Conventions — summary, test plan, autonomous-decisions section, and conditional sections (payloads, rollback, tables) when applicable.
- **Decisions instead of questions**: workflow agents have no `AskUserQuestion`. At any decision point, the agent records the options, chooses the one that best serves the plan, and proceeds — the record appears in the ledger, the workflow output, and the PR description.
