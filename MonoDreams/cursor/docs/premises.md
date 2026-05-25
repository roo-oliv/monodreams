# cursor — premises

> Technical invariants the engine assumes about the cursor block:
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

## `CursorPositionSystem` must run after the camera updates

`CursorPositionSystem` calls `camera.VirtualScreenToWorld(...)` to
compute the cursor's world position. That call depends on the camera
having its final position for the frame. Any system that moves the
camera (`CameraFollowSystem` from the `camera` block, or game-specific
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
