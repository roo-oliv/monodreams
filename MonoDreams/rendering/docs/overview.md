# rendering — overview

The unified draw stack: one `DrawComponent` per entity (Sprite / Text / NinePatch / Mesh), three render targets (Main / UI / HUD), explicit culling and Y-sort stages, and a single renderer (`MasterRenderSystem`) that owns every draw call. Ships both the sprite path and the procedural-mesh primitives (`IMeshGenerator`, `MeshData`, `MeshPrepSystem`). Install this for any game that draws anything.

## Purpose

This module defines how things appear on screen. It owns the entire draw path — `SpriteBatch` and `BasicEffect` are both hidden behind `MasterRenderSystem`, batch ordering and render-target switches are centralized, and per-entity visibility is a derived tag (`VisibleComponent`) maintained by `CullingSystem`. The module also ships the `Camera` class itself, because the draw stack reads `camera.ViewMatrix` every frame — making `Camera` a hard dependency of rendering rather than an optional add-on. Mesh primitives ship in this module too: `DrawComponent` carries the mesh fields (`Vertices`, `Indices`, `WorldMatrix`) directly, so procedural shapes can't be separated from rendering without circularity. Without this module, nothing renders; everything else that draws (text, cursor, dialogue UI) extends this stack rather than parallels it.

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
- `MeshPrepSystem` — invokes each mesh entity's `IMeshGenerator`, copies the resulting `MeshData` + `TransformComponent.WorldMatrix` into `DrawComponent`
- `CullingSystem` — adds/removes `VisibleComponent` based on camera view bounds (Main target only)
- `YSortSystem` — writes layer-depth offset for back-to-front Y-sorted layers; parent-child tiebreaker via tiny epsilon
- `MasterRenderSystem` — the sole render *implementation*; **one instance = one pass** (entities of a `source` target, through an optional `camera`, into a `destination` render target). Layer-sorts and draws sprites/text/ninepatch via `SpriteBatch`, meshes via `BasicEffect`. Register one per view: world + UI + HUD, plus extra instances for minimaps / splitscreen / CCTV / portals
- `FinalDrawSystem` — composites the render targets onto the backbuffer from an explicit, ordered `RenderLayer` list (`Main`/`UI`/`HUD` full-frame factories, `Overlay` for a sub-rectangle like a minimap)
- `DrawPrepSystemBase` — base class for new prep systems (text uses this)

### Mesh primitives

- `IMeshGenerator` — interface returning `MeshData` (triangle-list `VertexPositionColor[]` + `short[]`)
- Canonical implementations: `CircleMeshGenerator`, `LineMeshGenerator`, `RectangleOutlineMeshGenerator`, `FilledRectangleMeshGenerator`, `GradientPathMeshGenerator`, `CompositeMeshGenerator` (rebases sub-mesh indices into the combined buffer)

### Non-ECS types

- `Camera` (class, in this module) — view matrix, virtual resolution, zoom, position, rotation
- `ViewportManager` — handles letterbox/pillarbox between virtual and screen coords
- `DrawLayerMap` — utility for ordering layers

## Pipeline wiring

Each frame the draw stack runs in this order, at the tail of the screen's update pipeline:

1. **Prep systems** (per draw type) populate `DrawComponent` from source data — `SpritePrepSystem` reads `SpriteInfoComponent`; `MeshPrepSystem` invokes each entity's `IMeshGenerator`; `TextPrepSystem` (from `rendering-text`) follows the same pattern.
2. **`CullingSystem`** adds/removes `VisibleComponent` based on camera view bounds.
3. **`YSortSystem`** writes a depth offset so back-to-front sprites overlap correctly.
4. **`MasterRenderSystem`** — one instance per pass; the screen registers the world pass (Main, main camera), the UI and HUD screen-space passes, and any extra views (e.g. a minimap: Main entities through a second camera into a minimap target).
5. **`FinalDrawSystem`** composites the targets onto the screen in the screen's `RenderLayer` order.

Entities that render need: `TransformComponent`, the type-specific source (e.g. `SpriteInfoComponent`), `DrawComponent`, and `VisibleComponent`. `VisibleComponent` is a tag — for Main entities, `CullingSystem` manages it; for UI/HUD, you set it yourself once.

## Cross-module dependencies

- `foundation` — `TransformComponent.WorldPosition` is the spatial input to every prep system; `HierarchySystem` must run before any prep stage.

## Extension points

- **New visual types.** Extend `DrawElementType`, add a corresponding prep system following `DrawPrepSystemBase`, and teach `MasterRenderSystem` how to draw it. Do not fork a parallel `*Component` + `*RenderSystem` pair — the framework's invariant is one render component, one render implementation.
- **New mesh shape.** Implement `IMeshGenerator` — produce a `MeshData` with triangle-list `short[]` indices. `MeshPrepSystem` and `MasterRenderSystem` already know how to consume it. Use `CompositeMeshGenerator` to bundle several sub-generators into one mesh entity (e.g. button outline + glow + label backdrop).
- **Extra camera views (minimap / splitscreen / CCTV / portals).** Register another `MasterRenderSystem` instance with the same `source` (usually `Main`), a second `Camera`, and its own `destination` render target — then add a `RenderLayer` for it to `FinalDrawSystem` (`Overlay` for a minimap box; tiled `Main`-style layers for splitscreen). For a portal, sample the portal target as a sprite texture in the world instead of compositing it. The camera demo wires a minimap this way. Caveat: a pass with `source = Main` inherits `VisibleComponent`, so under an active `CullingSystem` it shows only main-camera-visible entities; a cull-independent set per view is future work.
- **New render targets.** Add to `RenderTargetID`, register a `MasterRenderSystem` pass for it, and add a `RenderLayer` to `FinalDrawSystem`. New targets default to screen-space; only `source == Main` is culled.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (one render component, sole renderer, the three-targets-two-behaviors split, triangle-list mesh contract)
- Related modules: `rendering-text` (adds `Text` draws on top of this stack), `camera` (adds follow behavior on top of `Camera`), `ui` (uses mesh generators for button outlines), `debug` (adds collider/sprite overlays via the same path)
