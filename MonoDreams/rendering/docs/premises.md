# rendering — premises

> Technical invariants the engine assumes about the rendering pipeline:
> `DrawComponent`, `SpriteInfoComponent`, `VisibleComponent`, the
> `Camera` class, render targets, `CullingSystem`, `SpritePrepSystem`,
> `YSortSystem`, `MasterRenderSystem`, `FinalDrawSystem`, plus the mesh
> primitives (`IMeshGenerator`, `MeshData`, `MeshPrepSystem`). Read this
> before changing any of those pieces or any system that produces draw
> data downstream.

## `DrawComponent.Type` is mutually exclusive

A `DrawComponent`'s `Type` is one of `Sprite`, `Text`, `NinePatch`, or
`Mesh`. One render component per renderable entity; an entity that needs
two visual types needs two entities (or a new `DrawElementType` that
composes both).

**Why:** `MasterRenderSystem` switches batch context based on `Type`
(`Sprite`/`Text`/`NinePatch` go through `SpriteBatch`, `Mesh` goes
through `BasicEffect` triangles). Allowing a single `DrawComponent` to
span two types would force every render path through a per-call branch.
**Breaks:** game code that sets a `DrawComponent` to one type and then
expects sprite + text behavior renders only one of them, with no
diagnostic.
**Tests:** none yet.
**Depends on:** —

## `DrawComponent` is the only render component

Do not create new draw or render components. Adding a new visual type
extends `DrawElementType` and `MasterRenderSystem`; it does not fork the
pipeline.

**Why:** a parallel render component would also need a parallel render
system, parallel culling treatment, and parallel layer-depth handling —
the framework's value comes from there being exactly one path through
the pipeline.
**Breaks:** the parallel system either skips entities the main path
expected (silent invisibility) or competes with it (z-fighting,
double-rendering).
**Tests:** none yet.
**Depends on:** —

## `MasterRenderSystem` is the sole renderer

All `SpriteBatch.Begin` / `Draw` / `End` calls happen inside
`MasterRenderSystem`. Game code must not call `SpriteBatch` directly, and
no parallel render system should exist.

**Why:** centralizing the draw call lets one place own the render-target
switches, the batch settings, the sort, and the layer-depth contract. A
second renderer fragments all four.
**Breaks:** parallel renderers fight over the active `SpriteBatch`,
producing flickering frames, dropped draws, or driver-level errors.
**Tests:** none yet.
**Depends on:** —

## Renderable entity stack on the Main target

An entity is rendered to the Main target only if it has
`TransformComponent + SpriteInfoComponent (or text/mesh equivalent) +
DrawComponent + VisibleComponent`. Missing `VisibleComponent` means
`MasterRenderSystem` skips silently (it includes `.With<VisibleComponent>()`
in the Main-target query).

**Why:** `VisibleComponent` is the culling output. `CullingSystem` adds
it for entities inside the camera frustum and removes it for entities
outside, letting `MasterRenderSystem` skip a frustum check per entity.
Splitting it from `DrawComponent` is a wart — see Aspirational.
**Breaks:** the canonical missed-tag bug — a dev creates an entity with
`SpriteInfoComponent + DrawComponent` and stares at an invisible result
with no error. Forgetting to register `CullingSystem` in a new screen
makes everything Main-target invisible.
**Tests:** none yet. *This is the specific bug from the bootstrap
interview that motivated writing premises files in the first place.*
**Depends on:** "`VisibleComponent` is owned exclusively by `CullingSystem`".

## `VisibleComponent` is owned exclusively by `CullingSystem`

Game code must not add or remove `VisibleComponent` on Main-target
entities. It is managed automatically by `CullingSystem` based on the
camera's `VirtualScreenBounds`. UI and HUD entities may set
`VisibleComponent` themselves as a one-shot tag, since those targets
ignore culling (see "Three render targets, two behaviors").

**Why:** `VisibleComponent` is a derived state on the Main target. Manual
mutation creates ghost entities (visible but outside the frustum, wasting
draw calls) or phantom-invisible entities (inside the frustum but
skipped). `CullingSystem` will overwrite the game-set value next frame
anyway.
**Breaks:** entities flicker in and out as `CullingSystem` overwrites
game-set values frame to frame; or the cull check fights game code
forever.
**Tests:** none yet.
**Depends on:** —

## Three render targets, two behaviors

`RenderTargetID.Main` is camera-transformed and respects culling.
`RenderTargetID.UI` and `RenderTargetID.HUD` are screen-space and always
render. Only Main consults `VisibleComponent`.

**Why:** UI and HUD are always-on-screen by definition. Culling them
would mean checking against the *screen* frustum, which is a degenerate
case (everything is inside). `MasterRenderSystem` enforces this by only
adding `.With<VisibleComponent>()` to the Main-target query.
**Breaks:** putting a Main-target entity on UI/HUD by mistake skips
camera transforms — the entity renders at its world coordinates,
unscaled by zoom. The reverse (UI on Main) gets culled away when the
camera moves.
**Tests:** none yet.
**Depends on:** —

## Rendering systems run last in the pipeline

In any screen's pipeline assembly, the prep / cull / sort / render block
goes at the tail. Logic that mutates renderable state (positions, sprite
source rects, text contents, layer depths) must complete before the prep
block reads it.

**Why:** the prep block freezes the state of `DrawComponent`s into the
draw queue; mutations after the queue is built are silently lost until
next frame.
**Breaks:** a game system that updates text mid-render-block sees the
old text on screen. A movement system after `CullingSystem` updates
positions without re-culling — entities pop in or out by one frame.
**Tests:** none yet.
**Depends on:** —

## Layer-depth ownership pipeline

`SpritePrepSystem` initializes `DrawComponent.LayerDepth` from
`SpriteInfoComponent.LayerDepth`. `YSortSystem` may override it for
entities on Y-sorted layers. `MasterRenderSystem` sorts on the final
value. Game systems writing `LayerDepth` after `YSortSystem` create
undefined sort behavior.

**Why:** three writers in a defined order produce one final value. A
fourth writer in between (or after) breaks the contract.
**Breaks:** entities flicker, depth-fight, or render behind/in-front of
static elements unexpectedly.
**Tests:** none yet.
**Depends on:** —

## Y-sort tiebreaker is parent-child bias only

`YSortSystem` uses a minimal epsilon (`1e-6f` in
`MonoDreams/rendering/System/Draw/YSortSystem.cs`) to bias children
relative to their parent's final depth, preserving group ordering.
Same-layer entities at the same world Y with no parent-child relationship
have no further tiebreaker — render order falls through to entity
insertion order.

**Why:** parent-child bias is the only ambiguity case the framework
treats explicitly; "true ties" are rare in practice (entities at the
exact same Y on the same layer).
**Breaks:** if true ties become common, flicker can appear when
insertion order changes (e.g., re-spawn). The fix is a tiebreaker —
possibly entity ID — but that's a framework change, not a workaround.
**Tests:** none yet.
**Depends on:** —

## `Camera.VirtualResolution` is immutable

`Camera.VirtualWidth` and `Camera.VirtualHeight` are readonly properties
set in the constructor (the `Camera` class lives at
`MonoDreams/rendering/Camera.cs` because it is a hard dependency of the
draw stack — `MasterRenderSystem` reads its position, zoom, and view
matrix every frame). Only zoom, position, and rotation are mutable on a
live `Camera`.

**Why:** virtual resolution defines the world-units-per-pixel ratio.
Changing it mid-frame would require recomputing every entity's on-screen
size and re-running culling.
**Breaks:** a system that tries to change resolution at runtime either
silently fails (readonly) or, if it bypasses, produces a fractional
frame where culling, layout, and rendering disagree.
**Tests:** none yet.
**Depends on:** —

## `IMeshGenerator.Generate()` returns a triangle list

All canonical generators (`CircleMeshGenerator`, `LineMeshGenerator`,
`RectangleOutlineMeshGenerator`, `FilledRectangleMeshGenerator`,
`GradientPathMeshGenerator`, `CompositeMeshGenerator`) return `MeshData`
whose indices describe a triangle list — every triple of indices is one
triangle. `MasterRenderSystem` invokes `DrawUserIndexedPrimitives` with
`PrimitiveType.TriangleList`.

**Why:** triangle lists are the lowest common denominator for the
`BasicEffect` path. Triangle strips or fans would require per-shape
metadata about which primitive type to use, and would multiply the
render path's branching.
**Breaks:** a custom generator that emits a triangle strip without
setting `DrawComponent.PrimitiveType` correctly produces garbled
geometry — the renderer interprets the indices as a list and draws
random triangles between them.
**Tests:** none yet.
**Depends on:** —

## `MeshPrepSystem` writes the world matrix once per frame

`MeshPrepSystem` reads `TransformComponent.WorldMatrix` and writes it
into `DrawComponent.WorldMatrix` for every entity with `Type = Mesh`.
This must happen after `HierarchySystem` propagates dirty flags and
before `MasterRenderSystem` reads the matrix to bind it to
`BasicEffect.World`.

**Why:** meshes render in world space — their vertex positions are
the geometry's local-space layout, and the world matrix transforms
them to where the entity is. Reading `TransformComponent.WorldMatrix`
in `MasterRenderSystem` directly would couple the renderer to the
transform; staging through `DrawComponent` keeps the renderer
agnostic.
**Breaks:** running `MeshPrepSystem` before `HierarchySystem` produces
meshes positioned by last frame's parent transform (one-frame lag on
parented meshes). Running it after `MasterRenderSystem` is too late —
the renderer uses the previous frame's world matrix.
**Tests:** none yet.
**Depends on:** foundation — "`HierarchySystem` must run ahead of any
system reading WorldPosition"; "Rendering systems run last in the
pipeline" (above).

## `CompositeMeshGenerator` rebases indices into the combined buffer

When `CompositeMeshGenerator.Generate()` walks its sub-generators, it
adds each sub-mesh's vertices to a combined vertex list and adds
`indexOffset` to each sub-mesh's indices before appending to the
combined index list. A sub-generator returning indices that already
reference the combined buffer's range would draw the wrong triangles.

**Why:** composability is the design goal — a button outline + label
backdrop + glow can be one mesh entity. The rebase invariant is what
makes "just add another generator" work without per-generator
coordination.
**Breaks:** a custom generator that returns indices unrelated to its
own vertex buffer (e.g. via a stale cached array) corrupts the
composite's geometry. The bug manifests as visible triangles between
unrelated parts of the composite.
**Tests:** none yet.
**Depends on:** —

## Open questions

- **Same-Y flicker in practice** — has this surfaced? If so, the
  tiebreaker premise above needs to harden.
- **Mixed render targets per entity** — is it ever valid for the same
  entity to render on Main *and* HUD (via two separate `DrawComponent`s
  on different entities)? Probably always two entities, but not yet a
  settled convention.
- **`DrawElement` cache invalidation** — what happens if a prep system
  mutates `SpriteInfoComponent` after the draw queue was built?
- **Texturing meshes** — `MeshData` carries `VertexPositionColor`
  vertices; there's no `VertexPositionTexture` path. Whether to add one
  (and how it interacts with the SpriteBatch-based sprite path) is open.
- **Mesh culling extents** — meshes get `VisibleComponent` from
  `CullingSystem` based on `TransformComponent.WorldPosition` against
  the camera bounds. A mesh's actual extents (computed from its
  vertices) might lie outside the position's frustum, causing visible
  pop-out at edges. No bounding-box-from-vertices logic exists today.

## Aspirational direction

- `VisibleComponent` becomes a property of `DrawComponent` (removes the
  easy-to-miss tag).
- Render targets become more configurable — custom post-processing
  passes, shader effects, multiple Main targets for split-screen.
- A `MeshTransformBatcher` that combines static meshes sharing a layer
  depth into a single submission, cutting `BasicEffect` draw calls.
- Per-vertex texture-coordinate support so meshes can sample sprite
  atlases (would let the dialogue indicator or UI nine-patches be a
  mesh rather than a `SpriteBatch` ninepatch).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `DrawComponent.Type` is mutually exclusive
- `DrawComponent` is the only render component
- `MasterRenderSystem` is the sole renderer
- Renderable entity stack on the Main target
- `VisibleComponent` is owned exclusively by `CullingSystem`
- Three render targets, two behaviors
- Rendering systems run last in the pipeline
- Layer-depth ownership pipeline
- Y-sort tiebreaker is parent-child bias only
- `Camera.VirtualResolution` is immutable
- `IMeshGenerator.Generate()` returns a triangle list
- `MeshPrepSystem` writes the world matrix once per frame
- `CompositeMeshGenerator` rebases indices into the combined buffer

Architectural tests for ECS-purity premises (no parallel render systems,
no game `SpriteBatch` calls, `VisibleComponent` not added outside
`CullingSystem` on Main-target entities) are the highest-leverage
candidates here.
