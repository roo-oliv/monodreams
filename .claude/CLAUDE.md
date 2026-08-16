# About this project
This is **MonoDreams**, a code-first and ECS-purist 2D game engine built on
MonoGame (rendering) and DefaultEcs (ECS framework). The engine ships as **14
self-contained source modules** under `MonoDreams/<module>/`, distributed
shadcn-style via the `monodreams` CLI: users own the source they install. The core concept
is plug'n play components and systems: add a `TransformComponent` and
`RigidBodyComponent` to an entity, include `GravitySystem` in your pipeline,
and gravity just works.

MonoDreams is still in alpha. Each module's source is colocated with its
manifest (`MonoDreams/<module>/module.json`) and its docs
(`MonoDreams/<module>/docs/overview.md` + `premises.md`). The
`MonoDreams.Examples` project (a shared `MonoDreams.Examples.Core` library
plus per-platform `.Desktop` / `.Web` heads) demonstrates how to wire up
modules into real game screens — start at
`MonoDreams.Examples.Core/Screens/LoadLevelExampleGameScreen.cs`.

# Project structure
- `MonoDreams/` — engine source organized into 14 modules (`foundation`,
  `rendering`, `rendering-text`, `camera`, `physics`, `collision`,
  `level-loading`, `level-ldtk`, `ui`, `cursor`,
  `dialogue`, `debug`, `level-editor`, `audio`). Each module has
  `module.json`, `docs/`, and its components/systems/messages.
- `MonoDreams.Examples/` — two reference games proving the module
  boundaries: LDtk platformer and infinite runner (plus the committed
  native scene `Blender_Level`, which boots the native pipeline).
  Game-specific logic only (screens, input mapping, entity factories,
  settings, `ButtonInteractionSystem`).
- `MonoDreams.Cli/` — the `monodreams` global tool (init / add / list).
- `MonoDreams.Tests/` — unit + integration tests via `GameTestRunner`.

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
- `CameraFollowTargetComponent` lives in the `camera` module; the `Camera`
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
- Direct creation: `LDtkEntityParserSystem` creates entities from
  exported level data.
- Standard component stack for a renderable entity: `EntityInfoComponent`,
  `TransformComponent`, physics components as needed, `SpriteInfoComponent`,
  `DrawComponent`, `VisibleComponent`.

## Rendering pipeline
- All rendering infrastructure lives in the `rendering` module
  (`MonoDreams/rendering/`).
- Render targets defined by `RenderTargetID`: Main (game world, camera
  transform applied), UI, HUD, and Scroll (screen-space overlay).
- Draw pipeline (in order): `CullingSystem` adds/removes `VisibleComponent`
  from camera view bounds → prep systems (`SpritePrepSystem`,
  `TextPrepSystem`, `MeshPrepSystem`) populate `DrawComponent` for visible
  entities → `YSortSystem` orders them → `MasterRenderSystem` renders
  everything. The prep and sort systems query `[With(VisibleComponent)]`, so
  a culled entity is also un-prepped that frame.
- `MasterRenderSystem` is game-agnostic and handles all draw types. Do not
  create parallel render systems.

## Collision and physics
- `BoxColliderComponent`, `ConvexColliderComponent`, `ColliderTagComponent`,
  `RigidBodyComponent`, `VelocityComponent` — physics and collision are
  separate modules (`physics`, `collision`); the `collision` module soft-couples
  to `physics` for impulse-style resolution.
- Transform-based collision detection and resolution in
  `MonoDreams/collision/System/`.
- `GravitySystem` and `VelocitySystem` in `MonoDreams/physics/System/`.

## Level loading
- LDtk loader + parsers in `level-ldtk`. Shared, **format-agnostic** spawn
  plumbing in `level-loading` — it carries no LDtk type; the arrow is
  level-ldtk → level-loading, never the reverse.
- `LoadLevelRequest` message triggers the pipeline.
- LDtk parsers are component-driven (subscribe to `LDtkLevelDataComponent`
  added — the LDtk module's own level singleton holding the `LDtkLevel`;
  `CurrentLevelComponent` is just a `string LevelIdentifier` marker). At game
  boot the pipeline is native-only (`LoadLevelRequest` → native `.mdscene` via
  the native reader, else fail loud — `new LevelLoadRequestSystem(world,
  probe)`); the LDtk module is import-only machinery, and the import op
  composes `LDtkLevelLoadSystem` in place of `LevelLoadRequestSystem`.
- Layer-derived spawn data rides `EntitySpawnRequest.CustomFields` under
  `LDtkSpawnFields`' `ldtk:` keys (`request.Layer` no longer exists).

## Debug infrastructure
- **Logger** — `MonoDreams.State.Logger` (`foundation` module). Replaces
  `Console.WriteLine`. Writes to `debug/monodreams_*.log` with `[wallclock]
  [GT gametime] [LEVEL] message` format. Call `Logger.Initialize(dir)` once
  at startup, then `Logger.Info(msg)`, `.Debug()`, `.Warning()`, `.Error()`.
- **Input replay** — place `debug/input_replay.json` in the build output
  dir. Format: `{ startLevel, description, commands: [{ action, type, time }] }`.
  `startLevel` skips menus and jumps straight to the game screen. Actions
  match `AInputState` names (Up, Down, Left, Right, Jump, Grab, Orb, Exit,
  Interact). Game auto-exits when replay finishes.
- **Pointer replay** — `debug/pointer_replay.json` scripts the MOUSE (the
  input replay only speaks named actions): `{ description, tailFrames,
  commands: [{ kind: move|click|wheel|type|waitUntil|label, ... }] }`.
  Coordinates are authoring space (virtual resolution); timing is frames.
  `waitUntil` gates a stage on `entity` / `log` / `frames` with a
  `timeoutFrames` (a `log` wait consumes the line it matched, so two
  identical waits gate on two lines). Wired by
  `PointerReplaySystem.TryLoad(debugDir, world, camera, viewportManager,
  requestExit)` right after `CursorInputSystem`, with `SkipHardwareRead` +
  `SkipDerivation` set — the viewport manager maps the authored point into
  `ScreenPosition`'s backbuffer-pixel space, so editor chrome in the inset
  margins is deliberately not addressable from a pointer plan. Reference wiring:
  `MonoDreams.Examples.Core/Screens/LevelSelectionScreen.cs`. From a test:
  `GameTestRunner.RunAsync(plan, pointerPlan: ...)`.
- **Frame capture** — `ScreenshotCaptureSystem` writes PNGs (verification
  shots) or raw RGBA frames (`CaptureFormat.Raw` — full-rate 60 fps takes)
  to `debug/`. Off by default; enable via `"screenshots": true` in
  `input_replay.json` (PNG interval) or the env contract owned by
  `ScreenshotCaptureSystem.FromEnvironment`: `MONODREAMS_SCREENSHOT=1|png|raw`,
  `MONODREAMS_SCREENSHOT_INTERVAL`, `MONODREAMS_SCREENSHOT_MAX_FRAMES` (raw
  is ~3.5 MiB/frame — always cap it). See `MonoDreams/debug/docs/overview.md`.
- **Running a test session** — write `input_replay.json`, run
  `dotnet run --project MonoDreams.Examples.Desktop`, check `debug/` for log +
  screenshots. (Examples is now a shared `.Core` lib + per-platform heads;
  the desktop head is `MonoDreams.Examples.Desktop`.)
- **Headless mode (Examples — logic only)** — `dotnet run --project
  MonoDreams.Examples.Desktop -- --headless` skips rendering (its `Draw`
  early-returns), runs at max speed (no VSync, no fixed timestep), for
  logic/replay testing. The game window is created at 1×1 off-screen — it
  renders nothing, so it cannot observe visual or render-path behaviour.
- **Headless mode (Demos — observe & self-verify)** — `dotnet run
  --project MonoDreams.Demos -- --headless --screen <camera|physics|dialogue>
  --frames <N> --exit` renders every frame on a hidden full-res backbuffer,
  dumps non-blank PNGs to `MONODREAMS_DEBUG_DIR`, logs periodic live-heap
  samples, and self-terminates after `<N>` frames (exit 0). This is the
  path for verifying your own work without a human (issue #28). Optional:
  `--capture-every K`, `--sample-every M`. From tests:
  `GameTestRunner.RunDemosAsync(...)` plus `AssertScreenshotNonBlank` /
  `AssertHeapFlat` (see `HeadlessDemoTests`).
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
2. **For each module you touch**, read its
   `MonoDreams/<module>/docs/premises.md` (load-bearing invariants) and
   optionally `docs/overview.md` (purpose + wiring tour). The
   module-to-premises mapping below tells you which.
3. When you spawn a subagent (Explore, Plan, general-purpose) for
   implementation work in a module, **pass `docs/CORE_TENETS.md` and the
   relevant per-module `docs/premises.md` paths in the subagent's prompt**
   so it loads the invariants too. Subagents do not auto-load CLAUDE.md;
   if you don't pass these paths, the subagent operates without the
   invariants and reproduces the bugs the docs exist to prevent.
4. If your change introduces a new invariant the docs don't yet name,
   propose the premise text as part of the work (see the Premises
   subsection below for the format).

## Module-to-premises mapping

The convention is one-to-one: for any file under `MonoDreams/<module>/`,
read `MonoDreams/<module>/docs/premises.md` (and `docs/overview.md` for
the broader picture).

| Module | What lives there |
|---|---|
| [`foundation`](../MonoDreams/foundation/docs/premises.md) | `TransformComponent`, `ChildOfComponent`, hierarchy, `Logger`, input/replay, `ScreenController` |
| [`rendering`](../MonoDreams/rendering/docs/premises.md) | `DrawComponent`, `SpriteInfoComponent`, `VisibleComponent`, `MasterRenderSystem`, `CullingSystem`, `YSortSystem`, the `Camera` class, plus `IMeshGenerator` / `MeshData` / `MeshPrepSystem` (procedural shapes) |
| [`rendering-text`](../MonoDreams/rendering-text/docs/premises.md) | `DynamicTextComponent`, `TextPrepSystem`, `TextUpdateSystem` (BitmapFont) |
| [`camera`](../MonoDreams/camera/docs/premises.md) | `CameraFollowSystem`, `CameraFollowTargetComponent` (the `Camera` class itself ships in `rendering`) |
| [`physics`](../MonoDreams/physics/docs/premises.md) | `RigidBodyComponent`, `VelocityComponent`, `GravitySystem`, `VelocitySystem` |
| [`collision`](../MonoDreams/collision/docs/premises.md) | `BoxColliderComponent`, `ConvexColliderComponent`, `ColliderTagComponent`, detection + resolution systems, `CollisionMessage` |
| [`level-loading`](../MonoDreams/level-loading/docs/premises.md) | `LoadLevelRequest`, `EntitySpawnRequest`, `IEntityFactory`, `EntitySpawnSystem`, `LevelLoadRequestSystem` |
| [`level-ldtk`](../MonoDreams/level-ldtk/docs/premises.md) | `LDtkTileParserSystem`, `LDtkEntityParserSystem` |
| [`ui`](../MonoDreams/ui/docs/premises.md) | `LayoutNodeComponent`, `LayoutSlotComponent`, `AutoLayoutBuilder`, `IntrinsicSizingSystem`, `AutoLayoutSystem`, button visuals |
| [`cursor`](../MonoDreams/cursor/docs/premises.md) | `CursorControllerComponent`, `CursorInputComponent`, `CursorTexturesComponent`, cursor pipeline systems |
| [`dialogue`](../MonoDreams/dialogue/docs/premises.md) | `DialogueRunner`, `DialogueStateComponent`, `DialogueSystem`, YarnSpinner integration |
| [`debug`](../MonoDreams/debug/docs/premises.md) | `ColliderDebugSystem`, `SpriteDebugSystem`, `ScreenshotCaptureSystem`, `SystemProfiler`, `PointerReplaySystem` (scripted mouse) |
| [`level-editor`](../MonoDreams/level-editor/docs/premises.md) | in-game editor `Edit` run mode over the real pipeline (scaffold; the run-state model `RunMode`/`EditTimeBehavior`/`GatedSystem` lives in `foundation`) |
| [`audio`](../MonoDreams/audio/docs/premises.md) | `AudioSourceComponent`, `PlaySoundRequest`, `AudioSystem`, `IAudioPlayer`/`ContentAudioPlayer` seam (one-shot SFX, loops, interruptible sources) |

For files under `MonoDreams.Examples/`, identify which module(s) the
change exercises and load the relevant per-module premises — Examples
exercises every module, so pick what's load-bearing for your change
rather than loading all 14.

## Other workflow rules

- After planning but before coding, build `MonoDreams.sln` (core-first; the
  desktop heads + tests) so you know the build is working before you commit
  changes. The web head builds separately with `-p:MonoDreamsPlatform=web`.
- Eval between using `LevelSelectionScreen` (the lightweight entry point
  with UI + cursor) or `LoadLevelExampleGameScreen` (the full physics +
  level loading stack) as a starting point for your own work, or creating
  a new screen.
- When adding new functionality, first check `MonoDreams/<module>/` for
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
# Build core engine (compiles all 14 modules together).
# ALWAYS build this BEFORE Examples/Demos: the MGCB content step references
# MonoDreams.dll by absolute path (not as an MSBuild dependency), so the core
# dll must already exist or content build fails with
# "Failed to create importer 'YarnSpinnerImporter'".
dotnet build MonoDreams/MonoDreams.csproj

# Build the desktop reference game (the LDtk platformer).
# Examples is now a shared lib + per-platform heads (see "Platform targeting").
dotnet build MonoDreams.Examples.Desktop/MonoDreams.Examples.Desktop.csproj

# Build the CLI
dotnet build MonoDreams.Cli/MonoDreams.Cli.csproj

# Build tests
dotnet build MonoDreams.Tests/MonoDreams.Tests.csproj

# Build everything (desktop). The web head is deliberately excluded from the
# default .sln build — build it explicitly (see below).
dotnet build MonoDreams.sln
```

## Platform targeting (desktop + web via KNI/BlazorGL)

A game is a **shared `.Core` library + per-platform head projects**, so the
same engine + game source ships for desktop (MonoGame `DesktopGL`) and web
(KNI/BlazorGL `nkast.Xna.Framework.*`). The backend is chosen by the
`$(MonoDreamsPlatform)` MSBuild property (`desktop` default, `web`), defined in
`Directory.Build.props` and flowed from a head into `.Core` — never baked into
MonoDreams modules. `MonoDreams.Examples` is laid out as `.Examples.Core`
(shared) + `.Examples.Desktop` (head) + `.Examples.Web` (Blazor WASM head).

```bash
# Desktop tests + regression (GameTestRunner / HeadlessDemoTests live here)
dotnet test MonoDreams.Tests/
dotnet test MonoDreams.Cli.Tests/        # CLI unit tests (separate project)

# Build the web head (KNI/BlazorGL, WASM). -p is GLOBAL (flows to Core);
# requires the wasm-tools workload installed.
dotnet build MonoDreams.Examples.Web/MonoDreams.Examples.Web.csproj -p:MonoDreamsPlatform=web

# Scaffold a new game via the CLI for one or more platforms
dotnet run --project MonoDreams.Cli -- init MyGame --platform desktop|web|multi
```

A multi-platform `.sln` builds the desktop heads by default and **excludes the
web head from the default build** (MSBuild `AdditionalProperties` does not
propagate through `restore`, so a head-driven web build of `.Core` must be
invoked explicitly with `-p:MonoDreamsPlatform=web`). The web content build,
the native-lib shim required by KNI's MGCB on macOS/Linux, and the known
Reach-profile render limit are documented in
[`docs/web-targeting.md`](../docs/web-targeting.md).

# Documentation

The repo's docs layer captures engine tenets and per-module invariants.
**Load these as context before non-trivial work** (see the Workflow
section above for triggers and the module-to-premises mapping).

- [`docs/CORE_TENETS.md`](../docs/CORE_TENETS.md) — engine-wide
  invariants: framework-not-library, ECS purity & composition,
  hierarchy & transform, rendering, physics & collision, level loading,
  the reference pipeline, debug, and the named refactor backlog. Read
  this first for any non-trivial task.
- [`docs/index.md`](../docs/index.md) — routing index pointing to the
  per-module docs.
- `MonoDreams/<module>/docs/overview.md` — per-module tour: purpose,
  components/systems/messages, wiring, extension points.
- `MonoDreams/<module>/docs/premises.md` — per-module invariants in
  Why/Breaks/Tests/Depends on format.
- [`MonoDreams/MODULES.md`](../MonoDreams/MODULES.md) — module manifest
  schema and authoring guide.
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — contributor setup, build,
  test workflow, adding new modules.

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
**Depends on:** <Cross-references to other modules' premises (use the form
`<module> — "Premise title"`), or —.>
```

**Workflow when modifying a module.**

1. Read the module's `docs/premises.md` before changing code there.
2. If the change introduces a new premise the docs don't yet name,
   propose the premise text as part of the PR.
3. If a premise has `Tests: none yet` and the change exercises it,
   add a test in the same PR and update the `Tests:` field.
4. If a premise becomes wrong (refactored away, replaced), update or
   remove it in the same PR — stale premises are worse than missing
   ones.

# Skills

`.claude/skills/` contains a portable, config-driven engineering
pipeline (vendored from [`roo-oliv/skills`](https://github.com/roo-oliv/skills),
re-vendor with that repo's `scripts/install.sh`). Each skill reads
**`docs/agents/skills-config.md`** for everything repo-specific —
stack, verify command, docs layout, domains, sensitive domains
(`foundation`, `platform`), the per-module flow lenses, and commit/PR
conventions. Edit that file to retune them; nothing stack-specific is
hardcoded in a skill.

**Pipeline:**
- [`refine`](skills/refine/SKILL.md) — turn a raw request (text, plan file,
  or GitHub/Jira/Slack link) into an approved plan with a verifiable
  Contract block. Replaces interactive plan mode.
- [`deep-plan`](skills/deep-plan/SKILL.md) — fill and adversarially refute
  a plan's contract against the live codebase before code exists. Heavy
  path engages for changes touching a sensitive domain.
- [`implement`](skills/implement/SKILL.md) — drive an approved plan to an
  open PR: wave-based, fresh agent per wave + a persistent ledger, then
  chains `review-fix-loop`.
- [`review-fix-loop`](skills/review-fix-loop/SKILL.md) — review → fix loop
  over an open PR until exhaustion; posts a consolidated review.
- [`deep-review`](skills/deep-review/SKILL.md) — multi-agent review of a
  PR/branch/commit/local diff through the universal lens set plus one
  dedicated lens per module (flow) the change touches. Invoke `/deep-review`
  with a PR number, URL, branch, commit SHA, or no argument (current
  changes vs `main`). Append `cheaper` (aliases: `cheap`, `simple`, `eco`,
  `economy`) for tiered model routing.

**Checks:**
- [`verify`](skills/verify/SKILL.md) — run the configured verify command
  (`config › Verify`) with a fix loop until green.
- [`verify-plan`](skills/verify-plan/SKILL.md) — reconcile an
  implementation against its plan (Missing / Diverged / Unplanned).

**Setup (run once per repo):**
- [`setup`](skills/setup/SKILL.md) — write `docs/agents/skills-config.md`
  (already done for this repo).
- [`bootstrap`](skills/bootstrap/SKILL.md) — scaffold/revise the docs the
  skills consume: `CORE_TENETS.md`, per-module `premises.md`, and the
  per-module flow docs under `docs/flows/`.
