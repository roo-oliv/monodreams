---
flow: foundation
covers:
  - MonoDreams/foundation/**
sensitive: true
---

# Foundation: the per-frame heartbeat every module stands on

Foundation owns the spine every other module hangs off of: the screen lifecycle that drives
each frame, the transform/hierarchy stack that produces the world-space coordinates rendering,
collision, camera, and culling all read, and the input + logging scaffold. `ScreenController`
is the heartbeat — `Update(GameTime)` swaps in a pending screen (disposing the old one and
calling `Load` on the new), advances a single `GameState` (`Update` shifts `GameTime` to
`(current, last)`), pings `Logger.UpdateGameTime`, then runs the current screen's `UpdateSystem`;
`Draw` runs the `DrawSystem` against the same `GameState`. The screen *is* the pipeline
assembler (tenet 2) — foundation supplies no ordering of its own, so the load-bearing facts
here are about *where* foundation's systems must sit in a screen-owned pipeline relative to
everything downstream. The most consequential of these is the transform composition:
`TransformComponent` holds a *local* position/rotation/scale plus a lazily-cached `WorldMatrix`;
`HierarchySystem` walks `ChildOfComponent` → `TransformComponent.Parent` and propagates dirty
flags so that any later reader of `WorldPosition`/`WorldRotation`/`WorldScale` gets a
parent-composed value rather than a stale one (tenet 3).

## Entities & lifecycle

An entity carries `TransformComponent` (spatial) and optionally `ChildOfComponent` (structural
parent). Within one screen-owned frame:

1. **Movement** — game logic + physics write `TransformComponent.Position` (or the fluent
   `Translate*`/`Set*` helpers); every setter calls `SetDirty()`, invalidating the cached
   `WorldMatrix`. This is *local* position — a child's `Position` is relative to its parent.
2. **Hierarchy** — `HierarchySystem.Update` runs four steps in order: `DisposeOrphans`
   (cascade-dispose any `ChildOfComponent` whose `Parent` is no longer alive), `EntityHierarchy.Rebuild`
   (refresh the world-scoped parent/children lookup resource), `SyncTransformParents` (point each
   child's `TransformComponent.Parent` at its parent's transform — the matrix link), then
   `PropagateDirtyFlags` (recursively `SetDirty` every descendant of a dirtied parent). After this,
   `WorldMatrix` reads are fresh for the frame.
3. **World-space readers** — rendering, culling, `CameraFollowSystem`, and collision read
   `WorldPosition`/`WorldMatrix`. They must run *after* step 2.
4. **Commit** — `TransformCommitSystem` (an `AComponentSystem`, runs over every
   `TransformComponent`) calls `CommitPosition()`, copying `Position` into `LastPosition`. This
   makes the *next* frame's `Delta`/`HasMoved` meaningful; it does not dirty the matrix.

Input runs at the head of the pipeline: either `AKeyboardInputHandlingSystem` (the OR-aggregating
keyboard reader) or `InputReplaySystem` drives each `AInputState.Update(down, state)` exactly once
per frame, which derives the `JustPressed`/`JustReleased` edges from the prior committed press
state. `Logger` is a static lock-protected singleton living outside the world.

## Invariants

Authoritative list in [`MonoDreams/foundation/docs/premises.md`](../../MonoDreams/foundation/docs/premises.md);
the ones this flow's ordering leans on:

- `HierarchySystem` runs after movement and before any `WorldPosition`/`WorldMatrix` reader.
  Reorder it after rendering/collision/camera and they read last frame's world transform with no error.
- `TransformComponent.Delta`/`HasMoved` are valid only after the *previous* frame's
  `TransformCommitSystem`. Drop Commit and swept collision (which reads `Delta`) goes through walls silently.
- `ChildOfComponent` (lifecycle/disposal) and `TransformComponent.Parent` (matrix cascade) are two
  separate links; `HierarchySystem` syncs the second from the first. Reading only one misses the other's behavior.
- `Logger.Initialize` must run before the first write or the write silently no-ops;
  `Shutdown` flushes. `MONODREAMS_DEBUG_DIR` redirects all debug output for test isolation.
- Engine source never touches `File`/`Environment`/`Console` directly — all of it routes through
  `PlatformServices.Current` so the same source runs on web (see the platform premises).
- `WindowFit` is opt-in and sits outside the frame entirely (a head-constructor call, no system, no
  component). It must run *after* `Logger.Initialize` or its single boot line — the feature's only
  observable — silently no-ops. Everything it touches is in points on macOS DesktopGL; there is no
  Retina conversion anywhere in that path.

## Load-bearing quantities

Foundation moves coordinates and timing rather than scalar magnitudes, so it carries few capped values:

- `TransformComponent.Delta` = `Position - LastPosition` — a per-frame position delta in world
  units, meaningful only on the frame *after* Commit (see invariants). Consumed by collision's swept tests.
- `GameState.Time` = `GameTime.current.ElapsedGameTime` in seconds — the frame `dT` physics multiplies
  velocity by; `TotalTime` is the monotonic clock `InputReplaySystem` schedules commands against.

## Failure modes

- **Stale world transform** — `HierarchySystem` placed after a `WorldPosition` reader (or omitted).
  A child renders/collides at last frame's parent-composed position; a follow-camera tracks a stale
  target. Silent, and the dirtied-cache design means it looks like a one-frame lag, not a crash. Highest-impact bug here because every downstream module trusts this ordering.
- **Through-the-wall via missing Commit** — `TransformCommitSystem` dropped from the pipeline tail;
  `Delta` stays empty, swept collision never sees the motion, dynamic contacts tunnel. The dev hunts in `collision` for hours before finding the missing Commit upstream (handoff seam: see `docs/flows/physics.md` / collision).
- **Half-wired hierarchy** — a visually-attached child given `TransformComponent.Parent` but no
  `ChildOfComponent` survives its parent's disposal and orphan-renders at world origin; the reverse (only `ChildOfComponent`) misses the matrix cascade and renders un-parented.
- **Lost logs / clobbered test runs** — a system logs before `Logger.Initialize`, or a parallel
  test forgets `MONODREAMS_DEBUG_DIR`; output silently drops or two runs overwrite one log file.
- **Game renders offscreen** — a head pins `PreferredBackBufferWidth/Height` to its render
  resolution (or re-pins it *after* `WindowFit.Apply`). macOS does not clamp a fixed window, so on
  any display smaller than the render resolution the bottom strip — menus, Start buttons, HUD —
  draws below the physical screen. No crash, no log, and players do not report it; they quit.

> Name note: the input base class is `AKeyboardInputHandlingSystem`, but its file is
> `System/Input/AbstractInputHandlingSystem.cs` — file name and class name differ.
