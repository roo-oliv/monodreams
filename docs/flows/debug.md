---
flow: debug
covers:
  - MonoDreams/debug/**
sensitive: false
---

# Debug overlays & capture

This module observes the running game — and, in exactly one place, *drives* it. The observers
opt in at the screen's discretion; none is required, and each is a pure read of state another
module owns. `ColliderDebugSystem` walks every entity carrying `ColliderTagComponent` +
`TransformComponent`, reads the `BoxColliderComponent` / `ConvexColliderComponent` the
`collision` module wrote, and emits ephemeral mesh outlines (red = active, green = passive,
gray = disabled). `SpriteDebugSystem` reads the `DrawComponent` data that `SpritePrepSystem`
just populated (`Position`, `Origin`, `Size`, `SourceRectangle`) and emits a translucent
bounds rect, an origin marker, and an origin-to-center line. Both render *through the same
pipeline as everything else* — they create transient `DrawComponent { Type = Mesh }` entities
at a high `LayerDepth` and let `MasterRenderSystem` draw them; they never touch `SpriteBatch`.
`ScreenshotCaptureSystem` is the capture leg: it reads back the composited frame after
`FinalDrawSystem` and writes a PNG, either on a time interval (`Update`, opt-in) or on a chosen
frame (`CaptureNow`, deterministic). The source is the window backbuffer by default, or — when
`MONODREAMS_SCREENSHOT_TARGET` names a `RenderTargetID` — that pass's fixed-resolution target,
resolved from `MasterRenderSystem.RenderedTargetSink` so the file geometry stops following the
window. `KeepAwake` is the module's other non-system piece: an opt-in
(`MONODREAMS_KEEP_AWAKE=1`) macOS activity assertion a host holds for the run, so an unattended
one is not suspended by App Nap or display sleep. The whole module is off by default and adds
zero cost to a screen that registers none of it.

`PointerReplaySystem` is the one **driver** here, and it is the module's exception to
read-only: it consumes a `PointerReplayPlan` (`debug/pointer_replay.json` — move / click /
wheel / type / waitUntil / label) and writes the `cursor` module's `CursorInputComponent` each
frame, so a scripted mouse drives the game's real picking / focus / UI path. It writes exactly
one component (plus the cursor's own transform, through `Cursor.ApplyPose`) and calls into no
game system; the screen stands the hardware path down (`SkipHardwareRead` + `SkipDerivation`)
so there is one writer, not two.

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

`ScreenshotCaptureSystem` owns no entities. It reads its source — the backbuffer, or the named
render target last published by a pass — into a reused `_pixelBuffer` + `_stagingTexture`,
encodes a PNG, and writes it to `_outputDirectory`. The async `Update` path fires the write on
`PlatformServices.RunBackground` (best-effort); `CaptureNow` writes synchronously and returns
the non-blank verdict before returning. In target mode a tick whose target no pass has drawn
writes nothing at all (one warning, no counter, no fallback to the window).

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
- Target capture subscribes to the render socket only when a target was named and unsubscribes
  on `Dispose`; it takes the first target published for that id since its last read, and
  refuses (rather than falling back to the window) when nothing has published one.
- `KeepAwake` is opt-in, macOS-only and never throws — an unavailable Objective-C runtime is a
  logged no-op, not a failed run.
- All debug output honors `MONODREAMS_DEBUG_DIR`, falling back to `<BaseDirectory>/debug` — the
  load-bearing case is parallel test isolation.
- `PointerReplaySystem` injects into the real `CursorInputComponent` (never calls a handler),
  addresses **authoring space** and counts **frames**, gates stages on observables with a
  timeout, drains into a single `requestExit`, and is file-gated + single-owner (including the
  `Logger.LineSink` tap it must release on dispose).

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

- **A debug system mutates the simulation** — the cardinal sin for the OBSERVERS. These systems
  are read-only; they `Get` collider/draw/transform data and only ever create their own throwaway
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
- **A scripted pointer that only half-stands-down the hardware path** — with
  `SkipHardwareRead` set but `SkipDerivation` left false, `CursorPositionSystem` recomputes the
  derived positions from the injected `ScreenPosition` and marks the pointer
  `OutsideViewport`, so every scripted click is silently discarded as "over chrome". The
  symptom (nothing happens, no error) looks like a game bug, not a wiring bug.
- **Two pointer channels in one run** — the pointer replay and the editor-op channel both stamp
  the same cursor entity; last writer wins on both position and edges, producing clicks that
  land nowhere. Run one per session.
- **A pointer script that races the game** — a `click` scheduled before the frame that laid the
  target out hits empty space. That is what `waitUntil` is for; a plan without stage gating is
  a flaky test waiting to happen.
