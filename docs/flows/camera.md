---
flow: camera
covers:
  - MonoDreams/camera/**
sensitive: false
---

# Camera follow

Each frame `CameraFollowSystem` eases a single `Camera` toward the world position of the entity it
tracks, then the draw stack reads that camera. The system iterates its `EntitySet` of
`CameraFollowTargetComponent` + `TransformComponent` entities, picks the **first** whose `IsActive`
flag is true, and treats that target's `Transform.Position` as the desired camera position. If a
`Bounds` rectangle is set the desired position is clamped to it *first* (clamp the target, not the
result — so a camera starting outside eases back in rather than snapping). The per-axis distance
from the current `camera.Position` is clamped to `MaxDistanceX/Y`, then frame-rate-independent
exponential smoothing (`1 - exp(-Damping * dT)`) lerps the camera toward that constrained target,
snapping the last hundredth of a unit to kill jitter. The system writes **only** `camera.Position`
— never `Zoom` or `Rotation`.

The load-bearing fact is the cross-module ordering seam. `CameraFollowSystem` runs in the **Update**
pass; `CullingSystem` and `MasterRenderSystem` (in the `rendering` module) run in the **Draw** pass,
which executes after Update each frame. So the camera the draw stack sees is the one follow just
wrote. But within Update, follow must run *after* whatever moves the target (physics, hierarchy,
AI) and *before* any Update-pass consumer of the camera — notably `CursorPositionSystem`, which is
deliberately sequenced right after it (`LoadLevelExampleGameScreen.cs:316–319`). Move follow ahead
of movement and the camera trails the target by one frame; the same one-frame lag reaches culling.

## Entities & lifecycle

A followed entity carries `CameraFollowTargetComponent` (with `IsActive`) + `TransformComponent`.
The `Camera` is a long-lived object owned by the screen, **not** an ECS entity — it is constructed in
`rendering` and handed to `CameraFollowSystem`, `CullingSystem`, `CursorPositionSystem`, and
`MasterRenderSystem` by reference. Per frame, in pipeline order:

1. **Target moves** — physics / hierarchy / game logic finalize the target's `Transform.Position`.
2. **Follow** — `CameraFollowSystem` reads the first active target, clamps to `Bounds`, constrains
   to `MaxDistance`, smooths, and writes `camera.Position`.
3. **Camera consumers (Draw pass)** — `CullingSystem.PreUpdate` reads `camera.VirtualScreenBounds`
   (derived from `Position`) to add/remove `VisibleComponent`; `MasterRenderSystem` reads
   `GetViewTransformationMatrix()` for the Main target's camera transform.

`camera.Position` has many potential writers across a frame; with this module registered it is the
single owner. A second system also writing `Position` makes it last-write-wins (a tug-of-war).

## Naming seam

The `Camera` class itself lives in the **`rendering`** module (`MonoDreams/rendering/Camera.cs`),
not here — it is a hard dependency of `MasterRenderSystem`, so the draw stack needs it whether or not
this module is installed. This module ships **only** the optional follow behavior. Confusingly, both
`Camera` and `CameraFollowTargetComponent` sit in the `MonoDreams.Component` namespace (the component
is not under a `Camera` sub-namespace), while `CameraFollowSystem` is `MonoDreams.System.Camera`.

## Invariants

Authoritative list in [`MonoDreams/camera/docs/premises.md`](../../MonoDreams/camera/docs/premises.md);
the ones this flow's ordering leans on:

- `CameraFollowSystem` runs after target movement and before camera consumers (culling, render).
- Following is optional — fixed-camera and multi-camera screens omit it and write `Position` directly.
- `Bounds` clamps the *target* before smoothing, not the resolved position after.
- Exactly one writer of `camera.Position` per frame; registering this system claims that role.

## Load-bearing quantities

- `DampingX` / `DampingY` — easing speed, per second; used as `1 - exp(-Damping * dT)`. Default `5.0`.
  Higher = snappier. Frame-rate independent by construction.
- `MaxDistanceX` / `MaxDistanceY` — per-axis cap on how far the camera target can sit from the
  current camera position, world units. Default `100.0`. Caps the *constrained target*, not speed.
- `Bounds` — optional world-space `Rectangle?` the desired target is clamped into; `null` = follow
  freely. Bounds smaller than the viewport are legal (camera barely moves).
- `snapThreshold` = `0.01` world units — below this the lerp snaps exactly onto the target.

## Failure modes

- **One-frame-late camera** — follow sequenced before the target's movement (or, hypothetically, the
  draw pass reordered before Update). The camera trails by a frame, and because `CullingSystem` reads
  the same stale `Position`, the cull frustum lags too: entities pop in/out at the screen edges. The
  defining cross-module hazard of this flow.
- **Tug-of-war on `Position`** — a second system also writes `camera.Position`; last write wins, so
  follow and the other writer fight and the camera stutters or ignores one of them.
- **Non-deterministic target** — two entities with `IsActive = true`; the "first" is whatever
  DefaultEcs enumerates, so the camera can snap to a different entity across runs with no obvious cause.
- **Snap-to-edge on bounded handoff** — a future change that clamps the *resolved* position instead of
  the target hard-caps each frame, so handing control to a bounded target from outside the bounds
  snaps to the edge instead of easing in (protected by `CameraFollowBoundsTests`).
