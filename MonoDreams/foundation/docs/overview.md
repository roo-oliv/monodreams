# foundation — overview

The required base every MonoDreams game stands on: ECS world plumbing, a screen controller, the hierarchy-and-transform stack, input abstraction with replay, and the structured logger. Nothing else in the engine can build without it.

## Purpose

Install this module first; every other module depends on it directly or transitively. It defines the spatial primitive (`TransformComponent`) every system in the engine reads, the hierarchy semantics (`ChildOfComponent` for lifecycle, `Parent` for matrix cascade), the per-frame heartbeat (`GameState` and the screen controller), the input scaffold (an abstract input handler with replay-from-JSON support), and the lock-protected `Logger` that every other module writes to. Without it there is no world, no transforms, no input, no log — every other module reaches into this one.

## What ships

### Components

- `TransformComponent` — local + cached world position/rotation/scale/matrix; `Delta`, `IsDirty`, `Parent`
- `ChildOfComponent` — structural parent link for cascade disposal (intentionally distinct from `Transform.Parent`)
- `EntityInfoComponent` — string identifier on every entity; games can wrap their own enum for convenience

### Systems

- `HierarchySystem` — propagates dirty flags, syncs `ChildOfComponent` → `Transform.Parent`, disposes orphans. Runs after movement, before any system that reads `WorldPosition`
- `TransformCommitSystem` — end-of-frame: commits the current `Position` to `LastPosition` so next frame's `Delta` is meaningful
- `AKeyboardInputHandlingSystem` (in `System/Input/AbstractInputHandlingSystem.cs` — the file name differs from the class name) — base class for game-specific input mapping; reads keyboard/gamepad/replay
- `InputReplaySystem` — reads `debug/input_replay.json` and feeds it into the input handler, optionally driving headless test runs

### Messages

- `PositionChangeMessage` — emitted when an entity's position changes (subscribe to react to movement)

### State / utilities

- `GameState` — frame total time, delta time, run state
- `EntityHierarchy` — world-scoped resource for hierarchy queries
- `Logger` — static, lock-protected; writes to `debug/monodreams_*.log` (honors `MONODREAMS_DEBUG_DIR`)
- `GameScreen` / `ScreenController` — the per-screen update/render loop owner
- `PlatformServices` / `IPlatformServices` — the filesystem / environment / console portability seam (desktop default; a web head swaps its own in)
- `WindowFit` — **opt-in** desktop windowing helper: opens the largest aspect-correct window that fits the display's *usable* area (menu bar / dock / taskbar excluded), snapped to multiples of 16 and capped at the render resolution. `MONODREAMS_WINDOW=WxH` forces an exact size. Nothing in the engine calls it; a game that doesn't call it is unchanged
- `SdlNative` — best-effort access to SDL exports MonoGame never bound (`SDL_GetDisplayUsableBounds`, `SDL_HideWindow`), on the SDL image DesktopGL already loaded. The engine's single owner of SDL library resolution

## Pipeline wiring

1. In `Game.Initialize()`, call `Logger.Initialize("debug")` early (writes to `debug/monodreams_*.log`). Set the `MONODREAMS_DEBUG_DIR` env var to redirect output (the test runner uses this for parallel isolation).
2. Construct a `ScreenController` and a starting `IGameScreen`. See `MonoDreams.Examples/Screens/` for full examples.
3. In your screen's update pipeline:
   - Input / gameplay systems write to `TransformComponent.LocalPosition` and `VelocityComponent` (via `physics`).
   - **`HierarchySystem` runs AFTER all movement and BEFORE any system that reads world-space transforms** (rendering, collision, camera follow, culling).
   - **`TransformCommitSystem` runs at end of frame** to flip the current position into the previous-position buffer — the next frame's `Delta` reads it.
4. Logger lifecycle: call `Logger.Shutdown()` before process exit to flush the buffered writer.
5. **Desktop window sizing (recommended).** In your head's constructor, replace
   `_graphics.PreferredBackBufferWidth/Height = …` with
   `WindowFit.Apply(_graphics, VirtualWidth, VirtualHeight, Window)` — and call
   `Logger.Initialize` *before* it, so its one boot line (render / display / usable / window /
   mode) is not dropped. A fixed window larger than the player's display is **not** clamped by
   macOS, so pinning the backbuffer to the render resolution silently renders the bottom of the
   game offscreen on any smaller laptop. Keep the call inside the desktop branch of the platform
   gate: on web the host page owns the canvas size. Passing `Window` also turns
   `AllowUserResizing` on (except under `MONODREAMS_WINDOW`, which asked for an exact size), so if
   your screen scales through a `ViewportManager`, subscribe to `Window.ClientSizeChanged` and
   re-feed it the new device size — or omit the `Window` argument to keep the window fixed.

## Cross-module dependencies

This module has no dependencies — it is the root of the dependency graph. Everything else depends on it.

## Extension points

- **Custom input mapping.** Subclass `AKeyboardInputHandlingSystem` and override the input-state mapping. Implementers in `MonoDreams.Examples/` show keyboard, gamepad, and zone-based input mappings.
- **Custom screens.** Implement `IGameScreen` and register with `ScreenController` to swap update/render pipelines per screen.
- **Replay-driven tests.** Write an `InputReplayPlan` to `debug/input_replay.json` and run the game — `InputReplaySystem` feeds it into your input handler frame-by-frame. The headless test runner uses this for integration tests.
- **Window policy.** `WindowFit.Compute` is the pure decision (mode + size) and `WindowFit.Fit` the pure geometry, both usable without a graphics device — a head that wants a different policy (fullscreen, remembered size, a per-monitor rule) can reuse them and apply the result itself instead of calling `WindowFit.Apply`.
- **Unbound SDL calls.** `SdlNative.TryInvoke<TDelegate>(export, call)` resolves an export on the SDL image DesktopGL already loaded and hands it over as a delegate. Always best-effort — it returns `false` rather than throwing when SDL or the export is absent, so every caller must carry a fallback.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (transforms, hierarchy, logger lifecycle)
- Related modules: `rendering` (consumes `TransformComponent`), `collision` (consumes `Transform.Delta`), `physics` (writes to `Transform.LocalPosition` via velocity)
