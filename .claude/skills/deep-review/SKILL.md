---
name: deep-review
description: "Perform a thorough code review using parallel domain-specialized agents and core business tenets. Accepts a PR number/URL, branch name, commit SHA, or reviews current local changes by default."
---

Perform a thorough code review. Accepts flexible input:

- `/deep-review` — review current local changes (committed + uncommitted) against origin/main
- `/deep-review 542` or `/deep-review #542` — review a PR by number
- `/deep-review <pull-request URL>` — review a PR by URL
- `/deep-review feat/my-branch` — review a branch against origin/main
- `/deep-review abc1234` — review a single commit

Append `cheaper` (aliases: `cheap`, `simple`, `eco`, `economy`) to use tiered model routing for token efficiency. Default is all-Opus.

This skill reads per-repo configuration from `docs/agents/skills-config.md` (written by the `setup` skill) — stack, verify command, docs layout, domains, sensitive domains, flows, conventions. It hardcodes nothing stack-specific; where a needed section is absent it falls back to a stated default and says so in its output.

---

## Model Routing

The review fans out one agent per **lens**. The universal lenses always run; each **flow** the change touches (`docs/agents/skills-config.md` › Flows — one doc per flow) runs as one `flow-lens` agent. Below, "universal lens" rows are fixed; the flow-lens row applies per touched flow doc.

**Default (full):** All phases and agents use **Opus** with extended thinking.

**Economy mode** (when `cheaper`/`cheap`/`simple`/`eco`/`economy` is passed):

| Phase / Agent | Model | Reason |
|---------------|-------|--------|
| Universal — Derived-Quantity | **Opus** (extended thinking) | Cross-domain writers-closure reasoning |
| Universal — Adjacent Code Paths | **Sonnet** (extended thinking) | Structured grep + verify pattern |
| Universal — Negative Space | **Sonnet** (extended thinking) | Structured complement checklists |
| Universal — Contract × Code | **Sonnet** | Mechanical contract↔code diffing |
| Universal — Test Coverage & Premises | **Sonnet** | Semi-structured gap analysis |
| Flow lens (per touched flow doc) | **Opus** (extended thinking) | Deep, repo-specific multi-stage reasoning |

Consolidation (Phase 3), facet enumeration (Phase 3.5) and adversarial verification (Phase 3.75) always use **Opus** regardless of mode.

When the change touches a **Sensitive domain** (`docs/agents/skills-config.md` › Sensitive domains), take the **heavy path**: run the full lens fan-out + facet enumeration + adversarial verification. When no configured Sensitive domain is touched (or none are listed), the change is on the **light path** — run the universal lenses and consolidate, but you may skip enumeration/verification when there are zero Blocker/High, and the review never blocks.

---

## Phase 0: Input Resolution

Parse arguments to determine the **review target** and **model tier**.

### Step 0a: Detect model tier

If any argument matches `cheaper`, `cheap`, `simple`, `simpler`, `eco`, or `economy` → set **Economy mode**. Remove that token from the arguments. Otherwise → **Full mode**.

### Step 0b: Detect review target

With remaining arguments, apply the first matching rule:

1. **No arguments** → **Local mode**: review current branch vs origin/main, including uncommitted changes
2. **URL containing `/pull/`** → **PR mode**: extract the PR number from the URL
3. **Numeric value or `#N`** → **PR mode**: use as PR number
4. **Hex string, 7-40 characters** → **Commit mode**: review a single commit
5. **Anything else** → **Branch mode**: treat as a branch name

### Step 0c: Prepare the code and diff

**PR mode:**

CRITICAL: You must be on the PR's code before agents read any files.

```bash
# Try branch checkout first (cleanest)
gh pr checkout <number>

# If that fails (e.g., branch in another worktree), use detached HEAD:
PR_SHA=$(gh pr view <number> --json headRefOid -q .headRefOid)
git fetch origin "$PR_SHA" 2>/dev/null || true
git checkout "$PR_SHA"
```

Verify: `git log --oneline -3` — latest commits must match PR commits.
If both fail, abort — do NOT proceed with agents reading the wrong branch.

Get the diff: `gh pr diff <number>`
Get metadata: `gh pr view <number> --json title,body,additions,deletions,files`

**Branch mode:**

```bash
git checkout <branch>
git fetch origin main
```

Verify: `git log --oneline origin/main..HEAD` — must have commits ahead of origin/main. If none, abort.

Get the diff: `git diff origin/main...HEAD`
Get stats: `git diff --stat origin/main...HEAD` and `git log --oneline origin/main..HEAD`

**Local mode:**

```bash
git fetch origin main
```

No checkout needed — you're already on the working branch. This mode includes uncommitted changes.

Get the diff: `git diff origin/main` (compares working tree to origin/main — includes both committed and uncommitted changes)
Get stats: `git diff --stat origin/main`, `git log --oneline origin/main..HEAD`, and `git status`

**Commit mode:**

```bash
git fetch origin main
```

Get the diff: `git diff <SHA>~1 <SHA>`
Get stats: `git show --stat <SHA>`

---

## Phase 1: Context Gathering

Read the repo config first: **`docs/agents/skills-config.md`** — it supplies the docs layout, domain map, sensitive domains, flows dir, and conventions used below. If the file is absent, proceed with the defaults noted at each step and say so in the review header.

1. **Diff and metadata**: Collected in Phase 0 above. For PR mode, also get title, description, file list. Do NOT read existing PR comments or reviews.

2. **Domain documentation**: Read the **core tenets** doc for business invariants (path from config › Docs layout › Core tenets; default `docs/CORE_TENETS.md`). Detect which domains the diff touches via config › Domains (path-glob → domain), and read the relevant **schema** docs for those domains (config › Docs layout › Schema, if present).

3. **Premises files**: For each affected domain, read its premises file (path from config › Docs layout › Premises, substituting `{domain}`/`{module}` per the configured pattern; default `docs/{domain}/premises.md`) if it exists. These document technical invariants tests must protect. Pass premises contents to all agents.

4. **Code conventions**: Read the repo's conventions doc(s) — its root `CLAUDE.md`/`AGENTS.md` and any rules dir (config › Docs layout › Rules dir).

5. **Affected domains**: From the diff, list which domain areas are touched (per config › Domains). Note which, if any, are **Sensitive** (config › Sensitive domains) — that decides heavy vs light path.

**Pass to all agents**: Full diff text, core-tenets doc, relevant schema docs, premises files, conventions doc, list of affected domains. In PR mode, also pass the title and description. Tell agents the docs-layout patterns so they can resolve further paths themselves.

---

## Phase 2: Parallel Review Agents

Launch **all lens agents in a single message** (parallel Agent calls). Each agent's role spec lives in its own file under `.claude/skills/deep-review/agents/` so the same spec can be reused by other consumers (e.g. the `review-fix-loop` skill and any CI review workflow).

**Universal lenses (always run):**

| Lens | Role file |
|---|---|
| Adjacent Code Paths | `.claude/skills/deep-review/agents/adjacent-code.md` |
| Derived-Quantity Auditor | `.claude/skills/deep-review/agents/derived-quantity.md` |
| Negative Space & Scope Complement | `.claude/skills/deep-review/agents/negative-space.md` |
| Plan-Contract × Code Reconciler | `.claude/skills/deep-review/agents/contract-reconciler.md` |
| Test Coverage & Premises | `.claude/skills/deep-review/agents/test-coverage.md` |

These five are the lens model the whole pipeline depends on: the universal failure shapes that are not specific to any domain — an un-enumerated cross-domain writer of a derived quantity, the complement of a poller's scope, and a code↔contract contradiction that is a note in everyone's prompt and therefore nobody's job.

**Flow lenses (one per flow doc the change touches):**

Read the **Flows dir** (`docs/agents/skills-config.md` › Flows; default `docs/flows/`). For each flow doc whose frontmatter `covers:` globs intersect the changed files — or every flow doc, if a doc has no `covers` or the change is broad — spawn one agent that reads `.claude/skills/deep-review/agents/flow-lens.md` and is given that flow's doc (path + content) as its spec. If there is no flows dir or no matching flow doc, spawn none — the universal set runs alone. (A money repo's `docs/flows/` might hold `payment-pipeline.md`, `dsa-lifecycle.md`, `payout.md`; a game engine's might hold `level-load.md`, `collision-resolution.md`. The flow doc, not this skill, carries the domain knowledge.)

The Contract × Code lens short-circuits to `_No findings._` when the branch carries no plan-contract — it costs almost nothing on un-planned changes.

For each agent: launch with `subagent_type: general-purpose`. In Economy mode set `model: opus` or `model: sonnet` per the routing table (flow lenses default to Opus); in Full mode all use Opus. The prompt template is:

> Read `.claude/skills/deep-review/agents/<role-file>.md` and operate as that
> specialist. [For a flow lens, also: You are the `<flow>` lens; your flow doc
> follows — review the diff against what it says must hold:\n`<flow doc content>`.]
> Apply your role to the
> Phase 1 context provided below. You have full file-access tools (Read, Grep,
> Bash) — use them liberally; scope `rg` to the repo.
>
> **Phase 1 context**:
> {full diff, core-tenets doc, relevant schema docs, relevant premises files,
>  conventions doc, docs-layout patterns, list of affected domains, title/body
>  if applicable}
>
> Output: a markdown findings document with the severity bucket structure
> defined in your role file. Do NOT post to GitHub or any external system —
> return findings to the orchestrator.

**Shared instruction**: You are on the review target's code. When you Read or Grep files, you see the proposed code. The diff and the files on disk should be consistent. If they appear inconsistent, flag the discrepancy — do NOT silently dismiss findings. Analyze the diff first to identify potential issues, then read full files for surrounding context. The diff is the source of truth for what is being proposed.

---

## Phase 3: Consolidation

After all agents complete, read `.claude/skills/deep-review/agents/consolidate.md` for the consolidation logic (deduplication, verification of high-severity claims — demotion only with read-code evidence, severity classification keyed to config › Sensitive domains, surface keys for every Blocker/High, output structure).

## Phase 3.5: Facet Enumeration (exhaust hot surfaces)

A confirmed Blocker/High proves its surface is hot, and defects on hot surfaces come in families — one query can yield many Blocker/High across many review rounds (one facet per round) because each pass finds one defect and moves on. Don't move on: exhaust the surface in this same review.

1. Group the consolidated Blocker/High findings by their `surface` key.
2. For each distinct surface (cap **6 per review**, Blocker surfaces first), launch one enumeration agent in parallel: it reads `.claude/skills/deep-review/agents/enumerate.md` and receives the surface key + its seed findings + the Phase 1 context pointer. Model: Opus.
3. Merge the enumerators' NEW findings into the consolidated review (mark their origin as `enumeration`), deduplicating against existing entries.
4. Record exhaustion: each enumerator returns `dry` plus a facet table. If any facet came back `unchecked`, or any enumerator found new Blocker/High, say so in the review header — the review is then **not exhaustive** and a follow-up pass on those surfaces is warranted.

Skip this phase only when the consolidated review has zero Blocker/High (this is also the only skip the light path takes when not touching a Sensitive domain).

## Phase 3.75: Adversarial Verification (attack every Blocker/High before publishing)

A mis-priced finding misdirects the whole fix round. A blind adversarial pass over the finished review routinely re-prices a fraction of Blocker/High and refutes the core mechanism of some — errors in BOTH directions (a "declared design" kept too high; a "stranded forever" whose recovery arm existed; a finding whose own refinement proved its arm unreachable). Attack your own findings before the user sees them:

1. Group ALL consolidated Blocker/High findings — lens and enumeration origin alike — by `surface` key.
2. For each surface, launch one verification agent in parallel: it reads `.claude/skills/deep-review/agents/verify.md` and receives the surface's findings **with agent/origin attribution stripped**, plus the Phase 1 context pointer. Model: Opus.
3. Apply verdicts to the review body:
   - `REFUTED` → remove the finding; keep a one-line entry in a `### ⚖️ Refuted in verification` section quoting the killing evidence (so the next round doesn't re-mine it).
   - `REPRICED` → move to the verdict's severity bucket, appending the verifier's reason.
   - `CONFIRMED` → fold the verifier's `corrections` into the finding text (sharper evidence, fixed line numbers).
   - `UNVERIFIABLE` → keep the severity, append the "what's missing" note.
4. Verification does not re-trigger enumeration. A surface whose seed was refuted keeps its enumeration findings only where those were independently confirmed.

Skip this phase only when there are zero Blocker/High findings. Economy mode does NOT skip it — mis-priced Blockers cost the most exactly when the review was cheap.

For the terminal output, prepend this header to the body produced by the consolidator (merged with the enumeration findings, post-verification):

```markdown
## Code Review: {title or branch name}

**Mode**: {Full Opus | Economy (tiered)}
**Path**: {Heavy (sensitive domain touched) | Light}
**Target**: {PR #N | branch name | commit SHA | local changes}
**Scope**: {files changed}, +{additions} / -{deletions}
**Affected domains**: {list}
**Exhaustion**: {hot surfaces enumerated dry | N surfaces still hot / M facets unchecked — not exhaustive}
**Verification**: {N surfaces attacked — X confirmed / Y repriced / Z refuted}
```

## Phase 4: Review Output

After presenting the consolidated review to the user, ask what they'd like to do with it using `AskUserQuestion`. Offer these options:

1. **Post to GitHub** (PR mode only) — Post the review as a PR comment using `gh pr review <number> --comment --body "..."`. Only offer this option when in PR mode.
2. **Copy to clipboard** — Copy the full review markdown to the system clipboard using `pbcopy` (macOS) or `xclip`/`xsel` (Linux).
3. **Save to file** — Save to a file. Suggest a default path like `/tmp/review-{branch-or-pr}.md` and let the user override.
4. **Nothing** — Just leave it in the conversation output.
5. **Promote a recurrent finding class** — When a finding in this review is the *same root cause* that has appeared in an earlier change (or is general enough to obviously recur), promote it into the repo's recurring-failure-modes doc (config › Docs layout › Planning; e.g. `docs/planning/recurring-failure-modes.md`) as a new `FM-N` entry (Mined-from / Trigger / The failure / Artifact response) **and** add the protecting invariant to the owning domain's premises file. This is the review→planning feedback loop: it makes the lesson a question every future `/deep-plan` must answer, so the next plan consults it instead of rediscovering it in another N rounds. Only offer/act on this when a finding genuinely meets the recurrence bar — do not promote one-offs. If config lists no Planning path, note that and skip this option.

Example prompt:
> What would you like to do with this review?
> 1. Post as PR comment on GitHub (PR #N)
> 2. Copy to clipboard
> 3. Save to file (default: /tmp/review-{name}.md)
> 4. Nothing — keep it in the conversation
> 5. Promote a recurrent finding class → recurring-failure-modes + premise

If the user picks option 1 (GitHub), format the review as a single PR comment. Use a HEREDOC with `gh pr review` to avoid shell escaping issues. If the review is very long (>65000 chars), split into multiple comments.

If the user picks option 2, pipe the review through `pbcopy` on macOS or detect the platform and use the appropriate clipboard command.

If the user picks option 3, write the file and confirm the path.

---

## Key Rules

- **Sensitive-domain impact is king**: in a domain the repo marks **Sensitive** (config › Sensitive domains), bugs that lose or corrupt money/state, double-count, or miscalculate are ALWAYS Blockers regardless of likelihood. If no Sensitive domains are configured, classify on data-corruption / broken-flow impact and never auto-block.
- **Adjacent code is where the hardest bugs hide**: code NOT in the diff but broken by the changes is the highest-value finding category.
- **The diff is the source of truth**: when verifying findings, the diff takes precedence over local file reads. If they disagree, you may be on the wrong branch.
- **Verify before reporting**: if an agent claims "X doesn't handle Y", grep for it. False positives erode trust.
- **Core tenets are non-negotiable**: every agent checks findings against the embedded tenets — violations are automatic blockers in a sensitive domain.
- **Clean first pass**: never read existing PR comments — provide an independent perspective.
- **A finding proves a surface is hot — exhaust it now**: one defect on a derived quantity, formula, or hook predicts siblings on the same surface. Phase 3.5 exists so the family arrives in one review instead of one facet per round.
- **"Clean" means enumerated dry, not merely quiet**: a review with zero Blocker/High is exhaustive evidence only if it had nothing hot to enumerate; a review whose enumerators found more, or left facets unchecked, must say so in the Exhaustion header line.
- **Every Blocker/High survives its own refutation before publishing**: Phase 3.75 attacks each hot surface blind (provenance stripped, refute-first). A wrong severity misdirects the fix round more than a missing finding does.
- **No single run exhausts the surface**: running the same pipeline twice on the same SHA produces only partially overlapping Blocker/High sets. Treat "not exhaustive" headers literally; re-run after fixes land.
