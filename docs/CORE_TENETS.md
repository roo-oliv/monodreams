# MonoDreams — Core Tenets

> The invariants and design principles that hold across the engine.
> Read this before working on MonoDreams — most of what looks surprising in
> the code is consistent with one of these tenets. AI coding agents should
> load this file as context for any non-trivial task in the repo.

## 1. Framework, not library

MonoDreams is to game devs what Spring is to web devs: the framework owns
the heavy lifting (rendering, camera, physics, level loading, spawning),
the developer owns the gameplay code, and the price of full control is
embracing ECS. The competitive position is between **bare MonoGame** (too
low-level — you write the rendering loop yourself) and **Nez or similar**
(too loose — you give up control to get convenience).

**Two users, equal weight.**
- Solo devs and small indie teams who *like writing code* and want plug'n
  play primitives.
- AI coding agents (Claude Code and similar) operating on the codebase.
  Docs in this repo — CORE_TENETS, premises files, CLAUDE.md — are
  first-class context for that user, not human-only supplementary material.

**Key rule.** When choosing between a more specialized API and a more
general one, pick the general one. The same primitive should serve more
than one feature; if it can't, it's probably a game-shaped abstraction
hiding inside the framework.

## 2. ECS purity & composition

The architecture is composition all the way down:

- **Components** are pure data containers. No logic, no methods beyond
  trivial property getters. If you reach for a method on a component, the
  logic belongs in a system.
- **Systems** are pure functions over components. A system declares the
  components it operates on and acts on every entity that matches. It
  makes **no assumption** about which other systems ran before or will
  run after.
- **Entities** are IDs that group components. They carry no state of
  their own.
- **The pipeline assembler — the screen — owns ordering.** The screen
  registers systems in the order they should run for that screen's
  intended behavior. A system that quietly depends on another system
  having run is a framework-level bug; the dependency belongs in the
  pipeline assembly, not inside the system.

**Atomic unit of composition.** A new feature usually lands as one or
more new systems, sometimes paired with one new component. Components
are added only when there is genuinely new data to model. Net: the engine
should always have more systems than components.

**Discover before creating.** Before adding a component or system,
search what's already there. Prefer extending an existing component to
creating a new one (CLAUDE.md says the same — it is repeated here because
this is the most common review finding). The failure mode is a plethora
of micro-components, each solving one feature's narrow pain, none
reusable.

**Key invariant.** Public contracts — messages, components, public
systems — must be backed by a test or example that exercises them.
Contracts without a maintained use site become indistinguishable from
dead code, and the framework cannot signal "this is part of the API" by
name alone.

**Key rule (the #1 review lens).** When a PR adds a new component or
system, ask: *does this evolve well as more features land, or did we
just solve today's pain?* A hyper-specialized addition that doesn't
generalize is the framework's primary failure mode. If a generalization
isn't obvious, propose extending an existing primitive instead.

### Aspirational direction

- **Declarative system dependencies.** Today, ordering is implicit in
  the screen's registration order. A future API would let a system
  declare "I expect X to have run this frame" and let the framework
  enforce or warn at registration time.
- **Modular packs.** Today the engine is one bundle. The intended
  end-state is Spring-style modules (`MonoDreams.Rendering`,
  `MonoDreams.Physics`, `MonoDreams.Dialogue`, …) that can be adopted
  independently. The current `MonoDreams.YarnSpinner/` is the
  closest prototype.
- **Test or example per public contract.** New messages, components, and
  systems should ship with at least one usage that proves the contract
  works and signals that it's maintained.

## 3. Hierarchy & Transform

`Transform` is the engine's spatial component today. It holds position,
rotation, scale, origin, and an optional parent link; it caches a world
matrix and exposes `WorldPosition` / `WorldRotation` / `WorldScale`
properties that walk the parent chain.

**Key invariant — don't mix two Transform-shaped components in one
project.** A developer is free to write a `MyTransform` with different
trade-offs; what breaks the framework is **mixing** `Transform` and
`MyTransform` in the same world. Systems should be agnostic enough to
operate on either one (see §2 — systems are functions), but the bundled
collision, rendering, camera, and culling systems read `Transform`
specifically. Using a custom transform means re-implementing those
consumers too.

**Two parent links, intentionally.** `Transform.Parent` is the matrix
link (it determines how `WorldMatrix` cascades); `ChildOf` is a
structural marker (it determines lifecycle and cascading disposal). The
split exists to support complex hierarchical UI such as a dialogue
panel — a single visual whose banner, avatars, text and waiting-indicator
move and dispose together but may not all share matrix scaling.
`HierarchySystem` keeps the two in sync. **This split is on the refactor
backlog** (§10); read both fields when reasoning about hierarchy until it
is consolidated.

**Dirty propagation.** Mutating a parent's transform marks the entire
descendant chain dirty; `WorldMatrix` is a cached property whose getter
re-walks the chain when needed. Systems that read `WorldPosition` get
the parent-corrected value, but only if `HierarchySystem` has run in the
pipeline ahead of them — that's the pipeline assembler's job to arrange.

**`Transform.Delta` and `HasMoved`.** These reflect the change between
two committed positions. They are meaningful only after
`TransformCommitSystem` has run for the *previous* frame. Today no API
prevents reading stale `Delta` mid-frame; this is on the backlog as
"`Transform` should expose interaction methods that guarantee `Delta`
consistency" (§10). For now, downstream systems that depend on `Delta`
(notably the collision detection's swept tests) must trust the
assembler put `TransformCommitSystem` at the end of every frame.

### Aspirational direction

Collision, rendering, culling, and camera all read `Transform`
directly. The intended end-state is loose coupling — those modules
operate against any *Transform-shaped contract*, so a developer can
swap in `MyTransform` without re-implementing them. The framework
currently doesn't enforce this; treat any `MonoDreams/System/`
reference to `Transform` as implementation debt rather than design.

## 4. Rendering pipeline

The rendering pipeline is the most delicate part of the engine. Bugs
here are visual rather than logical, which makes them harder to debug
than crashes. Two rules cover most of the surface:

**Key invariant — `DrawComponent` is the unified render component.**
There is one `DrawComponent` per renderable entity. Its `Type` field is
one of `Sprite`, `Text`, `NinePatch`, or `Mesh`, mutually exclusive.
Source data lives in companion structs (`SpriteInfo`, `DynamicText`,
`NinePatchInfo`, mesh buffers) populated by prep systems
(`SpritePrepSystem`, `TextPrepSystem`, `MeshPrepSystem`).
**Do not create new draw/render components.** Adding a new visual type
extends `DrawElementType` and `MasterRenderSystem`; it does not fork
the pipeline.

**Key invariant — `MasterRenderSystem` is the sole renderer.** No game
code should call `SpriteBatch` outside the prep-then-master path. A
parallel render system is a framework violation — flagged by review.

**Key rule — rendering systems run last.** In any pipeline assembly,
the prep-cull-sort-render module goes at the end. The recommended order
inside the module, from the reference assembly
(`LoadLevelExampleGameScreen.cs:277–331`), is:
`CullingSystem` → `SpritePrepSystem` → `YSortSystem` → `TextPrepSystem`
→ `MeshPrepSystem` → `MasterRenderSystem`.

**Render targets.** Three are defined by `RenderTargetID`:
- **Main** — world-space, camera transform applied, **respects culling**.
- **UI** — screen-space, no camera transform, always renders.
- **HUD** — screen-space, no camera transform, always renders.

The `Visible` tag is added and removed by `CullingSystem` based on the
camera's `VirtualScreenBounds`. Only the Main target consults it. UI and
HUD entities don't need `Visible`; if you put a `Visible`-gated entity
on UI by mistake, it will always render (which is usually the desired
outcome anyway — but the failure mode is the reverse: a Main-target
entity that never gets `Visible` because no `CullingSystem` is in the
pipeline, and the dev stares at an invisible sprite). The renderable
entity stack is therefore `EntityInfo + Transform + SpriteInfo +
DrawComponent + Visible` for Main, minus `Visible` for UI/HUD.
**This split is a known wart** (§10): `Visible` may become a property of
`DrawComponent` to remove the easy-to-miss tag.

**Layer depth ownership.** Three systems write `LayerDepth` — four when a
screen composes scene layers — in this order: `SpritePrepSystem`
initializes it from the sprite's layer, the optional `SceneLayerSystem`
remaps each scene-layer member's depth from (layer order, within-layer
key) while entities on no layer pass through untouched (see §6),
`YSortSystem` may override it for entities on Y-sorted layers, and
`MasterRenderSystem` sorts on the final value. `YSortSystem` uses a
minimal epsilon to bias parent-child groups so children stay attached
to their parent's depth band. Same-layer entities with the same world Y
fall through to insertion order — no other tiebreaker exists; if
flicker becomes a problem, that's where to look.

**Two coordinate spaces (opt-in).** `ViewportManager` owns an **authoring
(layout)** resolution — where every game number is written: entity and UI
coordinates, HUD/overlay boxes, camera zoom, culling extents, and the point
`MapMouse` returns — and a **render (virtual)** resolution: the pixel size of the
render targets and back buffer. They default to being EQUAL, and a game opts in
by giving the manager a different layout size. The ratio (`RenderScale`) is
applied in **exactly one place — the per-pass render cameras**: world passes take
`ViewportManager.CreateCamera()`, screen-space passes take
`ViewportManager.LayoutCamera` (exactly `Matrix.Identity` when the two spaces are
equal, which is why adopting it changes nothing in a single-space game). Moving a
game from 720p to 1080p is then a two-number diff in the head, with no coordinate
— and no coordinate-carrying test — recalibrated. `ViewportManager.MapMouse` is
the one screen→game pointer mapping; it inverts the present
`DestinationRectangle`, so it is robust to resize, letterboxing and the editor's
viewport inset for free. Details: the `rendering` premises.

**Presentation scaling is declared, not improvised.** How the frame reaches a
window that is not the render resolution is a policy the game declares
(`ViewportManager.Policy`), resolved in one place and tried in one order:
**overscan** to a clean scale (the camera reveals a sliver more world, bounded by
a gamedev-set tolerance) → **letter/pillarbox** at a clean scale → **stretch**.
Whichever step wins produces the single `DestinationRectangle` that
`FinalDrawSystem` composites into and `MapMouse` inverts — a layer that computes
its own destination desyncs the pointer from the picture. Sampling is a separate,
per-layer question (`RenderLayer.Sampler`: `Auto` = point at an integer scale,
linear otherwise), resolved against that layer's own scale. The engine default is
`Stretch` (the historical framing); a scaffolded game declares
`PresentationPolicy.Default`.

**Camera.** A `Camera` instance owns its virtual (destination) resolution and its
render scale (both immutable after construction) and exposes mutable zoom,
position, and rotation. Multiple cameras at once are explicitly supported — local
multiplayer or CCTV-style views. `CameraFollowSystem` is *optional*:
fixed-camera games or custom camera systems are valid; just don't
register `CameraFollowSystem` and `CullingSystem` will still read
whatever the `Camera` reports.

## 5. Physics & collision

The framework provides Newtonian-style movement, gravity, layer-based
collision filtering, and AABB plus convex-polygon (SAT) collision
detection. These pieces are independent components and systems — a
game can use velocity without collision, or collision without
gravity — and the pipeline assembler picks which to register.

**Components.** `Velocity` carries current and last velocity vectors
(the delta is exposed); `RigidBody` carries mass, gravity participation,
kinematic flag, and freeze-axis flags. A collider is its OWN entity now
(colliders-as-entities): `BoxCollider` carries a centered `Size`
(no offset — pose comes from the collider entity's `Transform`) and
active layers; `ConvexCollider` carries collider-entity-local vertices,
world-space vertices, a broadphase AABB and active layers. `ColliderTag`
is auto-applied to any collider entity so detection queries can match a
unified tag. A collider's **body** (the entity a contact acts on) is
resolved via `ColliderBody.Resolve` — nearest `RigidBody`/`Velocity`
ancestor, else itself — and `CollisionMessage` carries both the collider
entities and their bodies; resolution writes corrections to the body.

**The reference physics pipeline order**, from
`LoadLevelExampleGameScreen.cs:277–286`:
**Movement → Velocity → Detection → Resolution → Commit.**
Each stage owns one job:
- *Movement* — game systems write `Velocity` based on input and AI.
- *Velocity (`VelocitySystem`)* — apply `Velocity` to `Transform.Position`.
- *Detection (`TransformCollisionDetectionSystem`)* — broadphase via AABB
  + narrowphase via SAT for `ConvexCollider`; emit `CollisionMessage`s.
- *Resolution (`TransformCollisionResolutionSystem` or
  `TransformPhysicalCollisionResolutionSystem`)* — correct positions and
  velocities for contacts; honor `RigidBody` freeze flags.
- *Commit (`TransformCommitSystem`)* — finalize `LastPosition`, so the
  next frame's `Delta` is meaningful.

Swept collision (the dynamic AABB-vs-AABB and convex-vs-convex tests)
reads `Transform.Delta`. Delta is correct only if the previous frame
ended with `TransformCommitSystem`. Skip `Commit` and collisions go
through walls — see §3.

**`ConvexCollider.BroadPhaseAABB`.** This AABB must be refreshed when
the world vertices change (transform moved or rotated). Detection
filters on it; a stale AABB makes a real collision invisible to the
broadphase. The detection system updates it; if you write a system
that mutates a convex collider's vertices directly, refresh the
broadphase AABB before the next detection pass.

**Single-threaded detection.** `TransformCollisionDetectionSystem`
holds instance-level polygon buffers. It is intentionally not
thread-safe; do not register two instances or invoke it from parallel
contexts.

**Multiple colliders per body (colliders-as-entities).** A collider is its
own entity, so a body owns N collider children — the former
one-collider-of-each-type-per-entity assumption is retired. Detection
iterates collider entities; resolution corrects each contact's body,
accumulating sequentially with per-message re-validation so a body hit
through two of its colliders in one frame is not double-corrected. Two
shape components on ONE entity is still not a thing (it would be a single
collider entity with an ambiguous shape) — give each shape its own child
collider entity.

### Aspirational direction

Collision is currently coupled to `Transform` (see §3). The intended
end-state is collision against any Transform-shaped contract.

## 6. Level loading & entity spawning

**The shipped game boots native `.mdscene` levels only** (see "Native-only
load" below). The LDtk (`.ldtk`) module is now **import-only**: its loader and
parsers run once, off the game boot, to migrate a legacy level into a native
scene the game then owns. The pipeline below describes that **import** path — a
request loads a file, the parser walks the data, and it emits
`EntitySpawnRequest` messages that a factory turns into entities. It is
composed only in the reference screen's `importMode`, never at live boot.

**The pipeline.**
1. Game code publishes `LoadLevelRequest`.
2. The import composition registers **`level-ldtk`'s own
   `LDtkLevelLoadSystem`** as the handler for that message (in place of the
   native-only `LevelLoadRequestSystem` the boot path composes). It loads the
   LDtk file and **adds `LDtkLevelDataComponent` to the world** — the LDtk
   module's own level singleton, carrying the full `LDtkLevel` — plus the
   format-agnostic `CurrentLevelComponent(identifier)` marker and the
   background color.
3. The LDtk parsers (`LDtkEntityParserSystem`, `LDtkTileParserSystem`)
   **subscribe to `LDtkLevelDataComponent` being added** and parse on add.
4. Parsers emit `EntitySpawnRequest`s. Layer-derived data they can no longer
   pass as an LDtk type rides `CustomFields` under the `ldtk:` keys of
   `LDtkSpawnFields`.
5. `EntitySpawnSystem` consumes each spawn request and dispatches to an
   `IEntityFactory` registered for the request's string identifier.

**Key invariant — `level-loading` is format-agnostic; the dependency arrow
points parser → plumbing.** No LDtk type appears in `level-loading` source
(issue #54): the shared `CurrentLevelComponent` holds a plain string
identifier, `EntitySpawnRequest` has no `LayerInstance` member, and
`LevelLoadRequestSystem` has no `ContentManager` and no format fallback.
Everything format-shaped lives in the format's own module — its level
component, its `LoadLevelRequest` handler, and its namespaced `CustomFields`
keys. A game that never installs `level-ldtk` therefore never compiles
against LDtk. When you add a format, add a module; do not add a branch or a
typed member to the shared plumbing.

**Key invariant — the parsers react to component lifecycle, not
to push messages.** Their pattern (subscribe to their module's level
component being added) is the engine-wide default. A test or tool that sets
`LDtkLevelDataComponent` manually triggers them just as well as the regular
`LoadLevelRequest` path — and without a content pipeline. For new parsers,
follow this pattern — resist the urge to make a system "only respond when the
right message arrived"; that couples the system to an upstream sequence that
should be the assembler's choice.

**Factory registration.** `EntitySpawnSystem` keeps a dictionary of
`string → IEntityFactory`. Game code registers factories at screen
setup time. Currently, an unregistered identifier produces a logged
warning and the spawn is silently dropped. **Intended behavior is to
throw** — this is on the backlog (§10). For now, treat the warning as a
high-severity signal during development.

**Native-only load — the content-driven unification (PS5, asymmetry
resolved).** `LevelLoadRequestSystem` is now a **native-only** dispatcher:
each `LoadLevelRequest` probes for a bundled native scene
`Content/Levels/<id>.mdscene` via `TitleContainer` (the console-portable
read) and, on a hit, loads it through the generalized `SceneReaderSystem`
(the same native reader the editor's `LoadSceneRequest` uses —
reconstructing entities from serialized components, not factories). An id
with **no** native scene **fails loud** — there is no LDtk branch in the system
to fall back to (issue #54 removed it along with the `ContentManager`).
Native `.mdscene` is the game's real level format: versioned in
`Content/Levels/`, MGCB-`/copy:`-bundled, read read-only via `TitleContainer`
on every platform (only the desktop editor writes, PS3). **The whole LDtk module
is now IMPORT-ONLY machinery:** its loader + parsers run once — via the import op
(a headless `--export-scene <id>` / `MONODREAMS_EXPORT_SCENE` dev op, or a future
editor toolbar action) — to re-parse a legacy level and serialize the resulting
world to a native `.mdscene` the game then owns; they are **not wired to live
game boot** (composed only in the reference screen's `importMode`, which
registers `LDtkLevelLoadSystem` in place of `LevelLoadRequestSystem`). This
closes the parser-asymmetry backlog (§10): one content-driven load path.
(The Blender importer that once shared this path was retired in wave BR —
see §10.) Migration status: the committed
`Content/Levels/Blender_Level.mdscene` (a native scene originally migrated
from Blender) boots native; the LDtk `Level_0` is not yet migrated (its
~21k per-tile entities need a native tile-layer batching primitive — a
follow-up, §10).

## 7. The reference pipeline

`LoadLevelExampleGameScreen.cs:277–331` is the gold-standard pipeline
assembly. Read it before composing a new screen. The recommended
overall order, end-to-end:

1. **Input** — keyboard/mouse polling, `InputReplaySystem` if running
   under replay.
2. **Game logic** — game-specific systems that read input and entity
   state to update gameplay components (movement intent, AI decisions,
   dialogue state).
3. **Physics module** — `MovementSystem` → `VelocitySystem` →
   `TransformCollisionDetectionSystem` →
   `TransformCollisionResolutionSystem` (or `…PhysicalCollisionResolutionSystem`) →
   `TransformCommitSystem`.
4. **Hierarchy** — `HierarchySystem` (then `TransformCommitSystem` if
   children moved independently), `SizeSystem`, `LayoutSystem`.
5. **Camera** — `CameraFollowSystem` (optional).
6. **Cursor** — `CursorInputSystem`, `CursorPositionSystem`.
7. **Render module** — `CullingSystem` → `SpritePrepSystem` →
   `YSortSystem` → `TextPrepSystem` → `MeshPrepSystem` →
   `MasterRenderSystem` → debug overlays.

Each game's screen owns its own pipeline. The reference assembly is a
recommendation — fixed-camera games omit `CameraFollowSystem`,
non-physics screens skip the physics module, UI-only screens may have
no game logic at all. What does not change is the *shape* of the order:
input first, render last, with physics ahead of hierarchy and hierarchy
ahead of culling.

### Aspirational direction

A declarative system-dependencies API would let
`TransformCollisionDetectionSystem` say "I need `VelocitySystem` before
me this frame" and let the framework validate the assembly. Until that
exists, ordering bugs are caught by review (see the `/deep-review`
skill at §`.claude/skills/deep-review/SKILL.md`) and by behaviour
tests.

## 8. Debug & testing

The engine provides a small set of debug primitives. They are
*available*, not *required* — most screens register none of them.

**`Logger`.** A static, lock-protected singleton in
`MonoDreams.State.Logger`. `Initialize(outputDirectory)` must be called
once before use; `Shutdown()` must be called to flush. Output goes to
`<dir>/monodreams_<timestamp>.log` with format `[wallclock] [GT gametime]
[LEVEL] message`. Replaces `Console.WriteLine`. The `MONODREAMS_DEBUG_DIR`
environment variable redirects all debug output (logs, replay input,
screenshots) for test isolation.

**Input replay.** Drop `debug/input_replay.json` in the build output
directory. Format: `{ startLevel, description, commands: [{ action, type,
time }] }`. Actions match `AInputState` names. The `startLevel` field
skips menus and jumps straight into the named game screen. The game
auto-exits when the replay finishes.

**Frame capture.** `ScreenshotCaptureSystem` writes frames to the debug
directory in one of two formats: PNG (encoded off-thread; the right
choice for occasional verification shots, but the ~50 ms encode caps a
per-frame capture at ~15–26 fps) or raw RGBA (`CaptureFormat.Raw` — a
synchronous memcpy dump sustaining full frame rate, ~3.5 MiB per frame
at 1280x720). Off by default. Env-driven capture goes through
`ScreenshotCaptureSystem.FromEnvironment`, the single owner of the
`MONODREAMS_SCREENSHOT=1|png|raw` / `MONODREAMS_SCREENSHOT_INTERVAL` /
`MONODREAMS_SCREENSHOT_MAX_FRAMES` / `MONODREAMS_SCREENSHOT_TARGET`
contract; the replay-gated pattern (`"screenshots": true` in
`input_replay.json`) still drives PNG-interval capture.
`MONODREAMS_SCREENSHOT_TARGET=Main|UI|HUD|Scroll|Editor` reads that
render target at its own fixed resolution instead of the window
backbuffer (default `window`), so captured evidence keeps the same
geometry across machines, resizes and letterboxing — the target is
resolved from the passes that ran, through
`MasterRenderSystem.RenderedTargetSink`. See the `debug` module's
`docs/overview.md` § Frame capture for the disk-cost table and the
raw-vs-encoded rationale.

**Unattended runs.** `MONODREAMS_KEEP_AWAKE=1` makes a host hold a macOS
`NSProcessInfo` activity for the run (`KeepAwake.FromEnvironment`) — the
in-process `caffeinate -disu`. Without it, App Nap (every headless run
has a hidden window) or display sleep can suspend a long agentic run,
which shows up as a process that stops making progress rather than one
that fails. Opt-in, macOS-only, a logged no-op elsewhere; see the
`debug` module's `docs/overview.md` § Unattended runs for what the flag
does not cover.

**Headless mode — two hosts, two contracts.** There are two headless
paths and they do *not* do the same thing:

- **Examples** (`dotnet run --project MonoDreams.Examples -- --headless`)
  creates a 1×1 off-screen window and **early-returns from `Draw`** — it
  runs Update-side logic at max speed but renders **nothing**. It is
  convenient for fast logic/replay integration tests via `GameTestRunner`
  but cannot observe any visual or render-path behaviour. Treat it as the
  logic-only path; don't rely on it for rendering correctness.
- **Demos** (`dotnet run --project MonoDreams.Demos -- --headless --screen
  <name> --frames <N> --exit`) is the **observe-and-self-verify** path
  (issue #28). It keeps a real `GraphicsDevice` on a hidden, full-virtual-
  resolution backbuffer, **renders every frame** (`Draw` is not a no-op),
  dumps non-blank PNGs to `MONODREAMS_DEBUG_DIR`, logs periodic live-heap
  samples, and self-terminates after `<N>` frames. This is the supported
  way for an agent to verify its own work on the demo host without a human.
  See the `debug` module premises ("Headless Demos renders every frame";
  "Headless heap samples measure the live set") and
  `MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs`.

A literal zero-window mode / null `GraphicsDevice` is **not** possible on
MonoGame DesktopGL 3.8.4 (the window hosts the GL context); a hidden
window that never presents is the achievable form of "headless render".

**Testing.** `MonoDreams.Tests/` uses xUnit + the custom
`GameTestRunner`, which spawns the game in headless mode with a temp
debug directory, writes an `InputReplayPlan`, waits for exit, and
exposes log-assertion helpers (`AssertLogContains`,
`AssertLogContainsInOrder`, `GetLogLines`). The gold-standard tests
today are `SATCollisionTests` (pure-logic), `BlenderLevelTests`
(native scene boot), and `InfiniteRunnerTests` (integration).

**No architectural tests today.** ArchUnit-style assertions
("`MonoDreams/Component/*.cs` must contain only data") do not exist
yet. Most premises in `docs/{domain}/premises.md` start their `Tests:`
field as `none yet`; introducing architectural tests is on the backlog.

## 9. The editor is part of the game

The level editor is **not a separate application** and **not a separate
renderer** — it is the running game under an editor run configuration.
There is one world, one `Camera`, and one draw stack
(`CullingSystem → SpritePrepSystem → YSortSystem → MeshPrepSystem →
TextPrepSystem → MasterRenderSystem`); the editor previews exactly what
the player sees because it runs that same pipeline. What changes between
playing and editing is not *which* pipeline runs but *which systems in
it are allowed to run* — a **run-state contract** the engine codifies in
`foundation` — and the run state is driven by a **transport**, not a
mode-toggle key.

**The transport model.** Under the editor run flag (`--editor` /
`MONODREAMS_EDITOR=1` — the ONLY way into the editor) the shell, chrome,
and editor systems are **always composed and visible**; no key toggles
the editor away. The designer drives the game like a media player through
the toolbar's left-most transport buttons (or the headless
`Play`/`Pause`/`Restart` editor ops), owned by `EditorTransport`:
**Paused** = `RunMode.Edit` (game logic Freeze-gated, editing tools
live — the boot state under the flag), **Playing** = `RunMode.Play`
(the game runs inside the inset viewport; the shell stays up and the
transport buttons + systems panel stay interactive, but the editing
tools are inert — a click in the viewport belongs to the game), and
**Restart** = return the world to the state of the original load:
clear the undo history, remove the world-level **marker** components
(`CurrentLevelComponent` — a plain string level id now — and
`CurrentBackgroundColorComponent`, so the reload starts from a clean
world-level state; a `Set` over a still-present component fires *Changed*, not
*Added*. The LDtk parsers key off `level-ldtk`'s own `LDtkLevelDataComponent`,
which exists only in the import-op composition — where the transport never
runs — so it is deliberately not swept), dispose every scene entity (editor infrastructure — entities tagged
`EditorInfrastructureComponent` — the cursor pipeline, and
screen-`KeepAlive`-named infrastructure survive), re-run the screen's
recorded `Reload`, and land Paused. **Unsaved live edits are discarded
by Restart** — the standard play-mode trade-off; Save first to keep
them.

**The run-state contract.** `GameState.RunMode` is one of `Play`
(default) or `Edit`. A system opts into run-state awareness by being
wrapped in a `GatedSystem` carrying an `EditTimeBehavior` policy
(`RunNormally` / `Freeze` / `RunPartial` / `RuntimeEditable`). Each
frame the gate reads `RunMode` and decides whether to forward to its
child: `RunNormally` runs in both modes; `Freeze` runs in `Play` only;
`RunPartial` and `RuntimeEditable` are reserved (today they run in both,
finer semantics deferred). Editor tooling is **ECS systems over this
gated game pipeline** — selection, gizmo, undo-apply, scene save/load,
and the toolbar are ordinary systems registered alongside the game's,
made inert while Playing by an Edit guard (the transport chrome —
toolbar transport buttons + systems panel — deliberately stays live in
both states). There is no parallel editor data model: a scene
round-trips by serializing the entities' components, not by re-running
factories.

**Key invariant — default `Play` + opt-in gating leaves every existing
screen byte-identical.** `RunMode` defaults to `Play`, and only a system
explicitly wrapped in a `GatedSystem` changes behavior with the mode. A
screen that never wraps a system and never sets `Edit` behaves exactly
as it did before the model existed. This is what makes the run-state
model safe to add across all modules at once.

**Key rule — the gating policy per system group is fixed by what editing
needs to see and not disturb.** Render, input, cursor, and
`HierarchySystem` stay live in `Edit` (`RunNormally`) — the preview must
keep drawing, the designer must keep clicking, and an editor's transform
edit must still propagate to world space the same frame. Game logic,
physics, collision, AI/dialogue, and `CameraFollowSystem` `Freeze` in
`Edit` — they would otherwise move entities out from under the designer
or fight the editor for the camera; in `Edit` the editor drives
`Camera.Position`/`Zoom` directly. Get a policy wrong and the failure is
silent: a frozen render module is a black screen the instant you enter
`Edit`; a live physics module rains gravity on entities you are trying
to place; a frozen `HierarchySystem` shows edits at last frame's world
position. The authoritative system-by-mode table is the interaction
matrix in the level-editor plan-contract and
[`docs/flows/level-editor.md`](flows/level-editor.md); the run-state
premises live in
[`MonoDreams/foundation/docs/premises.md`](../MonoDreams/foundation/docs/premises.md).

**Editor-overlay entities are standalone.** Gizmo handles, the selection
highlight, and the toolbar are never `ChildOfComponent`-parented to game
entities, because `HierarchySystem.DisposeOrphans` runs in `Edit` and
would cascade-dispose them when their host entity is deleted. Deletion is
modeled as an undo command that snapshots the disposed sub-graph, not a
bare `entity.Dispose()`.

**The editor shell is a compositing concern, not a pipeline fork.** With
the editor composed, the game composite renders into a smaller centered
viewport (`ViewportManager.SetViewportInset` — deliberately the same
object that inverts the mouse mapping, so picking follows for free) and
the editor chrome renders around it at native window resolution
(`RenderTargetID.Editor` + `RenderLayer.Native`). The shell is
**constant across transport states** — it never collapses while Playing.
The game pipeline itself is untouched — same passes, same targets — and
without the run flag nothing editor-related is constructed: zero inset,
no chrome layer, byte-identical to a screen without the editor. Details:
the `rendering` and `level-editor` premises.

**The editor is host- and screen-agnostic, and the pipeline is
inspectable.** The editor does not care which host or screen is running —
under the editor run flag every screen of every host (a menu or a module
demo as much as a level) builds its pipelines through the
`EditorPipelineRegistrar` and composes the `EditorOverlay` over its own
world, declaring its own per-system edit policies at the registration
site (e.g. a menu freezes its button interaction in `Edit` so clicks
belong to the editor; a runner freezes its whole simulation block). Where
a screen lacks a prerequisite, the overlay supplies it (its own cursor
pipeline for a cursor-less screen; the `DefaultEditorKeys` key surface
for a host with no action mapping) or degrades gracefully (no Y-sorted
layer ⇒ selection picks on the final source-derived depth). The recipe
for wiring a new host/screen is the level-editor overview's "Adding the
editor to a screen/host" section. The registrar
is also the live inspection surface — and it owns the hierarchy: composite
blocks are registrar groups (`AddGroup`) with named children, built and
gate-wrapped by the registrar itself (DefaultEcs composites hide their
children, so a screen must never pre-build an opaque composite for
anything it wants inspectable). The editor's systems panel renders that
tree — every entry of both pipelines, groups indented above their
children, name, policy, enabled state (tri-state on groups: all/none/
mixed) — and toggles any of them at runtime through `SetEnabled` (leaf =
both-modes master switch; group = cascade over its descendant leaves), so
the per-system edit-mode declaration is visible and adjustable while the
game runs.

**Editing produces versioned, portable, diffable levels — the persistence
story.** A level the editor authors is saved as a native `.mdscene` **into
the game's SOURCE content tree** (`Content/Levels/<id>.mdscene`), versioned
in git — not into the ephemeral build output. Writing is a **desktop-dev-only**
capability (guarded by the editor run flag + an OS check + a resolved project
root, resolved via `MONODREAMS_PROJECT_ROOT` or a walk-up to the `game.mdproj`
manifest; unresolved disables Save loudly, never crashes); **reading is
console-portable** — the shipped game boots a level read-only through
`TitleContainer` over MGCB-`/copy:`-bundled files on every platform (desktop,
web, consoles), native-first via `LoadLevelRequest` (§6), so straying from
MGCB never costs console support. The serializer is **canonical and
byte-stable** (deterministic bytes, stable per-entity ids ordering
`entities[]`), so `load → edit → save` is a fixed point and a git diff of a
level is meaningful. A **`game.mdproj` manifest** makes "a MonoDreams project"
a versionable unit (entry scene, levels dir, asset roots). A new level bundles
**zero-touch** — the editor appends the MGCB `/copy:` entry on first save
(MGCB has no glob; a build-time regen would sweep gitignored placeholder art),
so it boots after a normal build with no manual `.mgcb` editing. And a scene is
**ship-ready** (fully portable) exactly when it has **zero `file:` AssetKeys** —
a checkable lint (all drop-folder art graduated to MGCB content keys); the
committed reference levels are asserted ship-clean. The invariants live in the
`level-editor` and `level-loading` premises.

**One data model — anything authored is component state on an entity,
singletons included; special file blocks are debt.** A scene is a set of
entities carrying components, and *everything the designer authors lives that
way* — including the singletons it is tempting to model as a special
top-level file block (the scene camera, and later the layer map). A `camera`
block, a materialized editor rig for it, bespoke edit commands, and per-mode
re-sync seams are **four parallel representations of one value**, and every
one is a place the value can silently disagree with the others. The camera is
the case study: three camera defects in three days (zoom edits not reaching
the rig or the file) all lived in that camera-only plumbing; the day the
camera became an ordinary `core.Camera` entity — `EntityInfoComponent("Camera")`
+ `TransformComponent` (one rotation, on the Transform) + `CameraComponent`
zoom, serialized in `entities[]` like everything else — the block, the rig,
the commands, and the re-sync seams all deleted, exactly as colliders stopped
breaking the day they became child entities (§5).

**Corollary — Inspector-visible ⇒ Save-persisted ⇒ round-trip-owned.** If a
value shows up in the Inspector, Save must persist it; if Save persists it,
`load → save` must own it as a byte fixed point. A value that is editable but
not serialized (or serialized but not round-trip-stable) is a bug waiting to
surface as "my edit vanished on reload". The one data model is what makes this
hold for free: an authored value is a component, and components already
round-trip.

**The extension recipe (how to add a new authored thing).** Do NOT add a file
block or a bespoke editor subsystem. Instead:

1. **Define a component** with clearly named authored vs derived fields (author
   the source; recompute the derived — e.g. `CameraComponent.Zoom` is authored,
   the view matrix is derived; a collider's `Size` is authored, its world AABB
   derived).
2. **Register its serializer** (`ComponentSerializerRegistry` — engine types in
   `EngineComponentSerializers`, game types in the game's own registration),
   so it round-trips through the canonical serializer.
3. **Optionally add an Inspector default-initializer** so "Add ⟨component⟩"
   births a sane instance.

Those three steps buy the whole authoring stack — file authoring, Inspector
editing, undo, dirty-tracking, prefab overrides, byte-stable diffs, and
Game-mode sandbox protection — **by composition, not configuration**. There is
no fourth step and no special case. When a value seems to *need* a file block,
that is the signal it wants to be a component on an entity instead. (A legacy
block that predates this tenet is migrated forward by the CLI umbrella
`monodreams migrate` — the camera block's v2→v3 lift is the reference; see the
`level-editor` migrator premises.)

### Aspirational direction

The reserved `RunPartial` / `RuntimeEditable` policies are placeholders
for finer edit-time behavior (a system that does reduced work, or stays
interactive, while editing) — they run-as-`RunNormally` today and gain
distinct semantics when a later wave needs them. The interaction matrix
is enforced by review and by the editor screen's own tests until a
declarative system-dependency API (§2, §7) can validate it at
registration time.

## 10. Refactor backlog (named cruft)

Carried forward from the bootstrap interview so they are not forgotten.
Each is either implementation debt or an aspirational direction that
will eventually move from "documented in CORE_TENETS" to "enforced in
code".

- **`ChildOf` vs `Transform.Parent` split** (§3). Two parent links
  exist today; consolidation desired. Until then, read both when
  reasoning about hierarchy.
- **`Visible` as a tag** (§4). Could become `DrawComponent.Visible`.
  Open question: would moving it complicate the bulk add/remove
  pattern `CullingSystem` uses today?
- **`Blender_` identifier prefix / parser-asymmetry** (§6). **RESOLVED
  (PS5); Blender importer DELETED (wave BR).** The game boot is a single
  native-only dispatcher (`LevelLoadRequestSystem`): `LoadLevelRequest` →
  native `.mdscene` via `SceneReaderSystem`, or fail loud. The LDtk module is
  import-only machinery (its own `LDtkLevelLoadSystem` + parsers, composed only
  in the reference screen's `importMode`, run by the export op), so nothing
  legacy runs at boot — and since issue #54 `level-loading` carries no LDtk
  type at all, so the separation is structural, not a flag. The
  Blender importer that once shared the `Blender_` name-prefix dispatch was
  retired wholesale in wave BR (parser + data types + exporter plugin + CLI
  registry entry + module-count docs), so the name-prefix hack is gone
  end-to-end — no parser asymmetry remains. Residual: the LDtk `Level_0` is
  not yet migrated to native (it needs a **native tile-layer batching
  primitive** — a compact representation for ~21k per-tile entities, so its
  `.mdscene` isn't a multi-MB per-entity dump); until then it is import-only
  and not offered by the reference menu. When the LDtk parser too is
  eventually deleted, the import op moves to a standalone tool.
- **`EntitySpawnSystem` silent-drops unregistered factories** (§6).
  Intended behaviour is to throw.
- **`Transform.Delta` consistency not enforced** (§3). No API today
  prevents reading stale `Delta` mid-frame; `Transform` should expose
  interaction methods that guarantee consistency.
- **Module-level coupling to `Transform`** (§3, §5). Collision,
  rendering, camera, culling all read `Transform` directly. The
  eventual end-state is loose coupling to any Transform-shaped
  contract.
- **Folder layout** (`Component/<subdomain>/` vs `Component/<file>/`
  inconsistency). Intended end-state is **modular packs** (Spring Data
  / Spring Security analog) — `MonoDreams.Rendering`,
  `MonoDreams.Physics`, etc. — that can be adopted independently.
- **Examples headless mode is logic-only** (§8). Its `Draw` early-returns,
  so it renders nothing — fine for replay/logic tests, useless for visual
  observation. The Demos headless path (issue #28) is the load-bearing
  observe-and-self-verify route and *does* render; the remaining debt is
  that the Examples mode is still named "headless" despite not rendering,
  and the two paths could eventually share one host abstraction.
- **No architectural tests** (§8). Most premises lack programmatic
  protection; review and discipline are the only enforcement today. The
  one exception is the **manifest-honesty check** (issue #83,
  `MonoDreams.Cli.Tests/ManifestHonestyTests.cs`): every module is
  scaffolded, `add`ed and built against its *declared* dependencies alone,
  in CI, per module.
- **Module manifests that under-declare** (§1, tooling). Surfaced by the
  manifest-honesty check and listed there as known gaps — each one is a
  project that does not compile after `monodreams add <module>`:
  `ui` and `level-editor` omit modules they import (`cursor`, `camera`
  respectively — both acyclic, one-line manifest fixes); `dialogue` omits
  both `cursor` and the `MonoGame.Framework.Content.Pipeline` package its
  YarnSpinner importer needs; `foundation` (`ScreenController` takes
  `rendering`'s `ViewportManager`/`Camera`) and `rendering`
  (`DrawComponent`/`MasterRenderSystem` read `rendering-text`'s
  `DynamicTextComponent.DefaultLineSpacing`) cannot declare their way out —
  both would be cycles, so the coupling has to move in code. Until
  `foundation` + `rendering` are fixed, the check installs a compile floor
  and every scaffolded game inherits the same two modules whether it wants
  them or not.
- **Declarative system dependencies** (§2, §7). A future API would let
  a system declare "I expect X to have run this frame" at registration
  time, replacing implicit-order discipline with explicit assertions.
- **Test or example per public contract** (§2). Several messages
  (`PositionChangeMessage`, `SizeChangeMessage`, `RigidBodyTouchMessage`)
  are exposed but may be leftovers without active consumers. Every
  contract should ship with at least one usage.
