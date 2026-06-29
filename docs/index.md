# MonoDreams docs

Routing index. Engine-wide tenets live here; per-module invariants live
next to each module's source.

## Engine-wide

| Doc | What's in it |
|---|---|
| [`CORE_TENETS.md`](CORE_TENETS.md) | Engine-wide invariants: framework-not-library, ECS purity, hierarchy, rendering, physics, level loading, reference pipeline, debug, the editor-is-part-of-the-game run-state contract, refactor backlog. **Load this first** for any non-trivial task. |
| [`web-targeting.md`](web-targeting.md) | Targeting the web browser via KNI/BlazorGL: the shared `.Core` + per-platform heads model, `$(MonoDreamsPlatform)` backend selection, the CLI `--platform` flag, per-platform content build (incl. the macOS/Linux MGCB native-lib shim), and the open Reach 32-bit-index render limit. |
| [`level-editor/roadmap.md`](level-editor/roadmap.md) | The in-game level editor's Wave A–F map: each wave's seam, dependencies, decisions made-vs-deferred (incl. the deferred render forks for E/F), and the three foundation seams (run-state model, native scene format, serializer registry) every wave plugs into. The cross-session continuity artifact. |
| [`../MonoDreams/level-editor/docs/scene-format.md`](../MonoDreams/level-editor/docs/scene-format.md) | The native MonoDreams scene format: `version` / `camera` / `layers[]` / reserved `sources[]` / `entities[]` with `components{}` + `parent`, the engine component-type keys, and a concrete JSON example. |

## Per-module docs

Each of the 14 modules ships its own `docs/` subfolder colocated with the
module source. Read `overview.md` for the tour (purpose, components,
systems, wiring), `premises.md` for the load-bearing invariants.

| Module | Overview | Premises |
|---|---|---|
| `foundation` | [overview](../MonoDreams/foundation/docs/overview.md) | [premises](../MonoDreams/foundation/docs/premises.md) |
| `rendering` | [overview](../MonoDreams/rendering/docs/overview.md) | [premises](../MonoDreams/rendering/docs/premises.md) |
| `rendering-text` | [overview](../MonoDreams/rendering-text/docs/overview.md) | [premises](../MonoDreams/rendering-text/docs/premises.md) |
| `camera` | [overview](../MonoDreams/camera/docs/overview.md) | [premises](../MonoDreams/camera/docs/premises.md) |
| `physics` | [overview](../MonoDreams/physics/docs/overview.md) | [premises](../MonoDreams/physics/docs/premises.md) |
| `collision` | [overview](../MonoDreams/collision/docs/overview.md) | [premises](../MonoDreams/collision/docs/premises.md) |
| `level-loading` | [overview](../MonoDreams/level-loading/docs/overview.md) | [premises](../MonoDreams/level-loading/docs/premises.md) |
| `level-ldtk` | [overview](../MonoDreams/level-ldtk/docs/overview.md) | [premises](../MonoDreams/level-ldtk/docs/premises.md) |
| `level-blender` | [overview](../MonoDreams/level-blender/docs/overview.md) | [premises](../MonoDreams/level-blender/docs/premises.md) |
| `ui` | [overview](../MonoDreams/ui/docs/overview.md) | [premises](../MonoDreams/ui/docs/premises.md) |
| `cursor` | [overview](../MonoDreams/cursor/docs/overview.md) | [premises](../MonoDreams/cursor/docs/premises.md) |
| `dialogue` | [overview](../MonoDreams/dialogue/docs/overview.md) | [premises](../MonoDreams/dialogue/docs/premises.md) |
| `debug` | [overview](../MonoDreams/debug/docs/overview.md) | [premises](../MonoDreams/debug/docs/premises.md) |
| `level-editor` | [overview](../MonoDreams/level-editor/docs/overview.md) | [premises](../MonoDreams/level-editor/docs/premises.md) |

Premises follow the format **Why / Breaks / Tests / Depends on**, with
optional `Open questions`, `Aspirational direction`, and `Follow-up debt`
sections at the bottom. See [`CORE_TENETS.md`](CORE_TENETS.md) §"Premises"
for the full convention.

## Review flows (lenses)

One flow doc per module, under [`flows/`](flows/). Each reads like a
dedicated core-tenet for that module — the end-to-end **path** state takes
through it, its lifecycle/ordering, curated invariants (linking to the
module's premises), load-bearing quantities, and failure modes. The
`deep-review` / `deep-plan` skills spawn one dedicated lens per flow doc
whose `covers:` globs intersect a change, so review/planning agents scale
with the change's module footprint (see
[`agents/skills-config.md`](agents/skills-config.md) › Flows). Flows marked
**sensitive** trip the heavy planning/review path + the PR gate.

| Flow | Sensitive | Path in one line |
|---|---|---|
| [`foundation`](flows/foundation.md) | ✅ | Per-frame screen heartbeat: input → movement → hierarchy/transform → world-space readers → commit. |
| [`platform`](flows/platform.md) | ✅ | Head picks `$(MonoDreamsPlatform)` → flows into `.Core` → backend NuGet + per-platform content build. |
| [`rendering`](flows/rendering.md) | | Draw path: cull → prep → Y-sort → render → composite across Main/UI/HUD targets. |
| [`rendering-text`](flows/rendering-text.md) | | Revealable BitmapFont text: component → update → prep → `DrawComponent` text element. |
| [`camera`](flows/camera.md) | | Follow target → camera position, ordered before culling/render reads it. |
| [`physics`](flows/physics.md) | | Physics tick: gravity → velocity integrate → handoff to collision (Transform.Delta). |
| [`collision`](flows/collision.md) | | Detection (AABB + SAT, swept via Delta) → `CollisionMessage` → resolution. |
| [`level-loading`](flows/level-loading.md) | | `LoadLevelRequest` → `CurrentLevelComponent` / `EntitySpawnRequest` → factory-by-id dispatch. |
| [`level-ldtk`](flows/level-ldtk.md) | | Component-driven LDtk parse (tiles + entities) on `CurrentLevelComponent` add. |
| [`level-blender`](flows/level-blender.md) | | Message-driven Blender parse; the exporter↔parser JSON contract. |
| [`ui`](flows/ui.md) | | Auto-layout: build tree → intrinsic sizing (bottom-up) → flexbox placement (top-down). |
| [`cursor`](flows/cursor.md) | | Poll input → project across coordinate spaces → paint cursor on top (HUD). |
| [`dialogue`](flows/dialogue.md) | | Yarn node → runner steps lines → state machine → reveal text + commands. |
| [`debug`](flows/debug.md) | | Opt-in, read-only collider/sprite overlays + periodic screenshot capture. |
| [`level-editor`](flows/level-editor.md) | ✅ | In-game `Edit` run mode over the real pipeline; `GatedSystem` freezes game logic while render/input/cursor/hierarchy stay live (scaffold). |

## Contributor docs

| Doc | What's in it |
|---|---|
| [`../README.md`](../README.md) | Project README — for end users (game devs). Quickstart with the CLI, module tree, naming conventions. |
| [`../CONTRIBUTING.md`](../CONTRIBUTING.md) | Contributor setup: prereqs by OS, build commands, adding a new module, manifest validation, test workflow. |
| [`../MonoDreams/MODULES.md`](../MonoDreams/MODULES.md) | Module manifest schema and authoring guide. |
| [`../.claude/CLAUDE.md`](../.claude/CLAUDE.md) | Coding conventions, module-to-premises mapping, workflow rules. Loaded automatically by Claude Code; humans should also read it. |
| [`agents/skills-config.md`](agents/skills-config.md) | Per-repo config the engineering skills read — stack, verify command, docs layout, domains, sensitive domains, flows, conventions. |
| [`../.claude/skills/`](../.claude/skills/) | The engineering pipeline (`refine` → `deep-plan` → `implement` → `review-fix-loop`, plus `deep-review`, `verify`, `verify-plan`, `setup`, `bootstrap`). Run `/deep-review` on a PR, branch, or commit. |
