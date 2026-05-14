---
name: deep-review
description: Multi-agent code review of a PR, branch, commit, or local changes through six lenses calibrated for the MonoDreams engine. Catches framework-fit problems, system-ordering bugs, missed downstream callers, ECS-purity violations, and premises that no test protects.
---

# deep-review

Six parallel agents, each wearing one lens, review the change. Then a
consolidation pass dedupes, verifies, and classifies findings. The
output is a single structured review the user can post to GitHub,
copy, save, or discard.

Always quote file paths and line numbers in findings. Never paraphrase
code from memory — every Blocker/High finding must be re-verified by
reading the actual file.

---

## Phase 0 — Resolve the input

Parse the user's argument and determine review mode. Run input
parsing yourself; do not delegate to an agent.

**Argument shapes:**
- *(no argument)* — review the **current branch** vs `origin/main`,
  including uncommitted working-tree changes. Diff:
  `git diff origin/main...HEAD` plus `git diff` for uncommitted.
- *URL containing `/pull/<n>` or starts with `#<n>` or pure numeric* —
  **PR mode**. Extract the PR number; check it out with
  `gh pr checkout <n>`; diff with `gh pr diff <n>`.
- *7–40 hex characters* — **commit mode**. Check out with
  `git checkout <sha>`; diff with `git diff <sha>~1 <sha>`.
- *Anything else not matching above* — treat as a branch name; check
  out with `git checkout`; diff vs `origin/main`.

**Eco / fast mode.** If the user passes `--eco` or `--cheap` or
`--fast`, swap the deep-thinking agents (System-Ordering,
Cross-Domain, ECS-Purity) to Sonnet via the agent `model: sonnet`
override. Keep Adjacent-Code and Component-Design on Opus regardless
— they're the highest-value lenses.

**Abort conditions.** Stop with a clear error if:
- The checkout fails. Do not proceed against the wrong branch — the
  whole review becomes hallucinated. Surface the git error to the user.
- The diff is empty. Tell the user there's nothing to review.
- The working tree has uncommitted changes that would be clobbered by
  the checkout. Ask before stashing.

**Capture context for later phases:**
- The full diff (save to a temp file if large).
- The list of files changed (`git diff --name-only` against the right
  base).
- The mode (PR / branch / commit / local) for the output phase.

---

## Phase 1 — Gather framework context

Read these files yourself (don't delegate). Every agent in Phase 2
gets the **diff + the full text of each of these** in its prompt.

1. **`docs/CORE_TENETS.md`** — always.
2. **`docs/<domain>/premises.md`** for each domain whose files were
   touched. Domain detection:
   - `MonoDreams/Component/Draw/**`, `MonoDreams/System/Draw/**`,
     `MonoDreams/Renderer/**` → **rendering**.
   - `MonoDreams/Component/Collision/**`,
     `MonoDreams/System/Collision/**` → **collision**.
   - `MonoDreams/Component/Physics/**`,
     `MonoDreams/System/Physics/**` → **physics**.
   - `MonoDreams/Component/Transform.cs`, `Component/ChildOf.cs`,
     `Component/LayoutNode.cs`, `System/HierarchySystem.cs`,
     `System/TransformCommitSystem.cs`, `System/SizeSystem.cs`,
     `System/LayoutSystem.cs`, `State/EntityHierarchy.cs` →
     **hierarchy-transform**.
   - `MonoDreams/Component/Level/**`, `MonoDreams/System/Level/**`,
     `MonoDreams/System/EntitySpawn/**`, `MonoDreams/EntityFactory/**`,
     `MonoDreams/Message/EntitySpawnRequest.cs`,
     `MonoDreams/Message/Level/**` → **level-loading**.
   - If a path is in `MonoDreams.Examples/**`, include the
     foundational five premises files (they exercise everything).
3. **`.claude/CLAUDE.md`** — always.
4. **`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs`** —
   the reference pipeline assembly. Agents that check ordering need
   this verbatim.

If a touched file's domain isn't in the V1 premises set (Camera,
Cursor, Input, Debug, Screen, Messages/State), note "no premises file
exists for this domain (deferred to V2)" and proceed; the other
agents still apply.

---

## Phase 2 — Spawn six parallel review agents

Send all six in a single message, each with `Agent` tool, type
`general-purpose` (or the eco-mode override). Pass each the diff + the
gathered context + its persona + its checklist.

Every agent returns findings in this format:

```
**[Blocker | High | Medium | Low] — <one-line title>**
File: <path>:<line>
What: <what the change does>
Why it's a problem: <which tenet/premise/contract it violates>
Suggested fix: <concrete fix or "needs design discussion">
```

If an agent has nothing to report, it returns `No findings.` Don't
ask agents to invent findings.

### Agent 1 — Adjacent-Code (mandatory, Opus)

**Persona.** You are the engineer who knows every reference to every
public symbol in MonoDreams. When a method signature changes, you've
already opened every caller. When an enum gains a value, you've
already checked every switch statement and filter. You catch the
"forgot to update X downstream" class of bug that diff-only reviewers
always miss.

**Embedded tenets.** Read CORE_TENETS §2 and §7. The framework is a
network of components, systems, and messages with implicit
references; a diff shows only what changed, not what should have
changed in response.

**Checklist.**
1. Extract semantic changes from the diff:
   - New `enum` values (`DrawElementType`, `RenderTargetID`,
     `CollisionType`, status enums, etc.).
   - Changed method or constructor signatures on public types.
   - New fields on components.
   - New status / mode / type discriminators.
   - New factory identifiers registered.
   - New layers (`ActiveLayers`) or render targets.
2. For each semantic change, grep the entire repo for callers and
   pattern matches:
   - `grep -rn` on the symbol name across `MonoDreams/`,
     `MonoDreams.Examples/`, `MonoDreams.Tests/`,
     `MonoDreams.YarnSpinner/`.
   - For enum values: grep the enum type and every `switch` /
     `case` / `==` / `.Where(... == ...)`.
3. Always grep these **engine anchors** even if the diff doesn't seem
   to touch them — they're the high-fan-out cores:
   - `MasterRenderSystem` — anywhere it switches on
     `DrawElementType`.
   - `TransformCollisionDetectionSystem` — anywhere it dispatches by
     collider type.
   - `EntitySpawnSystem.RegisterFactory` — anywhere a factory is
     registered, and the dispatch site.
   - `HierarchySystem` — anywhere child / parent links are read.
   - `LevelLoadRequestSystem` — anywhere it adds
     `CurrentLevelComponent`.
   - `LoadLevelExampleGameScreen.cs` — the reference pipeline
     assembly.
4. For each grep hit, open the file and verify the code handles the
   new behavior. If not → finding.
5. **Inverse check.** Does the new code handle all *existing*
   scenarios it didn't before? A new filter that doesn't cover all
   pre-existing enum values is the same bug class as a missed caller.

### Agent 2 — System-Ordering / Pipeline (mandatory, Opus)

**Persona.** You are the engineer who has walked
`LoadLevelExampleGameScreen.cs:277–331` more times than anyone. You
think in pipeline phases: input → logic → physics (Movement →
Velocity → Detection → Resolution → Commit) → hierarchy → camera →
cull → prep → render. Ordering bugs are silent — the wrong order
doesn't throw, it just produces stale data.

**Embedded tenets.** CORE_TENETS §7 — the reference pipeline. The
ECS-purity rule: systems are pure functions; ordering is the screen's
responsibility. A system that quietly depends on another system
having run is a bug, and the assembler that orders them is the place
to catch it.

**Checklist.**
1. Identify any new system added by the diff, or any system whose
   registration order changed.
2. For each: determine where it lives in the reference pipeline
   shape. Use the per-frame phases as anchors:
   - Input
   - Game logic
   - Physics block (Movement → Velocity → Detection → Resolution →
     Commit)
   - Hierarchy / size / layout
   - Camera
   - Cursor
   - Render block (Culling → Sprite prep → YSort → Text prep → Mesh
     prep → Master render → Debug overlays)
3. Check the screen's registration site for the new/moved system.
   Confirm it lands in the correct phase.
4. **Execution-order check.** A new filter / query that runs before a
   transition or commit that hasn't fired yet returns empty. Read
   each Phase 2 system that filters by `Visible`, by `ColliderTag`,
   by `CurrentLevelComponent`, by `Velocity`, by `Transform.Delta`,
   etc., and verify the system that *produces* that filter is
   ordered before it.
5. If a touched system reads `Transform.WorldPosition` /
   `WorldRotation` / `WorldScale`, verify `HierarchySystem` runs
   before it.
6. If a touched system reads `Transform.Delta`, verify
   `TransformCommitSystem` is at the tail of the previous frame
   (i.e., last in the pipeline before the new frame).
7. If a touched system writes `LayerDepth`, verify it runs **before**
   `MasterRenderSystem` and check whether it stomps on `YSortSystem`.

### Agent 3 — Component-Design / Framework-Fit (mandatory, Opus)

**Persona.** You are the engineer who hates the engine's primary
failure mode: hyper-specialized components that solve today's pain
but don't generalize. You ask "would this primitive be useful to a
feature 6 months from now under a different name?" before any new
component lands. You also know every existing component and can
spot when a PR re-invents one under a new name.

**Embedded tenets.** CORE_TENETS §1 (framework, not library) and §2
(ECS purity & composition). The single highest-value finding type
is: *added a super specialized component/system that solves one
specific pain point right now but doesn't evolve well in the framework
to deliver value for more implementations and play nice with other
features in MonoDreams.*

**Checklist.**
1. For each new component (any file added under
   `MonoDreams/Component/**` or `MonoDreams.Examples/Component/**`):
   1. Verify it is **pure data**. No methods beyond trivial property
      getters / setters. Any logic-shaped method → finding.
   2. Search for existing components that could be extended instead.
      Look at `Transform`, `DrawComponent`, `SpriteInfo`,
      `RigidBody`, `Velocity`, `BoxCollider`, `ConvexCollider`,
      `EntityInfo`, `CameraFollowTarget`, and the LayoutNode /
      ChildOf / Visible family. If the new component overlaps any of
      these conceptually, propose extending the existing one instead.
   3. Ask the framework-fit question: *would another feature reach
      for this primitive under a different name?* If yes, the
      naming/shape is game-shaped, not framework-shaped. Propose a
      generalization.
   4. Verify namespace placement. New components in
      `MonoDreams/Component/**` or
      `MonoDreams.Examples/Component/**`. **Never** put one under
      `MonoDreams.Entity` (shadows `DefaultEcs.Entity`).
2. For each new system, ask the same generalization question: is
   this a framework primitive, or did we hard-code today's feature?
3. If the diff adds a new draw/render component (anything that
   carries pixels or layer-depth besides `DrawComponent`,
   `SpriteInfo`, `DynamicText`, `NinePatchInfo`): **Blocker**. The
   unification of rendering through `DrawComponent` is one of the
   most load-bearing tenets — see Rendering premises.

### Agent 4 — Cross-Domain Dependency (Opus, eco-swappable to Sonnet)

**Persona.** You are the engineer who keeps the premises files'
`Depends on:` graph in your head. You know that "swept collision
reads `Transform.Delta`" depends on "Delta is meaningful only after
`TransformCommitSystem` ran", and you flag any PR that touches one
side without considering the other.

**Embedded tenets.** Each domain's `premises.md` `Depends on:`
section. CORE_TENETS §2 — premises with `Depends on:` cross-refs
are how the framework's parts know they're connected.

**Checklist.**
1. List the domains touched by the diff (use the same routing as
   Phase 1).
2. For each *other* domain's `premises.md`, scan the `Depends on:`
   lines. Whenever a depends-on points back at a touched domain,
   open both premises and verify the diff still honors the
   dependency.
3. For any new or modified premise (file-level), check that any
   premise in other domains that should now `Depends on:` it is
   listed. Missing dependencies in `Depends on:` are *also* findings
   — they accumulate silently and break the cross-domain agent on
   future PRs.
4. Cross-reference contracts: if the diff changes `CollisionMessage`,
   `EntitySpawnRequest`, `LoadLevelRequest`, or any other public
   message, walk every consumer and confirm it handles the new
   shape.

### Agent 5 — Premises / Test-Coverage (Opus, eco-swappable to Sonnet)

**Persona.** You are the engineer who maps every premise in
`docs/{domain}/premises.md` to a test in `MonoDreams.Tests/`. You
flag two things: premises with `Tests: none yet` that the diff
makes urgent, and brand-new premises the diff introduces that the
docs don't yet name.

**Embedded tenets.** CORE_TENETS §2 — "Public contracts must be
backed by a test or example that exercises them." Every premises
file's "Follow-up debt" section.

**Checklist.**
1. For each premises file relevant to the diff (per Phase 1
   routing):
   - List the premises with `Tests: none yet`.
   - For each, ask: does the diff exercise this premise's path? If
     yes, and the diff didn't add a test, **Medium** finding:
     "premise X is now exercised by new code but still has
     `Tests: none yet`; consider adding a test in this PR."
2. Walk the diff for **new premises the docs don't yet name.** A new
   component, system, message, or invariant introduced by the PR is
   itself a new premise the docs should record. Examples:
   - New tag component → who manages it? Premise.
   - New message type → who emits, who consumes? Premise.
   - New system that depends on another running first → ordering
     premise.
   - For each, propose the premise text (one paragraph + Why /
     Breaks / Tests / Depends on).
3. If the diff adds a new public message, component, or system
   without a test or example, **High** finding:
   "Public contract added without a test/example that exercises it.
   CORE_TENETS §2 requires every contract to be maintained — without
   a use site it is indistinguishable from dead code."

### Agent 6 — ECS-Purity (Opus, eco-swappable to Sonnet)

**Persona.** You are the engineer who enforces the architectural
discipline. Components are pure data; systems are pure functions;
ordering belongs to the assembler. You flag any drift: logic creeping
into components, parallel renderers, shadowed namespaces, systems
that read state nobody told them about.

**Embedded tenets.** CORE_TENETS §2 — ECS purity & composition.

**Checklist.**
1. **Component purity.** Any new or modified `MonoDreams/Component/**`
   or `MonoDreams.Examples/Component/**` file: check that every
   member is a field or a trivial property. Methods that do logic →
   finding.
2. **System purity / pure-function shape.** A system that internally
   assumes another system ran first (without that being arranged by
   the screen) → finding. Watch for `Logger.Warning("X did not run
   yet")` patterns, or fallbacks that paper over ordering bugs.
3. **No parallel renderers.** Anything that calls `SpriteBatch.Draw`,
   `SpriteBatch.Begin`, etc. outside `MasterRenderSystem` → Blocker.
4. **No parallel level loading paths.** Anything that adds
   `CurrentLevelComponent` outside `LevelLoadRequestSystem` (excluding
   tests/tools that legitimately use the component-driven trigger
   pattern) → High.
5. **Namespace integrity.** Anything placed under `MonoDreams.Entity`
   → Blocker (shadows `DefaultEcs.Entity`). Anything placed under
   `MonoDreams.Camera` namespace conflicting with the `Camera` class
   → High.
6. **`Visible` ownership.** Game code that adds or removes `Visible`
   outside `CullingSystem` → High (see Rendering premises).
7. **`ColliderTag` ownership.** Manual adds/removes outside the
   auto-apply machinery → High.

---

## Phase 3 — Consolidate

Receive all six agent reports. Then:

1. **Dedupe.** Multiple agents will catch the same bug from
   different angles. Keep the most specific framing; cite which
   lenses caught it.
2. **Verify every Blocker and High.** For each, read the actual file
   at the cited path and confirm the code matches what the agent
   described. **Never dismiss a finding because the local file
   "looks different" — that means you may be on the wrong branch.
   Recheck Phase 0 first.** If the file genuinely doesn't say what
   the agent claimed, demote or drop the finding, and note the
   agent hallucinated.
3. **Classify.**
   - **Blocker** — framework-violating, will silently corrupt
     gameplay or the engine model (parallel renderer, namespace
     shadowing, missed enum case in critical filter).
   - **High** — premise violation, missing test for newly-exercised
     premise, framework-fit problem on a public primitive, missed
     downstream caller.
   - **Medium** — convention deviation, missing test for premise
     that *might* be exercised, opportunity to extend an existing
     primitive instead of adding a new one.
   - **Low** — naming nits, doc updates, refactor opportunities,
     premise-text suggestions.

---

## Phase 4 — Present and route

Render the consolidated review as a single markdown block:

```
# Deep review of <PR #N | branch <name> | commit <sha> | local changes>

**Mode:** <pr/branch/commit/local>
**Base:** <origin/main or <sha>~1>
**Files changed:** <count>
**Lenses applied:** <list of 6 agents>

## Blockers
<findings or "None.">

## High
<findings>

## Medium
<findings>

## Low
<findings>

## New premises proposed
<from Agent 5 — premise-text drafts that the docs should record after the PR lands>

## Premises now urgent
<from Agent 5 — `Tests: none yet` items the PR exercises>
```

Then ask the user where to send it via `AskUserQuestion`:

- *(PR mode only)* **Post to GitHub** — `gh pr review <n> --comment
  --body-file <tempfile>`. Confirm with the user before posting; the
  comment is visible publicly.
- **Copy to clipboard** — `pbcopy < <tempfile>` on macOS.
- **Save to file** — at a path the user picks, default
  `./deep-review-<timestamp>.md`.
- **Nothing** — keep the review in chat only.

If the user declines any action, the review still exists in the chat
transcript for their reference.

---

## Operational notes

- **Don't be cautious for the sake of it.** A "Low" finding that
  says "consider naming this differently" is noise unless the name
  actively misleads. Agents should return `No findings.` more often
  than they probably will at first; iterate the prompts above if
  Low-finding inflation becomes a problem.
- **Hallucinated paths are the #1 failure mode of Phase 2.** That's
  why Phase 3 re-verifies every Blocker/High. Trust the file on
  disk, not the agent's recollection.
- **The skill is calibrated for MonoDreams.** It cites tenets,
  premises, and anchors specific to this engine. Do not run it in
  another repo without re-tuning the agent personas and anchors.
- **Premises evolve.** When Phase 4 surfaces new premises Agent 5
  proposed, encourage the user to fold them into the relevant
  `docs/<domain>/premises.md` after merging. The skill's value
  compounds with the docs.
