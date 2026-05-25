# foundation — overview

The required base every MonoDreams game stands on: ECS world plumbing, a screen controller, the hierarchy-and-transform stack, input abstraction with replay, and the structured logger. Nothing else in the engine can build without it.

## Purpose

Install this block first; every other block depends on it directly or transitively. It defines the spatial primitive (`TransformComponent`) every system in the engine reads, the hierarchy semantics (`ChildOfComponent` for lifecycle, `Parent` for matrix cascade), the per-frame heartbeat (`GameState` and the screen controller), the input scaffold (an abstract input handler with replay-from-JSON support), and the lock-protected `Logger` that every other block writes to. Without it there is no world, no transforms, no input, no log — every other block reaches into this one.

## What ships

### Components

- `TransformComponent` — local + cached world position/rotation/scale/matrix; `Delta`, `IsDirty`, `Parent`
- `ChildOfComponent` — structural parent link for cascade disposal (intentionally distinct from `Transform.Parent`)
- `EntityInfoComponent` — string identifier on every entity; games can wrap their own enum for convenience

### Systems

- `HierarchySystem` — propagates dirty flags, syncs `ChildOfComponent` → `Transform.Parent`, disposes orphans. Runs after movement, before any system that reads `WorldPosition`
- `TransformCommitSystem` — end-of-frame: commits the current `Position` to `LastPosition` so next frame's `Delta` is meaningful
- `AbstractInputHandlingSystem` — base class for game-specific input mapping; reads keyboard/gamepad/replay
- `InputReplaySystem` — reads `debug/input_replay.json` and feeds it into the input handler, optionally driving headless test runs

### Messages

- `PositionChangeMessage` — emitted when an entity's position changes (subscribe to react to movement)

### State / utilities

- `GameState` — frame total time, delta time, run state
- `EntityHierarchy` — world-scoped resource for hierarchy queries
- `Logger` — static, lock-protected; writes to `debug/monodreams_*.log` (honors `MONODREAMS_DEBUG_DIR`)
- `GameScreen` / `ScreenController` — the per-screen update/render loop owner

## Pipeline wiring

1. In `Game.Initialize()`, call `Logger.Initialize("debug")` early (writes to `debug/monodreams_*.log`). Set the `MONODREAMS_DEBUG_DIR` env var to redirect output (the test runner uses this for parallel isolation).
2. Construct a `ScreenController` and a starting `IGameScreen`. See `MonoDreams.Examples/Screens/` for full examples.
3. In your screen's update pipeline:
   - Input / gameplay systems write to `TransformComponent.LocalPosition` and `VelocityComponent` (via `physics`).
   - **`HierarchySystem` runs AFTER all movement and BEFORE any system that reads world-space transforms** (rendering, collision, camera follow, culling).
   - **`TransformCommitSystem` runs at end of frame** to flip the current position into the previous-position buffer — the next frame's `Delta` reads it.
4. Logger lifecycle: call `Logger.Shutdown()` before process exit to flush the buffered writer.

## Cross-block dependencies

This block has no dependencies — it is the root of the dependency graph. Everything else depends on it.

## Extension points

- **Custom input mapping.** Subclass `AbstractInputHandlingSystem` and override the input-state mapping. Implementers in `MonoDreams.Examples/` show keyboard, gamepad, and zone-based input mappings.
- **Custom screens.** Implement `IGameScreen` and register with `ScreenController` to swap update/render pipelines per screen.
- **Replay-driven tests.** Write an `InputReplayPlan` to `debug/input_replay.json` and run the game — `InputReplaySystem` feeds it into your input handler frame-by-frame. The headless test runner uses this for integration tests.

## See also

- [Premises](premises.md) — load-bearing invariants for this block (transforms, hierarchy, logger lifecycle)
- Related blocks: `rendering` (consumes `TransformComponent`), `collision` (consumes `Transform.Delta`), `physics` (writes to `Transform.LocalPosition` via velocity)
