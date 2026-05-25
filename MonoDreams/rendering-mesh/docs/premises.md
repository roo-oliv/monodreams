# rendering-mesh — premises

> Technical invariants the engine assumes about procedural mesh
> rendering: `IMeshGenerator` (and its canonical implementations),
> `MeshData`, and `MeshPrepSystem`. Read this before changing any of
> those pieces or adding a new shape generator.

## Mesh entities use the same `DrawComponent` slot

A mesh-rendered entity has one `DrawComponent` with `Type = Mesh` plus
the standard `TransformComponent` + `VisibleComponent`. The vertex /
index buffers go into `DrawComponent.Vertices` / `Indices` (and
`PrimitiveType`); the world transform goes into
`DrawComponent.WorldMatrix`, written by `MeshPrepSystem` from
`TransformComponent.WorldMatrix`. There is no separate `MeshComponent`.

**Why:** the rendering pipeline's "`DrawComponent` is the only render
component" invariant applies here. Forking a `MeshComponent` would
require a parallel render system, parallel culling, parallel layer
depth — all the costs of breaking the unified path.
**Breaks:** game code that creates a parallel `MeshComponent` falls
out of `MasterRenderSystem`'s draw query, and the mesh never renders.
A new mesh feature (multiple geometries per entity, animated
deformation) should extend `DrawElementType` and `DrawComponent`, not
fork to a new component.
**Tests:** none yet.
**Depends on:** rendering — "`DrawComponent` is the only render component".

## `IMeshGenerator.Generate()` returns a triangle list

All canonical generators (`CircleMeshGenerator`, `LineMeshGenerator`,
`RectangleOutlineMeshGenerator`, `FilledRectangleMeshGenerator`,
`GradientPathMeshGenerator`, `CompositeMeshGenerator`) return
`MeshData` whose indices describe a triangle list — every triple of
indices is one triangle. `MasterRenderSystem` invokes
`DrawUserIndexedPrimitives` with `PrimitiveType.TriangleList`.

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
system reading WorldPosition"; rendering — "Rendering systems run last
in the pipeline".

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

- **Texturing meshes** — `MeshData` carries `VertexPositionColor`
  vertices; there's no `VertexPositionTexture` path. Whether to add
  one (and how it interacts with the SpriteBatch-based sprite path)
  is open.
- **Mesh culling** — meshes get `VisibleComponent` like sprites and
  `CullingSystem` adds/removes it based on
  `TransformComponent.WorldPosition` against the camera view bounds.
  But a mesh's actual extents (computed from its vertices) might lie
  outside the position's frustum, causing visible pop-out at edges.
  No bounding-box-from-vertices logic exists today.

## Aspirational direction

- A `MeshTransformBatcher` that combines static meshes sharing a
  layer depth into a single submission, cutting `BasicEffect` draw
  calls.
- Per-vertex texture-coordinate support so meshes can sample sprite
  atlases (would let the dialogue indicator or UI nine-patches be a
  mesh rather than a `SpriteBatch` ninepatch).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Mesh entities use the same `DrawComponent` slot
- `IMeshGenerator.Generate()` returns a triangle list
- `MeshPrepSystem` writes the world matrix once per frame
- `CompositeMeshGenerator` rebases indices into the combined buffer
