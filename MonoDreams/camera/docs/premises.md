# camera — premises

> Technical invariants the engine assumes about the camera-follow block:
> `CameraFollowTargetComponent` and `CameraFollowSystem`. Read this
> before changing either piece or any code that expects the camera to
> auto-track an entity.

## `CameraFollowSystem` is optional, not required

Fixed-camera, local multiplayer (multiple cameras at once), and
CCTV-style setups are explicitly in scope. A screen owns whether and
how its camera updates; `CullingSystem` and `MasterRenderSystem` read
whatever the `Camera` reports each frame regardless of who wrote to it.
Installing this block adds the option to register `CameraFollowSystem`;
a screen that wants manual camera control simply omits the registration
and writes to `camera.Position` / `camera.Zoom` directly.

**Why:** the framework targets any 2D game, including ones with no
moving camera or multiple simultaneous cameras. Requiring a follow
system would foreclose those.
**Breaks:** assuming `CameraFollowSystem` is always in the pipeline
leads code to read stale follow-target data when it isn't there.
Conversely, registering this system and *also* writing to
`camera.Position` from another system creates a tug-of-war (last write
wins per frame).
**Tests:** none yet.
**Depends on:** —

## The `Camera` class itself ships in the `rendering` block

`Camera` is a hard dependency of the draw stack — `MasterRenderSystem`
needs its view matrix every frame — so it lives at
`MonoDreams/rendering/Camera.cs`, not in this block. This block adds
**only** the optional follow behavior (`CameraFollowTargetComponent`
plus `CameraFollowSystem`). Game code can use `Camera` without ever
installing `camera`.

**Why:** decoupling who-owns-the-position (this block) from
what-the-camera-is (the `rendering` block) lets fixed-camera games skip
this block entirely without losing rendering. The split is the
cleanest expression of the underlying invariant: rendering depends on
*a* `Camera`, not on *how* the `Camera` is updated.
**Breaks:** if a future refactor moves `Camera` into this block, every
game that wants rendering will be forced to also install camera-follow
plumbing it doesn't use, and the `rendering` block will gain a
mandatory dependency on a behavior block — both bad smells.
**Tests:** none yet.
**Depends on:** rendering — "`Camera.VirtualResolution` is immutable".

## `CameraFollowSystem` picks the first `IsActive` target each frame

When multiple entities have `CameraFollowTargetComponent`,
`CameraFollowSystem` iterates the entity set and follows the first one
whose `IsActive` flag is true. There is no deterministic ordering guarantee
across runs — the order is whatever DefaultEcs's `EntitySet` enumeration
produces.

**Why:** the framework supports the common case (one active target at a
time, toggled via `IsActive`) with a minimal implementation. A
proper multi-target API would need a priority field or an explicit
selection message; that's framework work, not a workaround.
**Breaks:** two entities with `IsActive = true` causes a non-deterministic
target choice. The dev sees the camera "snap" to a different entity on
reload with no obvious cause.
**Tests:** none yet.
**Depends on:** —

## Follow runs after movement, before rendering

The reference pipeline places `CameraFollowSystem` after the physics /
movement block (so the target's `TransformComponent.Position` is the
final position this frame) and before the prep / cull / render block
(so culling and view-matrix consumers see the new camera position).
Following before movement makes the camera lag the target by one
frame; following after culling makes the cull frustum reflect last
frame's camera position.

**Why:** the camera's job is to track the *final* position of its
target. Anything that moves the target (physics, AI, scripted motion)
must complete before the follow runs; anything that consumes the
camera (culling, rendering) must run after.
**Breaks:** put `CameraFollowSystem` ahead of `VelocitySystem` and the
camera trails the player by one frame, producing visible lag. Put it
after `CullingSystem` and entities pop in/out at the edges as the cull
check uses last frame's bounds.
**Tests:** none yet.
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## Follow bounds clamp the target before smoothing

`CameraFollowTargetComponent.Bounds` is an optional world-space `Rectangle?`.
When set, `CameraFollowSystem` clamps the *desired target position* to that
rectangle *before* the smoothing/snap step, then eases the camera toward that
clamped point. The camera tracks the target but never aims past the rectangle's
edges (the standard "keep the camera inside the level" behavior) — and because
it is the target that is clamped, a camera that is currently *outside* the
bounds (e.g. control was just handed back from an unbounded target) eases
smoothly back inside rather than snapping to the edge. When null the camera
follows freely.

**Why:** games want the camera to stop at level edges without each screen
re-implementing a clamp (which would mean a second writer to `camera.Position`
— the tug-of-war this block's first premise warns against). Clamping the
*target* keeps a single owner of the camera position *and* a single smooth
easing path to an in-bounds goal.
**Breaks:** if a future change clamps the *resolved* position after smoothing
instead, it hard-caps X/Y each frame — so handing control back to a bounded
target from outside the bounds (switching targets, or a target sitting beyond
the bounds) snaps the camera to the edge in one frame instead of easing in.
Bounds smaller than the viewport are legal but mean the camera barely moves —
that's the caller's choice, not a bug.
**Tests:** `MonoDreams.Tests/Camera/CameraFollowBoundsTests.cs`.
**Depends on:** —

## Open questions

- **Multi-camera support** — `Camera` instances are not registered
  centrally; `CameraFollowSystem` is constructed with one `Camera` and
  follows targets into that camera. A split-screen game with two
  cameras and two follow systems should work in principle, but the
  pattern hasn't been exercised.

## Aspirational direction

- A priority field on `CameraFollowTargetComponent` (and a deterministic
  pick when multiple are active) would replace today's first-active-wins
  iteration order.
- Camera shake, look-ahead, dead-zones, and look-at as composable
  systems that read `CameraFollowTargetComponent` and write to the same
  `Camera`.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `CameraFollowSystem` is optional, not required
- The `Camera` class itself ships in the `rendering` block
- `CameraFollowSystem` picks the first `IsActive` target each frame
- Follow runs after movement, before rendering
