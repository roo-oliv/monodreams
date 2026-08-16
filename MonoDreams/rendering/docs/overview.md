# rendering — overview

The unified draw stack: one `DrawComponent` per entity (Sprite / Text / NinePatch / Mesh), three render targets (Main / UI / HUD), explicit culling and Y-sort stages, and a single renderer (`MasterRenderSystem`) that owns every draw call. Ships both the sprite path and the procedural-mesh primitives (`IMeshGenerator`, `MeshData`, `MeshPrepSystem`). Install this for any game that draws anything.

## Purpose

This module defines how things appear on screen. It owns the entire draw path — `SpriteBatch` and `BasicEffect` are both hidden behind `MasterRenderSystem`, batch ordering and render-target switches are centralized, and per-entity visibility is a derived tag (`VisibleComponent`) maintained by `CullingSystem`. The module also ships the `Camera` class itself, because the draw stack reads `camera.ViewMatrix` every frame — making `Camera` a hard dependency of rendering rather than an optional add-on. Mesh primitives ship in this module too: `DrawComponent` carries the mesh fields (`Vertices`, `Indices`, `WorldMatrix`) directly, so procedural shapes can't be separated from rendering without circularity. Without this module, nothing renders; everything else that draws (text, cursor, dialogue UI) extends this stack rather than parallels it.

## What ships

### Components

- `DrawComponent` (class) — the unified render component; one per renderable entity. `Type` discriminates `Sprite`/`Text`/`NinePatch`/`Mesh`
- `DrawElement` / `DrawElementType` — enum + helpers for the draw-type discriminator
- `SpriteInfoComponent` (struct) — texture + source rect + color + layer depth for sprite-typed draws
- `SpriteAnimationComponent` (struct) — a frame-sequence clip over a sprite: `SpriteAnimationFrame[]` (per-frame asset key + source rect + duration), `DefaultFrameDuration`, `Loop`, `Playing`, `Speed`, plus the runtime `Time` / `FrameIndex` (never serialized)
- `NinePatchInfo` — source data for nine-patch sprite drawing
- `VisibleComponent` — empty tag added/removed by `CullingSystem` for Main-target entities; UI/HUD set it themselves
- `RenderTargetID` — enum: `Main` (world, camera-transformed), `UI` (screen-space), `HUD` (screen-space, on top)

### Systems

- `SpritePrepSystem` — copies `SpriteInfoComponent` + `TransformComponent` into `DrawComponent` each frame
- `SpriteAnimationSystem` — advances each `SpriteAnimationComponent` and writes the current frame onto the sprite's SOURCE fields (texture / asset key / source rect, and size when unscaled); an update-pipeline system registered BEFORE the prep stage, with an injected `resolveTexture` callback for one-texture-per-frame strips. Register it `Freeze` in editor-capable screens
- `MeshPrepSystem` — invokes each mesh entity's `IMeshGenerator`, copies the resulting `MeshData` + `TransformComponent.WorldMatrix` into `DrawComponent`
- `CullingSystem` — adds/removes `VisibleComponent` based on camera view bounds (Main target only)
- `YSortSystem` — writes layer-depth offset for back-to-front Y-sorted layers; parent-child tiebreaker via tiny epsilon
- `MasterRenderSystem` — the sole render *implementation*; **one instance = one pass** (entities of a `source` target, through an optional `camera`, into a `destination` render target). Layer-sorts and draws sprites/text/ninepatch via `SpriteBatch`, meshes via `BasicEffect`. Register one per view: world + UI + HUD, plus extra instances for minimaps / splitscreen / CCTV / portals
- `FinalDrawSystem` — composites the render targets onto the backbuffer from an explicit, ordered `RenderLayer` list (`Main`/`UI`/`HUD` full-frame factories, `Overlay` for a sub-rectangle like a minimap)
- `DrawPrepSystemBase` — base class for new prep systems (text uses this)

### Mesh primitives

- `IMeshGenerator` — interface returning `MeshData` (triangle-list `VertexPositionColor[]` + `short[]`)
- Textured meshes — `DrawComponent.TexturedVertices` (`VertexPositionColorTexture[]`) + `DrawComponent.Texture` draw one mesh sampling a sheet, so a whole tile chunk is ONE draw call; same `BasicEffect`, `TextureEnabled` + the sprite sampler (`PointClamp`). `IMeshGenerator` itself stays vertex-coloured. See the premise "A mesh may be textured (`TexturedVertices` + `Texture`)"
- Canonical implementations: `CircleMeshGenerator`, `LineMeshGenerator`, `RectangleOutlineMeshGenerator`, `FilledRectangleMeshGenerator`, `GradientPathMeshGenerator`, `CompositeMeshGenerator` (rebases sub-mesh indices into the combined buffer)

### Non-ECS types

- `Camera` (class, in this module) — view matrix, virtual (destination) resolution, render scale, zoom, position, rotation
- `ViewportManager` — owns the two coordinate spaces (authoring/layout vs render/virtual), the presentation policy that maps the frame onto the window, `MapMouse`, and the cameras (`CreateCamera` / `LayoutCamera` / `CreateLayoutCamera`)
- `PresentationPolicy` — the declared answer to "the window is not the render resolution": overscan to a clean scale → letter/pillarbox at a clean scale → stretch, plus the `SamplerPolicy` each `RenderLayer` carries
- `DrawLayerMap` — utility for ordering layers

## The two coordinate spaces

`ViewportManager` owns two resolutions:

- **Authoring (layout) space** — `LayoutWidth`×`LayoutHeight`: where every game
  number lives (entity and UI coordinates, HUD/overlay boxes, `Camera.Zoom`,
  culling extents, and the point `MapMouse` returns).
- **Render (virtual) space** — `VirtualWidth`×`VirtualHeight`: the pixel size of
  the render targets and the back buffer.

They default to being equal — the single-space game, where nothing about this
section is observable. A game opts into two spaces by passing a layout size to
the constructor (or `SetResolution`, which takes the same arguments), and then a
render-resolution move costs a two-number diff in the head: `RenderScale` reaches
the frame through the cameras and nowhere else. Both entry points read a layout
dimension of **0** as "same as the render dimension", so a settings object whose
layout size is unset (`GameSettings.LayoutWidth`/`LayoutHeight` default to 0) can
be forwarded to either one and simply stays single-space.

```csharp
// Head: author at 1280x720, render at 1920x1080.
_viewportManager = new ViewportManager(this, 1920, 1080, 1280, 720);
_camera = _viewportManager.CreateCamera();               // world passes
...
// Screen: targets at RENDER size; screen-space passes take the layout camera
// (exactly Matrix.Identity when the two spaces are equal).
new MasterRenderSystem(sb, gd, world, RenderTargetID.Main, mainTarget, _camera);
new MasterRenderSystem(sb, gd, world, RenderTargetID.HUD, hudTarget,
    _viewportManager.LayoutCamera);
// Pointer → authoring coordinates → world (robust to resize + letterbox).
var layoutPoint = _viewportManager.MapMouse(mouse.Position.ToVector2());
var worldPoint = layoutPoint is { } p ? _camera.VirtualScreenToWorld(p) : (Vector2?)null;
```

`MonoDreams.Demos` is the reference usage: it authors at 1280×720 and takes its
render resolution from `MONODREAMS_RENDER_SCALE` (unset ⇒ 1). See the premise
"Authoring space and render space are distinct; the scale lives only in the
cameras".

## The presentation scaling policy

The window is rarely exactly the render resolution. How that conflict is resolved
is a declared policy (`ViewportManager.Policy`), tried in this order:

1. **Overscan to a clean scale** — spend up to `OverscanTolerance` (5% by
   default) of extra scale to land on a clean step; the frame then overflows the
   window and its edges leave the screen. The zero-crop way to spend that budget
   is to render more world instead: `policy.ResolveRenderSize(designW, designH,
   windowW, windowH)` returns the render resolution at which the clean present
   fills the window exactly — a boot-time decision, before the screens allocate
   their render targets.
2. **Letter/pillarbox at a clean scale** — drop to the clean step below, padding
   with bars, while the drop costs no more than `LetterboxTolerance` (25%).
3. **Stretch** — the exact aspect-fit rectangle at a fractional scale (the
   historical present).

"Clean" is `CleanScaleSteps.Half` (…, 1/1.5, 1, 1.5, 2, …) or `.Integer`
(…, 1/2, 1, 2, …). The presets:

| Policy | Chain | For |
|---|---|---|
| `Stretch` | stretch only | the ENGINE default — framed exactly as before the policy existed |
| `Default` | overscan 5% → letterbox 25% → stretch | the **scaffold default**: what a new game should declare |
| `Crisp` | overscan → letterbox (unbounded) | never resample at a fractional ratio, however wide the bars |
| `PixelPerfect` | whole steps, letterbox (unbounded) | the retired `ScalingMode.PixelPerfect` — identical at or above 1×; below it this shrinks in whole steps (1/2, 1/3, …) where the old mode clamped to 1× and cropped |

```csharp
_viewportManager.Policy = PresentationPolicy.Default;
// …or tune the trade: more extra view, no bars, never soft.
_viewportManager.Policy = PresentationPolicy.Default with
{
    OverscanTolerance = 0.08f, AllowStretch = false,
};
```

Whichever step wins produces the ONE `DestinationRectangle` that `FinalDrawSystem`
composites into and `MapMouse` inverts, so the pointer follows the framing for
free. Independently, every `RenderLayer` carries a `SamplerPolicy` — `Auto` (point
at an integer scale, linear otherwise), `Point` or `Linear` — resolved per layer
against its own destination-over-target scale. See the premise "Presentation
scaling is a declared policy, resolved in one place".

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
