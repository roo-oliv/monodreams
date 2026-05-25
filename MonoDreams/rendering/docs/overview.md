# rendering — overview

The sprite-first draw stack: one `DrawComponent` per entity, three render targets (Main / UI / HUD), explicit culling and Y-sort stages, and a single renderer (`MasterRenderSystem`) that owns every `SpriteBatch` call. Install this for any game that draws anything.

## Purpose

This block defines how things appear on screen. It owns the entire draw path — `SpriteBatch` is hidden behind `MasterRenderSystem`, batch ordering and render-target switches are centralized, and per-entity visibility is a derived tag (`VisibleComponent`) maintained by `CullingSystem`. The block also ships the `Camera` class itself, because the draw stack reads `camera.ViewMatrix` every frame — making `Camera` a hard dependency of rendering rather than an optional add-on. Without this block, nothing renders; everything else that draws (mesh shapes, text, cursor, dialogue UI) extends this stack rather than parallels it.

## What ships

### Components

- `DrawComponent` (class) — the unified render component; one per renderable entity. `Type` discriminates `Sprite`/`Text`/`NinePatch`/`Mesh`
- `DrawElement` / `DrawElementType` — enum + helpers for the draw-type discriminator
- `SpriteInfoComponent` (struct) — texture + source rect + color + layer depth for sprite-typed draws
- `NinePatchInfo` — source data for nine-patch sprite drawing
- `VisibleComponent` — empty tag added/removed by `CullingSystem` for Main-target entities; UI/HUD set it themselves
- `RenderTargetID` — enum: `Main` (world, camera-transformed), `UI` (screen-space), `HUD` (screen-space, on top)

### Systems

- `SpritePrepSystem` — copies `SpriteInfoComponent` + `TransformComponent` into `DrawComponent` each frame
- `CullingSystem` — adds/removes `VisibleComponent` based on camera view bounds (Main target only)
- `YSortSystem` — writes layer-depth offset for back-to-front Y-sorted layers; parent-child tiebreaker via tiny epsilon
- `MasterRenderSystem` — the sole renderer; switches between targets, batches, layer-sorts, and draws all four `DrawElementType`s
- `FinalDrawSystem` — composites the per-target render textures onto the backbuffer
- `DrawPrepSystemBase` — base class for new prep systems (mesh, text use this)

### Non-ECS types

- `Camera` (class, in this block) — view matrix, virtual resolution, zoom, position, rotation
- `ViewportManager` — handles letterbox/pillarbox between virtual and screen coords
- `DrawLayerMap` — utility for ordering layers

## Pipeline wiring

Each frame the draw stack runs in this order, at the tail of the screen's update pipeline:

1. **Prep systems** (per draw type) populate `DrawComponent` from source data — `SpritePrepSystem` reads `SpriteInfoComponent`; `MeshPrepSystem` (from `rendering-mesh`) and `TextPrepSystem` (from `rendering-text`) follow the same pattern.
2. **`CullingSystem`** adds/removes `VisibleComponent` based on camera view bounds.
3. **`YSortSystem`** writes a depth offset so back-to-front sprites overlap correctly.
4. **`MasterRenderSystem`** iterates render targets (Main → UI → HUD) and submits draw calls.
5. **`FinalDrawSystem`** composites the targets onto the screen.

Entities that render need: `TransformComponent`, the type-specific source (e.g. `SpriteInfoComponent`), `DrawComponent`, and `VisibleComponent`. `VisibleComponent` is a tag — for Main entities, `CullingSystem` manages it; for UI/HUD, you set it yourself once.

## Cross-block dependencies

- `foundation` — `TransformComponent.WorldPosition` is the spatial input to every prep system; `HierarchySystem` must run before any prep stage.

## Extension points

- **New visual types.** Extend `DrawElementType`, add a corresponding prep system following `DrawPrepSystemBase`, and teach `MasterRenderSystem` how to draw it. Do not fork a parallel `*Component` + `*RenderSystem` pair — the framework's invariant is one render component, one renderer.
- **New render targets.** Add to `RenderTargetID` and update `MasterRenderSystem` and `FinalDrawSystem`. New targets default to screen-space (UI/HUD-like behavior) unless they should be culled.
- **Custom culling.** `CullingSystem` reads `Camera.VirtualScreenBounds`; a wider bounds check (e.g. for a minimap) is a separate system that ignores `VisibleComponent` and writes to its own target.

## See also

- [Premises](premises.md) — load-bearing invariants for this block (one render component, sole renderer, the three-targets-two-behaviors split)
- Related blocks: `rendering-mesh` (adds `Mesh` draws), `rendering-text` (adds `Text` draws), `camera` (adds follow behavior on top of `Camera`), `debug` (adds collider/sprite overlays via the same path)
