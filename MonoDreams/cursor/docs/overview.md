# cursor — overview

A textured, hover-aware cursor pipeline: poll mouse input, convert screen coords to world coords via the camera, paint the cursor texture for the current `CursorType` (default / pointer / grab / etc.). Install for any game that needs a custom cursor — point-and-click adventures, RTS-style selection, tooltip-driven menus.

## Purpose

The hardware cursor is a single global mouse state, but a game often wants a custom-textured cursor that changes appearance based on context (idle vs hovering a clickable vs dragging) and whose world position is needed by game systems for hit-testing. This block wraps that into three small systems: `CursorInputSystem` reads raw mouse state, `CursorPositionSystem` projects screen → virtual → world (driven by the camera), and `CursorDrawPrepSystem` paints the right texture into `DrawComponent`. The `Cursor.Create` factory composes a cursor entity from four components in one call; game systems read `CursorInputComponent.WorldPosition` for hit-tests without caring what render target the cursor draws on.

## What ships

### Components

- `CursorControllerComponent` — current cursor `Type`, `IsVisible`, `HotSpot`
- `CursorInputComponent` — raw input state: `ScreenPosition`, `WorldPosition`, button presses, delta
- `CursorTexturesComponent` — dictionary mapping `CursorType` → `Texture2D`

### Systems

- `CursorInputSystem` — runs early; polls `Mouse.GetState()` and writes `ScreenPosition` + button/delta state
- `CursorPositionSystem` — runs after the camera has been updated; converts screen → world (HUD target stays in screen-space, Main target converts to world) and writes `TransformComponent.Position` and `CursorInputComponent.WorldPosition`
- `CursorDrawPrepSystem` — runs after position is finalized; reads the active `CursorType` and populates `DrawComponent` with the texture from `CursorTexturesComponent`

### Factory

- `Cursor.Create(world, textures, renderTarget)` — canonical entry point; composes the cursor entity with all four components (controller, input, transform, textures) plus a seeded `DrawComponent`. Defaults to `RenderTargetID.HUD`

## Pipeline wiring

1. **Load cursor textures** in `Load()` — one `Texture2D` per `CursorType` you use.
2. **Create the cursor entity** in your screen setup:
   ```csharp
   var cursor = MonoDreams.Cursor.Cursor.Create(world, textures, RenderTargetID.HUD);
   ```
   The cursor renders on `RenderTargetID.HUD` by default so it sits above the game world and UI (Main → UI → HUD is the render target order in `MasterRenderSystem`).
3. **Pipeline order** in the screen's update path (order matters):
   - `CursorInputSystem` early (reads raw mouse state).
   - **After the camera updates** (after `CameraFollowSystem` from `camera`, or your own camera-control system): `CursorPositionSystem`. It uses `camera.VirtualScreenToWorld()` to project, so a stale camera produces a one-frame lag on hover/click.
   - `CursorDrawPrepSystem` after position is finalized.
4. **In game interaction systems**, read `CursorInputComponent.WorldPosition` for world-space hit-testing. Don't read `TransformComponent.Position` for that purpose — on the HUD target it holds screen-space coordinates, not world.

`MonoDreams.Examples/Screens/LevelSelectionScreen.cs` is the canonical reference.

## Cross-block dependencies

- `foundation` — uses `TransformComponent` to position the cursor entity.
- `rendering` — the cursor renders through the standard `DrawComponent` path; the `Camera` (in `rendering`) is consulted to project screen coordinates to world.

## Extension points

- **New cursor types.** Extend the `CursorType` enum, add a texture to `CursorTexturesComponent` for that type, and have a game-side hover/interaction system flip `CursorControllerComponent.Type` based on context.
- **Custom render target.** Pass a different `RenderTargetID` to `Cursor.Create` (Main for world-attached cursors, UI to put the cursor below HUD overlays). HUD is the recommended default for system-cursor behavior.
- **Hit-test against UI.** Game-side: read `CursorInputComponent.WorldPosition` and intersect against `LayoutNodeComponent.ComputedBounds` from `ui` (or `BoxColliderComponent` for world-space clickable entities).

## See also

- [Premises](premises.md) — load-bearing invariants (three-stage pipeline order, camera-must-update-first, HUD render-target default, target-dependent transform position semantics, factory as canonical entry)
- Related blocks: `rendering` (cursor draws through it; provides `Camera`), `camera` (must update before `CursorPositionSystem` for accurate world coords during camera motion), `ui` (game-side interaction systems combine cursor world-position with UI bounds for hit-tests)
