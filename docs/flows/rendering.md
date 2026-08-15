---
flow: rendering
covers:
  - MonoDreams/rendering/**
sensitive: false
---

# Draw path

Each frame, at the **tail** of the screen's pipeline, source data becomes a unified `DrawComponent`,
which becomes one of three composited render targets — in that order, for every renderable entity.
Nothing draws unless an entity carries `TransformComponent` + a type source (`SpriteInfoComponent`,
text, or mesh) + `DrawComponent` + `VisibleComponent`. The path is: **cull → prep → Y-sort → render
→ composite**. `CullingSystem` intersects each Main-target entity's screen bounds against the
`Camera.VirtualScreenBounds` and adds/removes the `VisibleComponent` tag. The prep systems
(`SpritePrepSystem`, `MeshPrepSystem`, and `TextPrepSystem` from rendering-text) then copy
`TransformComponent.WorldPosition`/`WorldMatrix` + source data into each entity's `DrawComponent` —
they query `.With<VisibleComponent>()`, so culling must run first or they skip the entity.
`YSortSystem` overrides `DrawComponent.LayerDepth` for entities on Y-sorted layers. Finally, each
`MasterRenderSystem` instance renders one pass — every entity whose `DrawComponent.Target` matches
its `source`, depth-sorted, through its optional `camera` view matrix, into its `destination`
target (cleared to transparent first) — and `FinalDrawSystem` composites the targets onto the back
buffer in `RenderLayer` order. The camera seam (camera module) only moves the `Camera`'s position;
`CullingSystem`, `YSortSystem`, and `MasterRenderSystem` all read that same `Camera` here.

## Entities & lifecycle

A renderable entity carries `TransformComponent` + a type source + `DrawComponent`, with
`DrawComponent.Type` ∈ {`Sprite`, `Text`, `NinePatch`, `Mesh`} (mutually exclusive) and
`DrawComponent.Target` ∈ {`Main`, `UI`, `HUD`, `Scroll`}. Per frame, in pipeline order:

1. **Cull** — `CullingSystem` (Main only) sets/clears `VisibleComponent` from camera bounds; UI/HUD/Scroll entities set it themselves once and keep it.
2. **Prep** — `SpritePrepSystem` / `MeshPrepSystem` / `TextPrepSystem` (all `.With<VisibleComponent>()`) freeze `WorldPosition`/`WorldMatrix` + source into `DrawComponent`. `LayerDepth` is initialized here from `SpriteInfoComponent.LayerDepth`.
3. **Y-sort** — `YSortSystem` may overwrite `LayerDepth` for Y-sorted layers; a `PostUpdate` pass biases children to their parent's final depth by `1e-6f`.
4. **Render** — each `MasterRenderSystem` instance builds its draw `EntitySet` **once** (cached for its lifetime), sorts the matching entities by `LayerDepth` (stable on insertion index), and draws into its cleared `destination`. Main passes filter on `VisibleComponent`; screen-space passes do not.
5. **Composite** — `FinalDrawSystem` walks an ordered `RenderLayer` list, drawing each target into the aspect-fit `ViewportManager` destination (later = on top).

`VisibleComponent` on Main is owned **exclusively** by `CullingSystem`; `LayerDepth` has three ordered writers (`SpritePrepSystem` → `YSortSystem` → `MasterRenderSystem` reads). An un-enumerated writer to either is the classic bug source.

## Invariants

Authoritative list in [`MonoDreams/rendering/docs/premises.md`](../../MonoDreams/rendering/docs/premises.md) (27 premises); the ordering this flow leans on:

- Cull runs **before** prep — `SpritePrepSystem`/`YSortSystem`/`MeshPrepSystem` all gate on `.With<VisibleComponent>()`, so a Main entity culled this frame is also un-prepped this frame.
- The whole module runs **last** in the pipeline; logic mutating positions/sprites/text/depth must complete before prep freezes `DrawComponent`, or the change shows next frame.
- `LayerDepth` has exactly three ordered writers; a fourth writer between or after `YSortSystem` makes sort order undefined.
- One `MasterRenderSystem` instance = one pass; it clears its `destination` on entry, so two passes sharing a target erase each other. The draw set is built once per instance, never per frame.
- A pass's `camera.VirtualWidth/Height` must equal its `destination` size (projection derives from the destination, the camera centers the view there).
- Authoring (layout) space and render (virtual) space are distinct, and the ratio between them (`ViewportManager.RenderScale`) is applied in **exactly one place: the per-pass cameras** (`Camera.RenderScale` multiplies `Zoom` in the view matrix). Screen-space passes take `ViewportManager.LayoutCamera`, not a null camera. The two spaces are equal by default, where the layout camera is exactly `Matrix.Identity`.

## Load-bearing quantities

- `DrawComponent.LayerDepth` — sort key, `float` in `[0, 1]` (0 = back, 1 = front). `MasterRenderSystem` orders ascending; same-depth ties fall through to insertion index (no other tiebreaker but the parent-child `1e-6f` bias).
- `Camera.VirtualWidth/VirtualHeight` — RENDER resolution in pixels, **immutable after construction** (default 800×600). A render pass's destination size must equal these.
- `Camera.RenderScale` — render pixels per AUTHORING unit, **immutable after construction** (default 1 = single-space); `Camera.LayoutWidth/Height` is the destination size divided by it — the authoring extent world code reasons in.
- `ViewportManager.LayoutWidth/LayoutHeight` / `RenderScale` / `MapMouse(screen)` — the authoring canvas, the one scale, and the screen→authoring pointer mapping (inverts the present `DestinationRectangle`; `null` outside the viewport).
- `Camera.GetViewTransformationMatrix()` — the Main pass transform: translate by `-Position`, rotate, scale by `Zoom × RenderScale` (zoom clamped ≥ `0.1`), recenter at `(VirtualWidth/2, VirtualHeight/2)`. Null camera ⇒ `Matrix.Identity` (screen-space, single-space only).
- Sprite-quad run cap — `SpriteBatchFlush.MaxSpritesPerBatch`, strictly below the Reach 16-bit-index limit of 5461 quads per `SpriteBatch.Begin`; text counts one quad per glyph.

## Failure modes

- **Invisible sprite, no error** — entity has `SpriteInfoComponent` + `DrawComponent` but never gets `VisibleComponent` (no `CullingSystem` in the screen, or a Main entity placed off-camera). `MasterRenderSystem`'s Main query silently skips it. The single most common render bug.
- **Wrong render target** — a Main-space entity tagged `UI`/`HUD` renders unscaled at world coords (no camera transform); a UI entity on Main gets culled away when the camera moves.
- **Game-set `VisibleComponent` on Main** — `CullingSystem` overwrites it next frame; the entity flickers in and out as the two fight.
- **Late `LayerDepth` write** — a system writes depth after `YSortSystem`; entities depth-fight or render behind/in front of where Y-sort placed them.
- **UI shrinks into a corner after a render-resolution move** — a screen-space pass left on a `null` camera draws authoring coordinates 1:1 into a bigger target. Give UI/HUD/Scroll passes `ViewportManager.LayoutCamera` (harmless at scale 1).
- **An authored number read from `VirtualWidth`** — a UI root, HUD box, overlay rect, fit-zoom or frustum outline sized from the render resolution moves when that resolution changes; those all belong in `LayoutWidth/Height`.
- **Two passes, one target** — a second `MasterRenderSystem` instance shares a `destination`; each clears on entry, so the second erases the first's frame (give every pass its own target, overlap via `FinalDrawSystem`).
- **Web-only crash on a dense scene** — removing/raising the sprite-run flush past 5461 quads pushes a batch into 32-bit indices, which the Reach profile rejects; paints on desktop (HiDef), throws on web.
