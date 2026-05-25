# About this project
This is **MonoDreams**, a code-first and ECS-purist 2D game engine built on
MonoGame (rendering) and DefaultEcs (ECS framework). The engine ships as **15
self-contained blocks** under `MonoDreams/<block>/`, distributed shadcn-style
via the `monodreams` CLI: users own the source they install. The core concept
is plug'n play components and systems: add a `TransformComponent` and
`RigidBodyComponent` to an entity, include `GravitySystem` in your pipeline,
and gravity just works.

MonoDreams is still in alpha. Each block's source is colocated with its
manifest (`MonoDreams/<block>/block.json`) and its docs
(`MonoDreams/<block>/docs/overview.md` + `premises.md`). The
`MonoDreams.Examples/` project demonstrates how to wire up blocks into
real game screens — start at
`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs`.

# Project structure
- `MonoDreams/` — engine source organized into 15 blocks (`foundation`,
  `rendering`, `rendering-mesh`, `rendering-text`, `text-dynamic-reveal`,
  `camera`, `physics`, `collision`, `level-loading`, `level-ldtk`,
  `level-blender`, `ui`, `cursor`, `dialogue`, `debug`). Each block has
  `block.json`, `docs/`, and its components/systems/messages.
- `MonoDreams.Examples/` — three reference games proving the block
  boundaries: LDtk platformer, Blender platformer, infinite runner.
  Game-specific logic only (screens, input mapping, entity factories,
  settings, `ButtonInteractionSystem`).
- `MonoDreams.Cli/` — the `monodreams` global tool (init / add / list).
- `MonoDreams.Tests/` — unit + integration tests via `GameTestRunner`.
- `Tools/` — Blender exporter plugin (shipped by the `level-blender`
  block).

# Key conventions

## Component design
- Components are pure data containers — no logic, no methods beyond simple
  helpers. All behavior lives in systems.
- Components end in `Component` (e.g. `TransformComponent`,
  `VelocityComponent`, `DialogueStateComponent`). Systems end in `System`.
  Messages use no suffix (e.g. `CollisionMessage`, `LoadLevelRequest`).
- Before creating a new component, check if an existing one can be extended
  with a field or flag. This project avoids component sprawl.
- `EntityInfoComponent` is string-based in core (`new EntityInfoComponent("Player")`).
  Game code can define its own enum for convenience.
- `TransformComponent` is the single spatial component — there is no
  separate Position component.
- `CameraFollowTargetComponent` lives in the `camera` block; the `Camera`
  class itself lives in `rendering` (it's a hard dep of the draw stack).
- `DrawComponent` (class) is the unified rendering component — it supports
  Sprite, Text, NinePatch, and Mesh via the `DrawElementType` enum. **Do not
  create new draw/render components.**
- `SpriteInfoComponent` (struct) holds sprite source data (texture, source
  rect, color, layer depth). Both `SpriteInfoComponent` and `DrawComponent`
  are set on entities that render sprites.
- `VisibleComponent` is an empty tag struct added/removed by `CullingSystem`.
  Entities need it to be rendered to the Main target.

## Entity creation
- Factory-based: implement `IEntityFactory` (namespace `MonoDreams.EntityFactory`
  to avoid shadowing `DefaultEcs.Entity`), register it with `EntitySpawnSystem`
  by string identifier. The system listens for `EntitySpawnRequest` messages
  and dispatches to the right factory.
- Direct creation: `BlenderLevelParserSystem` and `LDtkEntityParserSystem`
  create entities from exported level data.
- Standard component stack for a renderable entity: `EntityInfoComponent`,
  `TransformComponent`, physics components as needed, `SpriteInfoComponent`,
  `DrawComponent`, `VisibleComponent`.

## Rendering pipeline
- All rendering infrastructure lives in the `rendering` block
  (`MonoDreams/rendering/`).
- Three render targets defined by `RenderTargetID`: Main (game world,
  camera transform applied), UI, HUD.
- Draw pipeline: prep systems (`SpritePrepSystem`, `TextPrepSystem`,
  `MeshPrepSystem`) populate `DrawComponent` from source data →
  `MasterRenderSystem` renders everything.
- `MasterRenderSystem` is game-agnostic and handles all draw types. Do not
  create parallel render systems.
- `CullingSystem` adds/removes the `VisibleComponent` based on camera view
  bounds.

## Collision and physics
- `BoxColliderComponent`, `ConvexColliderComponent`, `ColliderTagComponent`,
  `RigidBodyComponent`, `VelocityComponent` — physics and collision are
  separate blocks (`physics`, `collision`); the `collision` block soft-couples
  to `physics` for impulse-style resolution.
- Transform-based collision detection and resolution in
  `MonoDreams/collision/System/`.
- `GravitySystem` and `VelocitySystem` in `MonoDreams/physics/System/`.

## Level loading
- LDtk parser in `level-ldtk`, Blender parser in `level-blender`. Shared
  spawn plumbing in `level-loading`.
- `LoadLevelRequest` message triggers the pipeline.
- LDtk parsers are component-driven (subscribe to `CurrentLevelComponent`
  added). The Blender parser is message-driven (subscribes to
  `LoadLevelRequest` directly). This asymmetry is documented in both
  blocks' premises — a future cleanup will unify them.

## Debug infrastructure
- **Logger** — `MonoDreams.State.Logger` (`foundation` block). Replaces
  `Console.WriteLine`. Writes to `debug/monodreams_*.log` with `[wallclock]
  [GT gametime] [LEVEL] message` format. Call `Logger.Initialize(dir)` once
  at startup, then `Logger.Info(msg)`, `.Debug()`, `.Warning()`, `.Error()`.
- **Input replay** — place `debug/input_replay.json` in the build output
  dir. Format: `{ startLevel, description, commands: [{ action, type, time }] }`.
  `startLevel` skips menus and jumps straight to the game screen. Actions
  match `AInputState` names (Up, Down, Left, Right, Jump, Grab, Orb, Exit,
  Interact). Game auto-exits when replay finishes.
- **Screenshots** — `ScreenshotCaptureSystem` saves PNGs every 2s to
  `debug/`. Off by default; enable by setting `"screenshots": true` in
  `input_replay.json`.
- **Running a test session** — write `input_replay.json`, run
  `dotnet run --project MonoDreams.Examples`, check `debug/` for log +
  screenshots.
- **Headless mode** — `dotnet run --project MonoDreams.Examples -- --headless`
  skips rendering, runs at max speed (no VSync, no fixed timestep), for
  automated testing. The game window is created at 1×1 off-screen.
- **Debug directory override** — set `MONODREAMS_DEBUG_DIR` env var to
  redirect all debug output (logs, replay input, screenshots) to a custom
  path. Used by the test runner for parallel test isolation.

## Testing
- Run tests: `dotnet test MonoDreams.Tests/`
- Integration tests use headless replay + log assertions via `GameTestRunner`.
- `GameTestRunner` spawns the game in headless mode with a temp debug dir,
  writes an `InputReplayPlan`, waits for exit, and provides log assertion
  helpers (`AssertLogContains`, `AssertLogContainsInOrder`, `GetLogLines`).

# Workflow

**Before any non-trivial implementation, read the docs.** The repo's docs
layer captures engine invariants that are silently load-bearing — most
"surprising" bugs come from violating one of them. Skipping this step is
the single most common way an implementation lands that quietly breaks an
engine contract.

1. **Always** read [`docs/CORE_TENETS.md`](../docs/CORE_TENETS.md)
   before any task that adds, removes, or significantly modifies a
   component, system, message, screen, or factory.
2. **For each block you touch**, read its
   `MonoDreams/<block>/docs/premises.md` (load-bearing invariants) and
   optionally `docs/overview.md` (purpose + wiring tour). The
   block-to-premises mapping below tells you which.
3. When you spawn a subagent (Explore, Plan, general-purpose) for
   implementation work in a block, **pass `docs/CORE_TENETS.md` and the
   relevant per-block `docs/premises.md` paths in the subagent's prompt**
   so it loads the invariants too. Subagents do not auto-load CLAUDE.md;
   if you don't pass these paths, the subagent operates without the
   invariants and reproduces the bugs the docs exist to prevent.
4. If your change introduces a new invariant the docs don't yet name,
   propose the premise text as part of the work (see the Premises
   subsection below for the format).

## Block-to-premises mapping

The convention is one-to-one: for any file under `MonoDreams/<block>/`,
read `MonoDreams/<block>/docs/premises.md` (and `docs/overview.md` for
the broader picture).

| Block | What lives there |
|---|---|
| [`foundation`](../MonoDreams/foundation/docs/premises.md) | `TransformComponent`, `ChildOfComponent`, hierarchy, `Logger`, input/replay, `ScreenController` |
| [`rendering`](../MonoDreams/rendering/docs/premises.md) | `DrawComponent`, `SpriteInfoComponent`, `VisibleComponent`, `MasterRenderSystem`, `CullingSystem`, `YSortSystem`, the `Camera` class |
| [`rendering-mesh`](../MonoDreams/rendering-mesh/docs/premises.md) | `IMeshGenerator`, `MeshPrepSystem`, procedural shapes |
| [`rendering-text`](../MonoDreams/rendering-text/docs/premises.md) | `DynamicTextComponent`, `TextPrepSystem`, `TextUpdateSystem` (BitmapFont) |
| [`text-dynamic-reveal`](../MonoDreams/text-dynamic-reveal/docs/premises.md) | Placeholder block — reserved for the future static/dynamic reveal split |
| [`camera`](../MonoDreams/camera/docs/premises.md) | `CameraFollowSystem`, `CameraFollowTargetComponent` (the `Camera` class itself ships in `rendering`) |
| [`physics`](../MonoDreams/physics/docs/premises.md) | `RigidBodyComponent`, `VelocityComponent`, `GravitySystem`, `VelocitySystem` |
| [`collision`](../MonoDreams/collision/docs/premises.md) | `BoxColliderComponent`, `ConvexColliderComponent`, `ColliderTagComponent`, detection + resolution systems, `CollisionMessage` |
| [`level-loading`](../MonoDreams/level-loading/docs/premises.md) | `LoadLevelRequest`, `EntitySpawnRequest`, `IEntityFactory`, `EntitySpawnSystem`, `LevelLoadRequestSystem` |
| [`level-ldtk`](../MonoDreams/level-ldtk/docs/premises.md) | `LDtkTileParserSystem`, `LDtkEntityParserSystem` |
| [`level-blender`](../MonoDreams/level-blender/docs/premises.md) | `BlenderLevelParserSystem`, `BlenderLevelData`, `Tools/blender_level_export.py` |
| [`ui`](../MonoDreams/ui/docs/premises.md) | `LayoutNodeComponent`, `LayoutSlotComponent`, `AutoLayoutBuilder`, `IntrinsicSizingSystem`, `AutoLayoutSystem`, button visuals |
| [`cursor`](../MonoDreams/cursor/docs/premises.md) | `CursorControllerComponent`, `CursorInputComponent`, `CursorTexturesComponent`, cursor pipeline systems |
| [`dialogue`](../MonoDreams/dialogue/docs/premises.md) | `DialogueRunner`, `DialogueStateComponent`, `DialogueSystem`, YarnSpinner integration |
| [`debug`](../MonoDreams/debug/docs/premises.md) | `ColliderDebugSystem`, `SpriteDebugSystem`, `ScreenshotCaptureSystem` |

For files under `MonoDreams.Examples/`, identify which block(s) the
change exercises and load the relevant per-block premises — Examples
exercises every block, so pick what's load-bearing for your change
rather than loading all 15.

## Other workflow rules

- After planning but before coding, build the MonoDreams.Examples solution
  so you know the build is working before you commit changes.
- Eval between using `LevelSelectionScreen` (the lightweight entry point
  with UI + cursor) or `LoadLevelExampleGameScreen` (the full physics +
  level loading stack) as a starting point for your own work, or creating
  a new screen.
- When adding new functionality, first check `MonoDreams/<block>/` for
  existing components and systems that can be extended. Prefer adding
  fields to existing components over creating new component types.
- This project behaves as a framework, so avoid having multiple ways to
  do the same thing — don't create many new components and systems when
  you can extend existing ones.
- Refactorings are fine since this project is still in alpha. Just align
  with the user on your plan first.

# Building the Project
Before making changes, ensure the project builds successfully.

## Build Commands
```bash
# Build core engine (compiles all 15 blocks together)
dotnet build MonoDreams/MonoDreams.csproj

# Build examples (includes MonoDreams)
dotnet build MonoDreams.Examples/MonoDreams.Examples.csproj

# Build the CLI
dotnet build MonoDreams.Cli/MonoDreams.Cli.csproj

# Build tests
dotnet build MonoDreams.Tests/MonoDreams.Tests.csproj

# Build everything
dotnet build
```

# Documentation

The repo's docs layer captures engine tenets and per-block invariants.
**Load these as context before non-trivial work** (see the Workflow
section above for triggers and the block-to-premises mapping).

- [`docs/CORE_TENETS.md`](../docs/CORE_TENETS.md) — engine-wide
  invariants: framework-not-library, ECS purity & composition,
  hierarchy & transform, rendering, physics & collision, level loading,
  the reference pipeline, debug, and the named refactor backlog. Read
  this first for any non-trivial task.
- [`docs/index.md`](../docs/index.md) — routing index pointing to the
  per-block docs.
- `MonoDreams/<block>/docs/overview.md` — per-block tour: purpose,
  components/systems/messages, wiring, extension points.
- `MonoDreams/<block>/docs/premises.md` — per-block invariants in
  Why/Breaks/Tests/Depends on format.
- [`MonoDreams/BLOCKS.md`](../MonoDreams/BLOCKS.md) — block manifest
  schema and authoring guide.
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — contributor setup, build,
  test workflow, adding new blocks.

## Premises

Premises are the smallest unit of "this must hold or things break
silently." Each entry uses the format:

```
## <Short, declarative, present-tense title>

<One paragraph: what is true and must remain true. Write it so it stands
alone — a third-party reader who has only this file in front of them
should be able to understand.>

**Why:** <The reason — usually a downstream system or past bug.>
**Breaks:** <What goes wrong if violated.>
**Tests:** <Test that protects this, or `none yet`.>
**Depends on:** <Cross-references to other blocks' premises (use the form
`<block> — "Premise title"`), or —.>
```

**Workflow when modifying a block.**

1. Read the block's `docs/premises.md` before changing code there.
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
