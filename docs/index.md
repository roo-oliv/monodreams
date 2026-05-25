# MonoDreams docs

Routing index. Engine-wide tenets live here; per-block invariants live
next to each block's source.

## Engine-wide

| Doc | What's in it |
|---|---|
| [`CORE_TENETS.md`](CORE_TENETS.md) | Engine-wide invariants: framework-not-library, ECS purity, hierarchy, rendering, physics, level loading, reference pipeline, debug, refactor backlog. **Load this first** for any non-trivial task. |

## Per-block docs

Each of the 15 blocks ships its own `docs/` subfolder colocated with the
block source. Read `overview.md` for the tour (purpose, components,
systems, wiring), `premises.md` for the load-bearing invariants.

| Block | Overview | Premises |
|---|---|---|
| `foundation` | [overview](../MonoDreams/foundation/docs/overview.md) | [premises](../MonoDreams/foundation/docs/premises.md) |
| `rendering` | [overview](../MonoDreams/rendering/docs/overview.md) | [premises](../MonoDreams/rendering/docs/premises.md) |
| `rendering-mesh` | [overview](../MonoDreams/rendering-mesh/docs/overview.md) | [premises](../MonoDreams/rendering-mesh/docs/premises.md) |
| `rendering-text` | [overview](../MonoDreams/rendering-text/docs/overview.md) | [premises](../MonoDreams/rendering-text/docs/premises.md) |
| `text-dynamic-reveal` | [overview](../MonoDreams/text-dynamic-reveal/docs/overview.md) | [premises](../MonoDreams/text-dynamic-reveal/docs/premises.md) |
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

Premises follow the format **Why / Breaks / Tests / Depends on**, with
optional `Open questions`, `Aspirational direction`, and `Follow-up debt`
sections at the bottom. See [`CORE_TENETS.md`](CORE_TENETS.md) §"Premises"
for the full convention.

## Contributor docs

| Doc | What's in it |
|---|---|
| [`../README.md`](../README.md) | Project README — for end users (game devs). Quickstart with the CLI, block tree, naming conventions. |
| [`../CONTRIBUTING.md`](../CONTRIBUTING.md) | Contributor setup: prereqs by OS, build commands, adding a new block, manifest validation, test workflow. |
| [`../MonoDreams/BLOCKS.md`](../MonoDreams/BLOCKS.md) | Block manifest schema and authoring guide. |
| [`../.claude/CLAUDE.md`](../.claude/CLAUDE.md) | Coding conventions, block-to-premises mapping, workflow rules. Loaded automatically by Claude Code; humans should also read it. |
| [`../.claude/skills/deep-review/SKILL.md`](../.claude/skills/deep-review/SKILL.md) | Multi-agent review skill. Run `/deep-review` on a PR, branch, or commit. |
