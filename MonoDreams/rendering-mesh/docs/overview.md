# rendering-mesh — overview

Procedural shape rendering — circles, lines, filled rectangles, outlines, gradient paths, and arbitrary composites — driven by an `IMeshGenerator` interface and a single `MeshPrepSystem` that stages geometry into the standard `DrawComponent` pipeline. Install this when a game needs primitive shapes that aren't worth authoring as textures (UI chrome, debug overlays, vector indicators).

## Purpose

This block exists so games can draw vector-shaped visuals without forking the render path. The trick is that meshes ride the same `DrawComponent` slot as sprites — only the `Type` discriminator and the vertex/index buffers differ. `MasterRenderSystem` switches to `BasicEffect` for `Type = Mesh` entries and renders them inline with sprites, preserving layer depth and render-target rules. A game that doesn't need procedural shapes can omit this block; one that needs it (UI outlines, dialogue indicators, screen flashes) gets a clean extension point rather than a new render path.

## What ships

### Source files

- `Draw/IMeshGenerator.cs` — the `IMeshGenerator` interface plus the canonical implementations: `CircleMeshGenerator`, `LineMeshGenerator`, `RectangleOutlineMeshGenerator`, `FilledRectangleMeshGenerator`, `GradientPathMeshGenerator`, `CompositeMeshGenerator`
- `Draw/MeshData.cs` — `MeshData` value type holding `VertexPositionColor[]` and `short[]` indices

### Systems

- `MeshPrepSystem` — runs per frame: invokes each mesh entity's generator, copies the resulting vertices/indices and `TransformComponent.WorldMatrix` into `DrawComponent`. Runs in the prep stage, after `HierarchySystem`, before `MasterRenderSystem`

This block does not ship a separate `MeshComponent` — geometry data lives directly on `DrawComponent` (see the block's premises for why).

## Pipeline wiring

1. On the entity you want to render as a mesh: set `DrawComponent.Type = DrawElementType.Mesh` and store your `IMeshGenerator` somewhere `MeshPrepSystem` can find it (typically a `MeshInfo` field on game-side state, or a custom component your code drives).
2. Add `MeshPrepSystem` to the prep stage of your update pipeline, alongside `SpritePrepSystem` and `TextPrepSystem`.
3. `MasterRenderSystem` from `rendering` handles the rest — it sees `Type = Mesh` and routes through `DrawUserIndexedPrimitives` with `BasicEffect`.

All canonical generators output triangle lists. `CompositeMeshGenerator` walks sub-generators and rebases indices into the combined buffer — composite shapes (button outline + glow + label backdrop) are one mesh entity, not three.

## Cross-block dependencies

- `rendering` — `MeshPrepSystem` writes into `DrawComponent`; `MasterRenderSystem` is the renderer that submits the geometry. The "one render component, one renderer" invariant carries through.

## Extension points

- **New mesh shape.** Implement `IMeshGenerator` — produce a `MeshData` with `VertexPositionColor[]` and a triangle-list `short[]`. Nothing else changes; `MeshPrepSystem` and `MasterRenderSystem` already know how to consume it.
- **Composing shapes.** Use `CompositeMeshGenerator` to bundle several sub-generators into one entity. Each sub-generator's indices are automatically rebased into the combined buffer.
- **Animated geometry.** A generator can re-compute `MeshData` per `Generate()` call. The prep system runs every frame, so the result reaches the renderer immediately.

## See also

- [Premises](premises.md) — load-bearing invariants for this block (one `DrawComponent` slot, triangle-list contract, composite index rebasing)
- Related blocks: `rendering` (the renderer; mesh draws ride its pipeline), `ui` (uses `ButtonMeshPrepSystem` + outline generators for interactive button chrome), `debug` (collider/sprite overlays are transient mesh entities)
