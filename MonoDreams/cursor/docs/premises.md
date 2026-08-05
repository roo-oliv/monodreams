# cursor — premises

> Technical invariants the engine assumes about the cursor module:
> `CursorControllerComponent`, `CursorInputComponent`,
> `CursorTexturesComponent`, the `Cursor.Create` factory, and the
> three cursor systems. Read this before changing any of those pieces
> or any code that reads cursor world/screen position for hit-testing
> or UI hover.

## Cursor system order: input → position → draw prep

The three cursor systems form a strict three-stage pipeline within a
screen's update loop. `CursorInputSystem` polls hardware mouse state
and writes the raw `ScreenPosition` plus button/delta state.
`CursorPositionSystem` converts screen position to virtual and world
coordinates and writes `TransformComponent.Position` based on the
cursor's render target. `CursorDrawPrepSystem` reads the final
transform and populates `DrawComponent` with the texture for the
active `CursorType`.

**Why:** each stage depends on the previous one's output. Game code
between stages can read intermediate state cleanly (e.g.
`ButtonInteractionSystem` reads `CursorInputComponent.WorldPosition`
after `CursorPositionSystem` has run). Reordering the stages produces
silent one-frame lag or stale world coordinates.
**Breaks:** running `CursorDrawPrepSystem` before
`CursorPositionSystem` paints the cursor at last frame's position;
running `CursorPositionSystem` before `CursorInputSystem` uses last
frame's raw mouse position.
**Tests:** none yet.
**Depends on:** —

## Button press/release edges derive from CursorInputSystem's own previous-state, immune to consumers clearing the level fields

`CursorInputSystem` computes the per-frame button edges (`LeftButtonPressed` /
`LeftButtonReleased`, and the right/middle equivalents) by diffing the current hardware
read against `CursorInputComponent.PreviousLeftButton` / `PreviousRightButton` /
`PreviousMiddleButton` — dedicated previous-state fields it writes straight from the raw
read each frame, **before any consumer runs**. It does **not** reuse the mutable
`LeftButton` / `RightButton` / `MiddleButton` level fields as the previous state. A
downstream consumer is free to clear the level fields (and the edges) to suppress a click —
an editor modal dialog's pointer-edge consume does exactly this every frame it is open — and
the next frame's edges are still derived correctly. This mirrors the scroll wheel, where the
edge (`ScrollWheelDelta`) is derived from an accumulator (`ScrollWheelValue`) that a consumer
never clears. The `PreviousXButton` fields are not read on the `SkipHardwareRead` / injected
path (the editor-op / replay channel authors edges directly and tracks its own previous state).

**Why:** reusing the level field as the previous state means a consumer that forces
`LeftButton = false` every frame makes `LeftButtonReleased = !LeftButton && prevLeft` forever
false (`prevLeft` reads the cleared level) — so a system acting on the release edge (the
Save dialog buttons) can never observe its own click. This was the confirmed cause of the
"clicking dialog buttons does nothing" bug. Owning the previous state makes the edge
derivation robust to ANY pointer-edge consumer, present or future.
**Breaks:** any modal / overlay that consumes pointer edges by also clearing the button level
silently kills its own (and every subsequent) click while open; the release edge is
structurally unobservable.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorDialogTests.cs`
(`SaveDialog_ClickSaveSceneThroughRealCursorPipeline_InvokesOnRelease` and
`ConfirmSwitch_ClickDiscardThroughRealCursorPipeline_DiscardsOnRelease` — scripted press→releases
through the real `CursorInputSystem → editor.dialog` order act on the release edge, exercising the
consume→edge interaction the injected-edge tests bypassed).
**Depends on:** level-editor — "The editor's Save dialog is a modal three-action chooser
(Save Scene / Save Project / Save Backup As…) that owns input while open" (the consumer this protects).

## `CursorPositionSystem` must run after the camera updates

`CursorPositionSystem` calls `camera.VirtualScreenToWorld(...)` to
compute the cursor's world position. That call depends on the camera
having its final position for the frame. Any system that moves the
camera (`CameraFollowSystem` from the `camera` module, or game-specific
camera control) must run before `CursorPositionSystem`.

**Why:** the camera's view transform is the projection that turns
virtual-screen coords into world coords. Running `CursorPositionSystem`
before the camera moves means the cursor's world position lags by one
frame — visible as click/hover offset during camera motion.
**Breaks:** during scrolling or camera-shake, world-space hit-testing
(e.g., picking up an item under the cursor) targets the wrong location.
The reference pipeline at
`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs` puts
`CursorPositionSystem` after `CameraFollowSystem` for exactly this
reason.
**Tests:** none yet.
**Depends on:** camera — "Follow runs after movement, before rendering".

## Cursor renders on the HUD target by default

`Cursor.Create` defaults to `RenderTargetID.HUD`. HUD entities render
in screen-space, after the Main (world) and UI targets, so the cursor
always sits on top of everything else.

**Why:** the cursor must be the visually topmost element to behave like
a system cursor — clicking through the cursor would be a UX bug. HUD
target gives unconditional always-on-top behavior; render order
(Main → UI → HUD) is enforced by `MasterRenderSystem`.
**Breaks:** rendering the cursor on Main subjects it to camera
transforms and to `CullingSystem` — the cursor would zoom with the
camera, vanish at the edges of the view, and respect Y-sort with world
sprites. Rendering on UI puts it under HUD elements.
**Tests:** none yet.
**Depends on:** rendering — "Three render targets, two behaviors".

## Cursor `TransformComponent.Position` depends on render target

`CursorPositionSystem` sets `TransformComponent.Position` differently
based on `DrawComponent.Target`: HUD target uses virtual-screen coords
plus `HotSpot` (no camera transform applied), Main target uses
world coords plus `HotSpot` (camera transform will be applied at draw
time). `CursorInputComponent.WorldPosition` is always populated
regardless of target, so game systems (hit-testing, button hover) can
read world coordinates without caring how the cursor is rendered.

**Why:** the cursor entity participates in the same draw pipeline as
everything else, so its `TransformComponent.Position` must already be
in the coordinate space that target expects. Decoupling `WorldPosition`
from the rendered position lets the game logic stay target-agnostic.
**Breaks:** if a game system reads `transform.Position` for hit-testing
and the cursor is on HUD, the hit-test runs against screen coords and
fails. Always read `CursorInputComponent.WorldPosition` for world-space
checks.
**Tests:** none yet.
**Depends on:** —

## Cursor is a single entity, created via the `Cursor.Create` factory

`Cursor.Create(world, textures, renderTarget)` is the canonical entry
point. It composes the cursor entity from the four components
(`CursorControllerComponent`, `CursorInputComponent`,
`TransformComponent`, `CursorTexturesComponent`) plus a `DrawComponent`
seeded with the initial texture. Game code should not hand-roll cursor
entities by setting the components individually — the factory is the
contract.

**Why:** the framework-not-library tenet says one canonical entry per
behavior. A hand-rolled cursor risks omitting a component (most
commonly `CursorTexturesComponent`, since `CursorDrawPrepSystem` needs
it to swap textures on hover state changes).
**Breaks:** a cursor entity missing `CursorTexturesComponent` falls
out of `CursorDrawPrepSystem`'s query — the cursor texture never
updates on hover, and the controller's `Type` field becomes a
write-only dead field.
**Tests:** none yet.
**Depends on:** —

## A mesh cursor renders via `Cursor.CreateMesh` + `MeshPrepSystem`, not `CursorDrawPrepSystem`

`Cursor.CreateMesh(world, meshData, renderTarget)` builds a cursor whose
silhouette is a generated mesh (e.g. an arrow), authored in local space with
its hot-spot at the origin. It composes `CursorControllerComponent` +
`CursorInputComponent` + `TransformComponent` + a `Mesh` `DrawComponent` +
`VisibleComponent`, and deliberately carries **no** `CursorTexturesComponent`.
It is positioned by the same `CursorInputSystem` → `CursorPositionSystem` pair
(neither requires the textures component) and rendered by the screen's existing
`MeshPrepSystem` — so a mesh-cursor screen registers **no** `CursorDrawPrepSystem`.
The textured path (`Cursor.Create` + `CursorTexturesComponent` +
`CursorDrawPrepSystem`) is unchanged and is what `MonoDreams.Examples` uses.

**Why:** the engine is mesh-capable, and a generated arrow keeps the demos free
of any cursor image asset. `MeshPrepSystem` already writes the per-frame world
matrix for every `Mesh` entity with `VisibleComponent`, so the cursor needs no
bespoke draw-prep — only the transform that `CursorPositionSystem` already sets.
**Breaks:** a mesh cursor missing `VisibleComponent` falls out of
`MeshPrepSystem`'s query and renders at last frame's (or the origin's) matrix.
Registering `CursorDrawPrepSystem` for a mesh cursor is harmless (it filters on
`CursorTexturesComponent`, which the mesh cursor lacks) but pointless.
**Tests:** none yet (exercised by every demo screen — all four use the mesh cursor).
**Depends on:** rendering — "`MeshPrepSystem` writes the world matrix once per
frame"; rendering — "Three render targets, two behaviors" (HUD always renders).

## `CursorMeshLibraryComponent` holds the per-`CursorType` silhouettes a mesh cursor swaps between

`CursorMeshLibraryComponent` is an optional add-on for a mesh cursor: a
`Dictionary<CursorType, MeshData>` whose `Default` entry is the resting (arrow) mesh and
whose other entries (e.g. `Hand`) are alternate silhouettes. It is pure data — the *swap*
is owned by a consumer system (today the `ui` module's `CursorHoverSystem`, see the ui
premises), which sets `CursorControllerComponent.Type` and, on a change, fills the cursor
entity's mesh `DrawComponent` from the matching library entry. A mesh cursor without this
component simply never swaps; the textured cursor path (`CursorTexturesComponent` +
`CursorDrawPrepSystem`) is the parallel mechanism for image cursors and is unaffected.

**Why:** the engine is mesh-capable, so a hover-cursor change is "pick a different mesh",
exactly mirroring how `CursorTexturesComponent` lets the textured path "pick a different
texture" per `CursorType`. Keeping the silhouettes as data (and the swap in a system)
keeps the mechanism reusable for any cursor type and any consumer.
**Breaks:** a library missing the `Default` entry leaves a consumer with no arrow to fall
back to (the cursor keeps whatever mesh it last had). Mutating the cursor's mesh
`DrawComponent` from elsewhere races the swap system the same way two writers race any
shared component.
**Tests:** none yet (exercised by the `ui` demo's Link-button hand cursor).
**Depends on:** "A mesh cursor renders via `Cursor.CreateMesh` + `MeshPrepSystem`".

## `SkipDerivation` lets an injection channel own the cursor's derived positions

`CursorPositionSystem.SkipDerivation` is the derivation-half twin of
`CursorInputSystem.SkipHardwareRead`. A channel that **injects** cursor state rather than
reading a mouse — the editor-op replay channel, an input-replay plan, a headless test — sets
both: `SkipHardwareRead` stops the hardware read from overwriting the injected
`CursorInputComponent`, and `SkipDerivation` stops the per-frame screen→virtual→world
derivation from recomputing `VirtualPosition` / `WorldPosition` / `OutsideViewport` /
`TransformComponent.Position` on top of it. With the flag set the system early-returns before
it touches the camera or the viewport manager, so the injected frame is exactly what
downstream consumers read. A real-mouse session leaves it `false` (the default), so every
existing screen is byte-identical.

**Why:** an injection channel authors world-space intent (`WorldPosition` / `VirtualPosition`),
not a window pixel, so the injected `ScreenPosition` is not a mappable in-viewport coordinate.
Live derivation feeds that un-mapped `ScreenPosition` to
`ViewportManager.ScaleMouseToVirtualCoordinates`, gets `null`, and clobbers the injection with
`OutsideViewport = true` (and, whenever the injected screen position *does* happen to map,
overwrites the injected virtual/world positions and the cursor transform with values derived
from it). `SkipHardwareRead` alone therefore cannot deliver an injected cursor: the very next
system in the canonical order undoes it.
**Breaks:** replay / editor-op cursor injection silently produces `OutsideViewport = true` plus
stale or recomputed world coordinates — every world-space consumer treats the click as "over
chrome, ignore it", picking and gizmo drags never fire, and mouse input replay is structurally
impossible.
**Tests:** `MonoDreams.Tests/Cursor/CursorPositionSystemTests.cs`
(`SkipDerivation_InjectedCursorState_SurvivesTheFrame` — the injected virtual/world positions,
`OutsideViewport = false`, and the transform all survive an un-mapped `ScreenPosition`; plus the
two contrast cases `WithoutSkipDerivation_UnmappedScreenPosition_ClobbersInjectionWithOutsideViewport`
and `WithoutSkipDerivation_MappedScreenPosition_RecomputesVirtualWorldAndTransform`, which pin
the clobber the flag exists to prevent).
**Depends on:** "Button press/release edges derive from CursorInputSystem's own previous-state,
immune to consumers clearing the level fields" (the same injected path — its `PreviousXButton`
fields are likewise not read when `SkipHardwareRead` is set; the injection channel owns the
button edges the way `SkipDerivation` hands it the positions); "Cursor system order: input →
position → draw prep" (the derivation this flag disables is stage two).

## Open questions

- **Multiple cursors** — the systems iterate an entity set, so two
  cursor entities would both update. The hardware mouse state is
  global, though, so both would mirror the same position. Couch
  multiplayer with split inputs would need an input-source field on
  `CursorInputComponent`.
- **Cursor when mouse leaves the window** — `CursorPositionSystem`
  keeps the previous position when `ScaleMouseToVirtualCoordinates`
  returns null (mouse in letterbox/pillarbox). Whether that's the
  desired behavior or whether the cursor should hide itself is
  unsettled.

## Aspirational direction

- Make `CursorDrawPrepSystem`'s render target, size, layer depth, and
  opacity configurable per cursor entity (currently hardcoded — see
  the `TODO` in `CursorDrawPrepSystem.cs`).
- Cursor lock / show / hide as first-class operations rather than via
  `IsVisible` on the controller alone.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Cursor system order: input → position → draw prep
- `CursorPositionSystem` must run after the camera updates
- Cursor renders on the HUD target by default
- Cursor `TransformComponent.Position` depends on render target
- Cursor is a single entity, created via the `Cursor.Create` factory
