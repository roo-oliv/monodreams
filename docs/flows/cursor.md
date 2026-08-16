---
flow: cursor
covers:
  - MonoDreams/cursor/**
sensitive: false
---

# Cursor frame

Each frame the cursor walks a fixed three-stage path: **poll → project → paint**, on a single
entity composed by `Cursor.Create` (textured) or `Cursor.CreateMesh` (mesh). `CursorInputSystem`
polls `Mouse.GetState()` and writes the raw `ScreenPosition`, the per-frame `Delta`, and
button/scroll edge state into `CursorInputComponent`; it does **not** touch world coords.
`CursorPositionSystem` then turns that screen position into game space in two hops —
`ViewportManager.MapMouse` undoes letterbox/pillarbox scaling to get
AUTHORING (layout) coords — the virtual resolution in a single-space game — and
`camera.VirtualScreenToWorld` inverts the camera view matrix to
get world coords — populating `VirtualPosition` and `WorldPosition` and writing
`TransformComponent.Position` in the space the cursor's render target expects. Finally
`CursorDrawPrepSystem` (textured path only) reads `CursorControllerComponent.Type` and copies the
matching `Texture2D` out of `CursorTexturesComponent` into the cursor's `DrawComponent`, then
copies `transform.Position` into `draw.Position`. The cursor seeds `DrawComponent.LayerDepth = 1.0`
and defaults to `RenderTargetID.HUD`, so `MasterRenderSystem` draws it last, above world and UI.

Note the **hover-type decision lives outside this flow.** Nothing in the cursor module picks a
`CursorType` — `Type` is a *write-only* field as far as cursor systems are concerned; they only
read it. The choice ("am I over a clickable?") is owned by a consumer: the `ui` module's
`CursorHoverSystem`, which derives it from the one pointer pick — but only for the **mesh** path,
since it requires a `CursorMeshLibraryComponent` to swap silhouettes. The **textured** path has no
shipped consumer today (Examples' menu keeps the arrow while hovering a button); a game that wants
a hand cursor there writes the `Type` itself from `PointerPickComponent`. The cursor flow's job is
only to position the entity and reflect `Type` into a texture.

## Entities & lifecycle

One cursor entity, two creators (never hand-rolled — see premises):

- **Textured** (`Cursor.Create`) — `CursorControllerComponent` + `CursorInputComponent` +
  `TransformComponent` + `CursorTexturesComponent` + a `Sprite` `DrawComponent`. Positioned by
  `CursorInputSystem` → `CursorPositionSystem`, painted by `CursorDrawPrepSystem`.
- **Mesh** (`Cursor.CreateMesh`) — same controller/input/transform + a `Mesh` `DrawComponent` +
  `VisibleComponent`, and **no** `CursorTexturesComponent`. Positioned by the same input→position
  pair, but drawn by the screen's `MeshPrepSystem`, not `CursorDrawPrepSystem`.

Per frame, in pipeline order (reference: `LevelSelectionScreen.cs` / `LoadLevelExampleGameScreen.cs`):

1. **Poll** — `CursorInputSystem` (early, with the rest of input): raw `ScreenPosition` + edge state.
2. *(camera moves — `CameraFollowSystem` or game camera control)*
3. **Project** — `CursorPositionSystem` (**after** the camera): screen → virtual → world; writes
   `VirtualPosition`, `WorldPosition`, and target-dependent `Transform.Position`.
4. *(game/ui hover systems read `WorldPosition`, set `Type`)*
5. **Paint** — `CursorDrawPrepSystem` (textured) fills `DrawComponent.Texture`; then the render
   module (`SpritePrepSystem`/`MeshPrepSystem` → `MasterRenderSystem`) draws on top.

## Invariants

Authoritative list in [`MonoDreams/cursor/docs/premises.md`](../../MonoDreams/cursor/docs/premises.md);
the ones this flow's ordering and coordinate handling lean on:

- Stage order is strict: input → position → draw-prep. Reorder and you paint at last frame's
  position or project last frame's mouse.
- `CursorPositionSystem` runs **after** the camera's final move for the frame; otherwise
  `WorldPosition` (and Main-target rendering) lags one frame during scroll/shake.
- World-space hit-tests read `CursorInputComponent.WorldPosition`, never `Transform.Position` —
  on the HUD target the transform holds virtual-screen coords, not world.
- HUD target + `LayerDepth = 1.0` keep the cursor topmost and free of culling/camera transforms.

## Load-bearing quantities

- `ScreenPosition` — raw physical pixels from `Mouse.GetState()`; input space, no scaling.
- `VirtualPosition` — virtual-resolution coords (0,0..`VirtualWidth`,`VirtualHeight`) after
  letterbox/pillarbox removal. **Null when the mouse is in the letterbox bars** — `CursorPositionSystem`
  then keeps last frame's position and skips all writes (no clamp, no hide).
- `WorldPosition` — `VirtualPosition` through the inverse camera view matrix; world units. The one
  value game logic should read for hit-testing.
- `Transform.Position` — `(HUD ? VirtualPosition : WorldPosition) + controller.HotSpot`. The space
  is chosen by `DrawComponent.Target`; the hot-spot offset aligns the texture tip with the click point.
- `DrawComponent.LayerDepth = 1.0` — top of the depth range; the cursor draws over everything on
  its target. `DrawComponent.Size` defaults to `32×32` if left zero.

## Failure modes

- **World hit-test reads `Transform.Position`** — on the default HUD cursor that is virtual-screen
  space, so every world-space pick (grab item under cursor) lands in the wrong place. The single
  most common cursor bug; the fix is always "read `WorldPosition`".
- **`CursorPositionSystem` before the camera moved** — click/hover offset that only appears while
  the camera is panning or shaking; invisible in a static scene, so it survives casual testing.
- **Draw-prep before position** — cursor paints one frame stale; visible as the texture lagging the
  motion under fast mouse movement.
- **Cursor under other layers** — created on Main or UI instead of HUD, or `LayerDepth` lowered;
  the cursor sinks beneath world sprites or HUD overlays (and on Main is also culled/zoomed).
- **Stale type / wrong texture** — a hand-rolled cursor missing `CursorTexturesComponent` (textured)
  or `VisibleComponent` (mesh) drops out of its draw-prep query: `Type` becomes a dead field and the
  appearance never changes on hover. Use the factory.
- **Lost in the letterbox** — with the mouse in the bars, the cursor freezes at its last position
  (an open question in the premises, not a settled hide behavior); code that assumes continuous
  tracking off-viewport will read stale coords.
