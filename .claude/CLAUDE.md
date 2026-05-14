# About this project
This project is a code-first and ECS-purist 2D game engine built on MonoGame
(rendering) and DefaultEcs (ECS framework). The core concept is plug'n play
components and systems: add a `Transform` and `RigidBody` to an entity, include
`GravitySystem` in your pipeline, and gravity just works.

MonoDreams is still in alpha. The core library (`MonoDreams/`) is the
authoritative source for all engine infrastructure — rendering, camera, cursor,
collision, physics, entity spawning, level loading, and screen management.
`MonoDreams.Examples/` contains game-specdific logic only. Your starting point to
understand how to wire up a game screen is
`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs`.

# Project structure
- `MonoDreams/` — core engine library: components, systems, rendering, camera,
  cursor, collision, physics, entity spawning, level loading, screen management
- `MonoDreams.Examples/` — game-specific logic: screens, input mapping,
  dialogue, movement, entity factories, settings
- `MonoDreams.YarnSpinner/` — YarnSpinner dialogue integration (standalone, not
  referenced by core)
- `MonoDreams.Tests/` — tests
- `Tools/` — Blender exporter plugin for level design

# Key conventions

## Component design
- Components are pure data containers — no logic, no methods beyond simple
  helpers. All behavior lives in systems.
- Before creating a new component, check if an existing one can be extended
  with a field or flag. This project avoids component sprawl.
- `EntityInfo` is string-based in core (`new EntityInfo("Player")`). Game code
  can define its own enum for convenience.
- `Transform` is the single spatial component — there is no separate Position
  component.
- `CameraFollowTarget` lives in `MonoDreams.Component` (not a Camera
  subdirectory) to avoid namespace conflicts with the `Camera` class.
- `DrawComponent` (class) is the unified rendering component — it supports
  Sprite, Text, NinePatch, and Mesh via the `DrawElementType` enum. **Do not
  create new draw/render components.**
- `SpriteInfo` (struct) holds sprite source data (texture, source rect, color,
  layer depth). Both `SpriteInfo` and `DrawComponent` are set on entities that
  render.
- `Visible` is an empty tag struct added/removed by `CullingSystem`. Entities
  need it to be rendered.

## Entity creation
- Factory-based: implement `IEntityFactory` (in `MonoDreams.EntityFactory`),
  register it with `EntitySpawnSystem` (in `MonoDreams.System.EntitySpawn`) by
  string identifier. The system listens for `EntitySpawnRequest` messages and
  dispatches to the right factory.
- Namespace choice: `MonoDreams.EntityFactory` (not `.Entity`) to avoid
  shadowing `DefaultEcs.Entity`.
- Direct creation: `BlenderLevelParserSystem` creates entities from exported
  Blender level data.
- Standard component stack for a renderable entity: `EntityInfo`, `Transform`,
  physics components as needed, `SpriteInfo`, `DrawComponent`, `Visible`.

## Rendering pipeline
- All rendering infrastructure lives in `MonoDreams.Component.Draw` and
  `MonoDreams.System.Draw`.
- Three render targets defined by `RenderTargetID`: Main (game world, camera
  transform applied), UI, HUD.
- Draw pipeline: prep systems (`SpritePrepSystem`, `TextPrepSystem`,
  `MeshPrepSystem`) populate `DrawComponent` from source data →
  `MasterRenderSystem` renders everything.
- `MasterRenderSystem` is game-agnostic and handles all draw types. Do not
  create parallel render systems.
- `CullingSystem` adds/removes the `Visible` component based on camera view
  bounds.

## Collision and physics
- `BoxCollider`, `RigidBody`, `Velocity` components in
  `MonoDreams.Component.Collision` / `MonoDreams.Component.Physics`.
- Transform-based collision detection and resolution in
  `MonoDreams.System.Collision`.
- `GravitySystem` in `MonoDreams.System.Physics`.

## Level loading
- LDtk and Blender parsers in `MonoDreams.System.Level`.
- `LoadLevelRequest` message triggers the pipeline.
- `LevelLoadRequestSystem` → parser systems → `EntitySpawnSystem`.

## Debug infrastructure
- **Logger** — `MonoDreams.State.Logger`, replaces `Console.WriteLine`. Writes
  to `debug/monodreams_*.log` with `[wallclock] [GT gametime] [LEVEL] message`
  format. Call `Logger.Info(msg)`, `.Debug()`, `.Warning()`, `.Error()`.
- **Input replay** — place `debug/input_replay.json` in the build output dir.
  Format: `{ startLevel, description, commands: [{ action, type, time }] }`.
  `startLevel` skips menus and jumps straight to the game screen. Actions match
  `AInputState` names (Up, Down, Left, Right, Jump, Grab, Orb, Exit, Interact).
  Game auto-exits when replay finishes.
- **Screenshots** — `ScreenshotCaptureSystem` saves PNGs every 2s to `debug/`.
  Off by default; enable by setting `"screenshots": true` in `input_replay.json`.
- **Running a test session** — write `input_replay.json`, run
  `dotnet run --project MonoDreams.Examples`, check `debug/` for log +
  screenshots.
- **Headless mode** — `dotnet run --project MonoDreams.Examples -- --headless`
  skips rendering, runs at max speed (no VSync, no fixed timestep), for
  automated testing. The game window is created at 1×1 off-screen.
- **Debug directory override** — set `MONODREAMS_DEBUG_DIR` env var to redirect
  all debug output (logs, replay input, screenshots) to a custom path. Used by
  the test runner for parallel test isolation.

## Testing
- Run tests: `dotnet test MonoDreams.Tests/`
- Integration tests use headless replay + log assertions via `GameTestRunner`.
- `GameTestRunner` spawns the game in headless mode with a temp debug dir,
  writes an `InputReplayPlan`, waits for exit, and provides log assertion
  helpers (`AssertLogContains`, `AssertLogContainsInOrder`, `GetLogLines`).

# Workflow

**Before any non-trivial implementation, read the docs.** The
`docs/` layer (see the Documentation section below) captures engine
invariants that are silently load-bearing — most "surprising" bugs in
this repo come from violating one of them. Skipping this step is the
single most common way an implementation lands that quietly breaks an
engine contract.

1. **Always** read [`docs/CORE_TENETS.md`](../docs/CORE_TENETS.md)
   before any task that adds, removes, or significantly modifies a
   component, system, message, screen, or factory.
2. **For each domain you touch**, read the matching
   `docs/<domain>/premises.md` before editing files in that domain.
   Use the path-to-premises mapping below to pick the right file(s).
3. When you spawn a subagent (Explore, Plan, general-purpose) for
   implementation work in a domain, **pass the relevant CORE_TENETS
   and premises file paths in the subagent's prompt** so it loads the
   invariants too. Subagents do not auto-load CLAUDE.md; if you don't
   pass these paths, the subagent operates without the invariants and
   reproduces the bugs the docs exist to prevent.
4. If your change introduces a new invariant the docs don't yet
   name, propose the premise text as part of the work (see the
   Premises subsection for the format).

## Path-to-premises mapping

Look up which `docs/<domain>/premises.md` to load based on which file
paths your change touches.

| Touched path pattern | Read this premises file |
|---|---|
| `MonoDreams/Component/Transform.cs`, `Component/ChildOf.cs`, `Component/LayoutNode.cs`, `System/HierarchySystem.cs`, `System/TransformCommitSystem.cs`, `System/SizeSystem.cs`, `System/LayoutSystem.cs`, `State/EntityHierarchy.cs` | [`docs/hierarchy-transform/premises.md`](../docs/hierarchy-transform/premises.md) |
| `MonoDreams/Component/Draw/**`, `System/Draw/**`, `Renderer/**`, anything touching `DrawComponent` / `SpriteInfo` / `Visible` / `MasterRenderSystem` / `CullingSystem` / `YSortSystem` | [`docs/rendering/premises.md`](../docs/rendering/premises.md) |
| `MonoDreams/Component/Collision/**`, `System/Collision/**`, `Extensions/Monogame/SATCollision.cs`, `Message/CollisionMessage.cs`, `Message/ICollisionMessage.cs` | [`docs/collision/premises.md`](../docs/collision/premises.md) |
| `MonoDreams/Component/Physics/**`, `System/Physics/**` | [`docs/physics/premises.md`](../docs/physics/premises.md) |
| `MonoDreams/Component/Level/**`, `System/Level/**`, `System/EntitySpawn/**`, `EntityFactory/**`, `Message/EntitySpawnRequest.cs`, `Message/Level/**`, `Tools/blender_level_export.py` | [`docs/level-loading/premises.md`](../docs/level-loading/premises.md) |
| Anything under `MonoDreams.Examples/**` | Load **all five** premises files — Examples exercises every domain. |
| Camera / Cursor / Input / Debug / Screen / Messages (V2 — no premises file yet) | Skip; read `CORE_TENETS.md` only and consider proposing a new premises file as part of the change. |

## Other workflow rules

- After planning but before coding, build the MonoDreams.Examples solution so
  that you know how to build and not question the build process after you
  commit your changes and test the build.
- Eval between using the LevelSelectionScreen as a starting point for
  your own work or creating a new screen.
- When adding new functionality, first check `MonoDreams/` (core) for existing
  components and systems that can be extended. Prefer adding fields to existing
  components over creating new component types.
- This project should behave as a framework, so avoid having multiple ways to
  do the same thing, meaning that you should not create many new components
  and systems when you can just add functionality to existing ones.
- Refactorings are fine since this project is still in alpha. Just be sure to
  align with the user your plan first.

# Building the Project
Before making changes, ensure the project builds successfully.

## Build Commands
```bash
# Build core engine
dotnet build MonoDreams/MonoDreams.csproj

# Build examples (includes MonoDreams)
dotnet build MonoDreams.Examples/MonoDreams.Examples.csproj

# Build YarnSpinner integration (includes MonoDreams)
dotnet build MonoDreams.YarnSpinner/MonoDreams.YarnSpinner.csproj

# Build tests
dotnet build MonoDreams.Tests/MonoDreams.Tests.csproj

# Build everything
dotnet build
```

# Documentation

The repo has a `docs/` layer that captures engine tenets and per-domain
invariants. **Load these as context before non-trivial work** (see the
Workflow section above for the exact triggers and the path-to-premises
mapping).

- [`docs/index.md`](../docs/index.md) — routing index.
- [`docs/CORE_TENETS.md`](../docs/CORE_TENETS.md) — engine-wide
  invariants: framework-not-library, ECS purity & composition,
  hierarchy & transform, rendering, physics & collision, level loading,
  the reference pipeline, debug, and the named refactor backlog. Read
  this first for any non-trivial task.
- [`docs/<domain>/premises.md`](../docs/index.md) — technical
  invariants downstream code silently depends on, one file per
  foundational domain (hierarchy-transform, rendering, collision,
  physics, level-loading).

## Premises

Premises are the smallest unit of "this must hold or things break
silently." Each entry uses the format:

```
## <Short, declarative, present-tense title>

<One paragraph: what is true and must remain true.>

**Why:** <The reason — usually a downstream system or past bug.>
**Breaks:** <What goes wrong if violated.>
**Tests:** <Test that protects this, or `none yet`.>
**Depends on:** <Cross-references to other premises, or —.>
```

**Workflow when modifying a domain.**

1. Read the domain's `premises.md` before changing code there.
2. If the change introduces a new premise the docs don't yet name,
   propose the premise text as part of the PR.
3. If a premise has `Tests: none yet` and the change exercises it,
   add a test in the same PR and update the `Tests:` field.
4. If a premise becomes wrong (refactored away, replaced), update or
   remove it in the same PR — stale premises are worse than missing
   ones.

# Skills

`.claude/skills/` contains repo-specific Claude Code skills.

- [`deep-review`](skills/deep-review/SKILL.md) — multi-agent code
  review through six lenses calibrated for MonoDreams: adjacent-code,
  system-ordering, component-design/framework-fit, cross-domain
  dependency, premises/test-coverage, and ECS-purity. Invoke with
  `/deep-review` on a PR number, URL, branch, commit SHA, or no
  argument (reviews the current branch vs `main`, including
  uncommitted changes). Pass `--eco` for a cheaper run.
