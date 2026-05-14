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
backlog** (§9); read both fields when reasoning about hierarchy until it
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
consistency" (§9). For now, downstream systems that depend on `Delta`
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
the prep-cull-sort-render block goes at the end. The recommended order
inside the block, from the reference assembly
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
**This split is a known wart** (§9): `Visible` may become a property of
`DrawComponent` to remove the easy-to-miss tag.

**Layer depth ownership.** Three systems write `LayerDepth`, in this
order: `SpritePrepSystem` initializes it from the sprite's layer,
`YSortSystem` may override it for entities on Y-sorted layers, and
`MasterRenderSystem` sorts on the final value. `YSortSystem` uses a
minimal epsilon to bias parent-child groups so children stay attached
to their parent's depth band. Same-layer entities with the same world Y
fall through to insertion order — no other tiebreaker exists; if
flicker becomes a problem, that's where to look.

**Camera.** A `Camera` instance owns its virtual resolution
(immutable after construction) and exposes mutable zoom, position, and
rotation. Multiple cameras at once are explicitly supported — local
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
kinematic flag, and freeze-axis flags; `BoxCollider` carries an AABB
and active layers; `ConvexCollider` carries model-space vertices,
world-space vertices, a broadphase AABB and active layers. `ColliderTag`
is auto-applied to any entity with a collider component so detection
queries can match a unified tag.

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

**One collider of each type per entity.** Today the framework assumes
an entity has at most one `BoxCollider` and at most one `ConvexCollider`.
This is implicit, not enforced. Multiple colliders of the same type on
a single entity is undefined behavior — if you need it, that's a
framework change, not a workaround.

### Aspirational direction

Collision is currently coupled to `Transform` (see §3). The intended
end-state is collision against any Transform-shaped contract.

## 6. Level loading & entity spawning

Levels live in `.ldtk` files (LDtk editor) and `.json` exports from
Blender (`Tools/blender_level_export.py`). The pipeline is identical
in shape: a request loads a file, a parser walks the data, and the
parser emits `EntitySpawnRequest` messages that a factory turns into
entities.

**The pipeline.**
1. Game code publishes `LoadLevelRequest`.
2. `LevelLoadRequestSystem` consumes the request, loads the file, and
   **adds `CurrentLevelComponent` to the world**. The background colour
   lands as `CurrentBackgroundColorComponent`.
3. Parser systems (`LDtkEntityParserSystem`, `LDtkTileParserSystem`,
   `BlenderLevelParserSystem`) **subscribe to `CurrentLevelComponent`
   being added** — not to the message. They parse on add and emit
   `EntitySpawnRequest`s.
4. `EntitySpawnSystem` consumes each spawn request and dispatches to an
   `IEntityFactory` registered for the request's string identifier.

**Key invariant — systems react to component lifecycle, not to push
messages.** The parser pattern (subscribe to `CurrentLevelComponent`
added) is the engine-wide default. A test or tool that adds
`CurrentLevelComponent` manually triggers the parsers just as well as
the regular `LoadLevelRequest` path. Resist the urge to make a system
"only respond when the right message arrived" — that's coupling the
system to an upstream sequence that should be the assembler's choice.

**Factory registration.** `EntitySpawnSystem` keeps a dictionary of
`string → IEntityFactory`. Game code registers factories at screen
setup time. Currently, an unregistered identifier produces a logged
warning and the spawn is silently dropped. **Intended behavior is to
throw** — this is on the backlog (§9). For now, treat the warning as a
high-severity signal during development.

**`Blender_` identifier prefix.** The level loader looks at the level
identifier; if it starts with `Blender_`, the Blender parser handles
it, otherwise the LDtk parser does. **This dispatch by name prefix is
a quick hack** (§9); a content-driven dispatch (a format field in the
level data) is the eventual replacement.

## 7. The reference pipeline

`LoadLevelExampleGameScreen.cs:277–331` is the gold-standard pipeline
assembly. Read it before composing a new screen. The recommended
overall order, end-to-end:

1. **Input** — keyboard/mouse polling, `InputReplaySystem` if running
   under replay.
2. **Game logic** — game-specific systems that read input and entity
   state to update gameplay components (movement intent, AI decisions,
   dialogue state).
3. **Physics block** — `MovementSystem` → `VelocitySystem` →
   `TransformCollisionDetectionSystem` →
   `TransformCollisionResolutionSystem` (or `…PhysicalCollisionResolutionSystem`) →
   `TransformCommitSystem`.
4. **Hierarchy** — `HierarchySystem` (then `TransformCommitSystem` if
   children moved independently), `SizeSystem`, `LayoutSystem`.
5. **Camera** — `CameraFollowSystem` (optional).
6. **Cursor** — `CursorInputSystem`, `CursorPositionSystem`.
7. **Render block** — `CullingSystem` → `SpritePrepSystem` →
   `YSortSystem` → `TextPrepSystem` → `MeshPrepSystem` →
   `MasterRenderSystem` → debug overlays.

Each game's screen owns its own pipeline. The reference assembly is a
recommendation — fixed-camera games omit `CameraFollowSystem`,
non-physics screens skip the physics block, UI-only screens may have
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

**Screenshots.** `ScreenshotCaptureSystem` saves PNGs every 2 seconds to
the debug directory. Off by default; enable by setting `"screenshots":
true` in `input_replay.json`.

**Headless mode.** `dotnet run --project MonoDreams.Examples -- --headless`
creates a 1×1 off-screen window, disables VSync, removes the fixed
timestep, and runs at maximum speed. **This is experimental and not
load-bearing.** It is convenient for fast integration tests via
`GameTestRunner` and was originally intended to let AI agents
"visually" test gameplay, but the implementation is not currently
trusted as a stable testing contract — flakes and missing edge cases
should be expected. Tests that need rendering correctness should not
rely on it.

**Testing.** `MonoDreams.Tests/` uses xUnit + the custom
`GameTestRunner`, which spawns the game in headless mode with a temp
debug directory, writes an `InputReplayPlan`, waits for exit, and
exposes log-assertion helpers (`AssertLogContains`,
`AssertLogContainsInOrder`, `GetLogLines`). The gold-standard tests
today are `SATCollisionTests` (pure-logic), `BlenderLevelTests`
(parsing), and `InfiniteRunnerTests` (integration).

**No architectural tests today.** ArchUnit-style assertions
("`MonoDreams/Component/*.cs` must contain only data") do not exist
yet. Most premises in `docs/{domain}/premises.md` start their `Tests:`
field as `none yet`; introducing architectural tests is on the backlog.

## 9. Refactor backlog (named cruft)

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
- **`Blender_` identifier prefix** (§6). Dispatch by name prefix is a
  hack; a content-driven dispatch (format field in level data) is the
  eventual replacement.
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
- **Headless mode is experimental** (§8). Original intent was AI-agent
  visual testing; current implementation is not stable enough to be
  load-bearing.
- **No architectural tests** (§8). Most premises lack programmatic
  protection; review and discipline are the only enforcement today.
- **Declarative system dependencies** (§2, §7). A future API would let
  a system declare "I expect X to have run this frame" at registration
  time, replacing implicit-order discipline with explicit assertions.
- **Test or example per public contract** (§2). Several messages
  (`PositionChangeMessage`, `SizeChangeMessage`, `RigidBodyTouchMessage`)
  are exposed but may be leftovers without active consumers. Every
  contract should ship with at least one usage.
