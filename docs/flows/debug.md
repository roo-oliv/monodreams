---
flow: debug
covers:
  - MonoDreams/debug/**
sensitive: false
---

# Debug overlays & capture

This module observes the running game and never changes it. Three systems opt in at the
screen's discretion — none is required, and each one is a pure read of state another module
owns. `ColliderDebugSystem` walks every entity carrying `ColliderTagComponent` +
`TransformComponent`, reads the `BoxColliderComponent` / `ConvexColliderComponent` the
`collision` module wrote, and emits ephemeral mesh outlines (red = active, green = passive,
gray = disabled). `SpriteDebugSystem` reads the `DrawComponent` data that `SpritePrepSystem`
just populated (`Position`, `Origin`, `Size`, `SourceRectangle`) and emits a translucent
bounds rect, an origin marker, and an origin-to-center line. Both render *through the same
pipeline as everything else* — they create transient `DrawComponent { Type = Mesh }` entities
at a high `LayerDepth` and let `MasterRenderSystem` draw them; they never touch `SpriteBatch`.
`ScreenshotCaptureSystem` is the capture leg: it reads back the composited backbuffer after
`FinalDrawSystem` and writes a PNG, either on a time interval (`Update`, opt-in) or on a chosen
frame (`CaptureNow`, deterministic). The whole module is off by default and adds zero cost to a
screen that registers none of it.

## Entities & lifecycle

The overlay systems own a short-lived set of *debug entities*, recreated every frame:

1. **Dispose last frame's** — at the top of `Update`, both systems dispose the entities they
   created last frame and clear their `_debugEntities` list. This runs *unconditionally*, even
   when disabled, so toggling off cleans up.
2. **Gate** — return early unless both the static `Enabled` flag and the instance `IsEnabled`
   property are true (two toggles; see Open questions in the premises).
3. **Read & emit** — iterate the system's `EntitySet` (collider entities, or sprite entities),
   read the source component data, and `CreateEntity()` one mesh `DrawComponent` per outline,
   tagged `VisibleComponent` for the Main target.
4. **Render** — `MasterRenderSystem`, running later the same frame, draws them. Next frame
   returns to step 1.

`ScreenshotCaptureSystem` owns no entities. It reads the backbuffer into a reused
`_pixelBuffer` + `_stagingTexture`, encodes a PNG, and writes it to `_outputDirectory`. The
async `Update` path fires the write on `PlatformServices.RunBackground` (best-effort);
`CaptureNow` writes synchronously and returns the non-blank verdict before returning.

## Invariants

Authoritative list in [`MonoDreams/debug/docs/premises.md`](../../MonoDreams/debug/docs/premises.md); the ones this flow's correctness leans on:

- Nothing requires this module — systems are registered only if a screen asks, and each honors
  static `Enabled` + instance `IsEnabled`.
- Overlays draw via the unified `DrawComponent` mesh path, not a parallel `SpriteBatch` — they
  ride `MasterRenderSystem`.
- Overlays must be prep'd *after* `SpritePrepSystem` (so bounds reflect this frame's draw data)
  and *before* `MasterRenderSystem` (so the transient entities exist when the renderer iterates).
- `ScreenshotCaptureSystem.Update` is gated by `IsEnabled` (set from `"screenshots": true` in
  `input_replay.json`); `CaptureNow` bypasses both `IsEnabled` and the interval gate.
- All debug output honors `MONODREAMS_DEBUG_DIR`, falling back to `<BaseDirectory>/debug` — the
  load-bearing case is parallel test isolation.

## Load-bearing quantities

- Capture interval — seconds; `2` in the reference screen, `0` in headless Demos (every frame
  via `CaptureNow`). The async path also self-throttles: it skips while `_pendingSave` is true.
- Overlay `LayerDepth` — `1.0` for colliders, `0.98` for sprite bounds (bounds < line < circle,
  +0.0005 / +0.001 steps) — high so overlays sort on top of the scene.
- Overlay coordinate space — **world units, on the Main target** (camera transform applied), the
  same space colliders and sprites live in. Collider outlines use `Transform.WorldPosition +
  Bounds`; sprite bounds reconstruct SpriteBatch placement as `position - scaledOrigin` where
  `scaledOrigin = origin * destSize / sourceSize`. A mismatch here is purely cosmetic but defeats
  the overlay's whole purpose (diagnosing offset).

## Failure modes

- **A debug system mutates the simulation** — the cardinal sin. These systems are read-only
  observers; they `Get` collider/draw/transform data and only ever create their own throwaway
  entities. A debug system that wrote back to a collider, velocity, or the inspected
  `DrawComponent` would make bugs appear or vanish depending on whether debugging was on — the
  worst kind of heisenbug. Their only world mutation is creating/disposing entities they alone own.
- **Overlay prep'd on the wrong side of the render** — registered after `MasterRenderSystem`,
  overlays render one frame late; registered before `SpritePrepSystem`, sprite bounds visualize
  stale `DrawComponent` data and lag during camera motion or animation.
- **Capture perturbs timing** — `Update`'s synchronous backbuffer read-back + PNG encode stalls
  the render thread; left on in a normal run it taxes every frame and floods `debug/`. This is
  why it is default-off and interval-gated, and why the headless deterministic path uses
  `CaptureNow` on a chosen frame rather than the interval.
- **Hardcoded `debug/` instead of `MONODREAMS_DEBUG_DIR`** — a new debug writer that ignores the
  override makes concurrent `GameTestRunner` runs clobber each other's PNGs/logs and fail with
  file-locking errors or cross-attributed output.
- **Overlay coordinate mismatch** — wrong origin scaling or reading `WorldPosition` before
  `HierarchySystem` runs draws outlines offset from what they describe; the overlay then lies
  about the very offset it exists to expose.
