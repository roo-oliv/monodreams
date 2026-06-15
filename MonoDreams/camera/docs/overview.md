# camera — overview

Optional auto-follow behavior for a `Camera` — tag the entity you want tracked with `CameraFollowTargetComponent`, register `CameraFollowSystem`, and the camera will write its position from the target each frame. Skip this block for fixed-camera or manually-driven cameras; the draw stack works fine without it.

## Purpose

This block is the small, optional layer on top of the `Camera` class that handles the one common case: "follow the player." Everything else about cameras — virtual resolution, zoom, position, the view matrix — lives in `rendering` (because `MasterRenderSystem` needs `Camera` every frame, making it a hard dependency of the draw stack). The split is deliberate: a fixed-camera puzzle game, a CCTV-style multi-camera setup, or a cutscene-driven cinematic doesn't need follow plumbing it doesn't use. Games that *do* want auto-follow get a one-line registration; games that don't simply omit this block and write to `camera.Position` directly.

## What ships

### Components

- `CameraFollowTargetComponent` — tag with an `IsActive` flag; place on the entity (typically the player) the camera should follow. An optional `Bounds` rectangle clamps the resolved camera position so the view never scrolls past those edges (e.g. to keep the camera inside the level) — leave it null to follow freely

### Systems

- `CameraFollowSystem(world, camera)` — runs once per frame in the update pipeline; picks the first `IsActive` target entity and writes `camera.Position` from its `TransformComponent.Position`

That's the entire block. The `Camera` class itself ships in `rendering`.

## Pipeline wiring

1. Place `CameraFollowTargetComponent { IsActive = true }` on the entity you want followed.
2. Register `CameraFollowSystem(world, camera)` in your update pipeline:
   - **After** any system that moves the followed entity (physics, AI, scripted motion) — otherwise the camera tracks last frame's position.
   - **Before** the prep / cull / render block — otherwise culling uses last frame's view bounds and entities pop at the edges.

For manual camera control (cutscenes, fixed-camera screens), don't register this system — just write to `camera.Position` / `camera.Zoom` / `camera.Rotation` directly from your own system.

To swap the followed entity at runtime, toggle `IsActive` on the old and new targets. There is no priority field today; if multiple entities have `IsActive = true`, the first one DefaultEcs enumerates wins (non-deterministic).

## Cross-block dependencies

- `rendering` — depends on `rendering` because the `Camera` class itself ships there (it's a hard dep of `MasterRenderSystem`). This is a notable design quirk: installing the `camera` block does *not* introduce the `Camera` class — it only adds the *optional follow behavior* on top of one that already exists. Games that want manual camera control use the `Camera` from `rendering` without ever installing this block.

## Extension points

- **Camera shake / look-ahead / dead-zones.** Compose new systems that read `CameraFollowTargetComponent` (or another marker of your own) and write to the same `Camera`. Run them in order; the framework supports last-write-wins per frame. This block's demo (`MonoDreams/camera/demo/CameraDemoScreen.cs`) demonstrates it: `CameraHitSystem` runs last and layers a small, decaying jolt on top of the camera transform when the dot enters one of two flanking "hit" squares — a positional shake (`Camera.Position`) from the right square, a rotational wobble (`Camera.Rotation`) from the left — reconstructing the clean base each frame (subtracting its own prior offset/rotation) so the jolt never bleeds into the follow smoothing.
- **Multiple cameras.** Construct multiple `Camera` instances and one `CameraFollowSystem` per camera. Split-screen hasn't been exercised but should work — the system is constructed with a specific `Camera` reference.
- **Priority for multiple active targets.** Today's first-active-wins iteration is the framework's current limit; a priority field is on the aspirational direction list (see premises).

## See also

- [Premises](premises.md) — load-bearing invariants for this block (follow is optional, runs after movement and before rendering, the `Camera` lives elsewhere)
- Related blocks: `rendering` (owns the `Camera` class), `cursor` (depends on the camera having been updated before its position system runs — wire `CursorPositionSystem` after `CameraFollowSystem`)
