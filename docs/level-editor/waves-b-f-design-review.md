# Waves B–F design repass — brush tools over the Wave-A substrate

> **Status: binding design, 2026-07-03.** This document is the design repass of the
> level editor's next feature waves, written against the live `feat/level-editor`
> code (every constraint below was verified in source; refs are `file:line`). It
> **supersedes the Wave B–F sections of [`roadmap.md`](roadmap.md)** and
> **re-letters the waves** (mapping table below). A fresh implementation session
> should be able to start Wave B from this doc plus the module premises
> ([`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md))
> without re-deriving the architecture.
>
> Read first: [`docs/CORE_TENETS.md`](../CORE_TENETS.md) §9,
> [`MonoDreams/level-editor/docs/overview.md`](../../MonoDreams/level-editor/docs/overview.md),
> [`scene-format.md`](../../MonoDreams/level-editor/docs/scene-format.md),
> [`docs/flows/level-editor.md`](../flows/level-editor.md).

---

## Open questions for the human (ranked)

Decisions only the gamedev can make — look/feel forks and scope trades. Everything
else in this doc is recommended with rationale and can proceed without input;
these change what gets built.

1. **Ground canvas resolution & budget (Wave E).** The ground paint output is a
   persistent offscreen texture (the "ground canvas"), chunked. Defaults proposed:
   **1 texel per world unit** (paint at the game's native pixel density),
   **1024×1024 chunks** (4 MB RGBA each) allocated on first touch, soft warning at
   **16 chunks** (~64 MB). Is that density right for the painterly look you want
   (higher = crisper brush edges, more memory), and what's an acceptable total
   paint area per scene?
2. **Ground look for E v1: how many materials?** V1 ships **one material per canvas
   layer** (a brush stamp texture with alpha falloff; erase supported). Multiple
   materials = multiple canvas layers with a material palette (each adds a chunk
   set + draw order choices). Is single-material v1 acceptable, and roughly how
   many ground materials does the real game need (2–3? 8+)? 8+ starts to argue for
   the splatmap-shader upgrade path later.
3. **Road look (Wave F): painterly stamps vs crisp textured strip.** Recommended:
   roads bake as **stamps along the spline into the ground canvas** (no new render
   tech, matches the illustrated art direction, Reach-safe). The alternative — a
   crisp UV-textured mesh strip — requires adding a textured-mesh path to the
   `rendering` module (a framework change; exact touchpoints named in §Constraints).
   Do you accept stamped roads for F, with the UV mesh path as a separately
   approved framework change later?
4. **Scatter re-edit semantics (Wave D).** Recommended: **per-stroke seed**; baked
   entities are ground truth and never re-randomize on load; explicitly re-baking a
   stroke (after changing its brush settings) re-rolls that one stroke's placements.
   Acceptable? (The alternative — per-stamp persisted randoms so re-bake keeps every
   position — roughly triples the source data and complicates erase; not recommended.)
5. **Perf budget: target visible-entity count.** Each scattered instance is one
   sprite = one draw call + one entry in the per-frame depth sort
   (`MasterRenderSystem.cs:147-153`). Culling keeps off-screen cost near zero, but
   what's the acceptable **on-screen** count on the weakest target (web/Reach)?
   This number sets the scatter density caps' defaults (proposal: warn at 2 000
   visible, hard-cap brush density so a single stroke can't exceed ~500 instances).
6. **Palette content (Wave C).** The palette is screen-supplied (the game hands the
   overlay a list of `{ id, label, preview, factory }`). For the reference Examples
   screens, which prototypes should the palette expose (Tile/Wall/Enemy/Charm/
   Obstacle…), and do you want sprite thumbnails in v1 or text labels first?
7. **Scatter eraser: in D or deferred?** Erase-by-brush (remove instances under the
   cursor, undoable) is a natural D slice but not required for the paint path.
   Include in D (recommended, +1 slice) or defer?
8. **Spline flavor (Wave F).** Recommended: **Catmull-Rom through on-curve points**
   (Godot Path2D / Unreal landscape-spline feel: click to lay points, drag points,
   no tangent handles) with a tension parameter. Unity-style Bézier tangent handles
   are deferred (the proxy `Index` scheme reserves room). OK?
9. **Ground undo memory.** Pixel undo snapshots the stroke's touched region per
   stroke (bounded by the existing history cap, FIFO — `EditorHistory.cs:151`).
   Worst case ≈ stroke bbox × 4 B × cap. Fine by default, or should ground strokes
   get a lower dedicated cap?

---

## Wave re-lettering (this doc supersedes the roadmap's B–F)

| New | This doc | Old roadmap letter |
|---|---|---|
| **B** | Stroke-sampling input layer (the brush substrate) | — (was implicit in old B) |
| **C** | Free entity placement (palette, ghost, snap, layers) | — (was implicit in Wave A "manual placement") |
| **D** | Scatter brush (seeded, deterministic, real entities) | old **B** (scatter) |
| **E** | Ground paint (pixels into a baked canvas) | old **C** (stamps) + old **E** (splatmap — resolved: canvas, not shader) |
| **F** | Road/path tool (spline + control-point handles) | old **D** (road stamps) + old **F** (road mesh — resolved: stamps; UV mesh named as a future framework change) |

Decided and **not re-litigated** here: per-tool hybrid parametric/baked (road =
parametric spline in `sources[]`, scatter = region+seed in `sources[]`, ground =
baked); mandatory bounded undo everywhere; engine-native web-capable chrome (no
ImGui); full component serialization preserving the parent/child graph; the perf
rule **bake sources at load, never re-evaluate parametric sources per frame**.

---

## Verified code constraints (the ground truth this design stands on)

Everything below was checked in source on `feat/level-editor`.

**Mesh path has no textures.** `DrawComponent.Vertices` is `VertexPositionColor[]`
(`MonoDreams/rendering/Component/Draw/DrawComponent.cs:41`); every `IMeshGenerator`
emits vertex-color triangles (`MonoDreams/rendering/Draw/IMeshGenerator.cs`); the
one runtime effect is a lazily-built `BasicEffect` with `VertexColorEnabled = true`
and `TextureEnabled` never set (`MasterRenderSystem.cs:104-112`). No
`VertexPositionTexture`/`VertexPositionColorTexture` exists anywhere in code. The
repo's `.fx` files are dead code (no `Load<Effect>` anywhere;
`docs/web-targeting.md:216-225`). **Adding a UV'd mesh path is a framework change**
touching: `MeshData` + `DrawComponent.Vertices` vertex type, the mesh generators,
`MasterRenderSystem.EnsureBasicEffect` (enable + bind texture) and
`DrawSingleMesh` (`MasterRenderSystem.cs:224-248`). It is named in the rendering
premises as an open aspiration — it is NOT required by any wave below.

**Every standard render pass clears its target each frame** —
`MasterRenderSystem.Update` starts with `Clear(Color.Transparent)`
(`MasterRenderSystem.cs:137-142`) — but **`FinalDrawSystem` never clears layer
targets, only samples them** (`FinalDrawSystem.cs:149-165`). A persistent
accumulation target (the ground canvas) is therefore possible via a custom
non-clearing bake step; the closest existing precedent is the nine-patch
render-once-and-cache `RenderTarget2D` (`SpritePrepSystem.cs:76-153`). A canvas
`RenderTarget2D` **must be created with `RenderTargetUsage.PreserveContents`**
(the default discards contents when the target is unbound/rebound) and must be
re-bakeable from its source strokes after GL context loss (real on web).

**Runtime textures render fine but don't round-trip.**
`SpriteInfoComponent.SpriteSheet` accepts any texture (runtime RTs included);
`AssetKey == null` serializes to null and the reader skips rehydration
(`SceneReaderSystem.cs:126`) — the sprite reloads invisible. Consequence: **baked
canvases must never be entity-serialized; they are re-baked from `sources[]` at
load.**

**Sprite cost shape.** One `spriteBatch.Draw` per sprite, no texture batching, one
per-frame LINQ `OrderBy` allocation over all visible draws
(`MasterRenderSystem.cs:147-153`), auto-flush at 4 096 quads per batch segment
(Reach 16-bit index budget, `SpriteBatchFlush.cs:31`). `YSortSystem` rewrites
depth for every **visible** sprite on a Y-sorted layer every frame
(`YSortSystem.cs:22,38-61`); non-Y-sorted layers early-out at
`TryGetYSortRange` (`YSortSystem.cs:42-43`). `CullingSystem` culls **Main-target
sprites only** (`CullingSystem.cs:12,72`) — scattered instances (sprites) are
culled; mesh/text entities are never frustum-culled and need manual
`VisibleComponent`.

**Web/Reach.** The two hard limits the repo documents: no 32-bit indices (handled
engine-wide: batch flush + `Get16BitIndices()`), and shader model ≤ SM3 on the GL
path (`docs/web-targeting.md:216-255`). Platform branches live in heads only —
engine modules carry no `#if MONODREAMS_WEB` and no `GraphicsProfile` literal.
Plain `SpriteBatch` alpha blending and custom `BlendState`s are Reach-safe; custom
shaders are unprecedented in this engine.

**Spawn path.** `EntitySpawnSystem` dispatch is message-driven and **synchronous**
(factories run in the publisher's stack — `EntitySpawnSystem.cs:34,53-59`);
`IEntityFactory.CreateEntity(World, in EntitySpawnRequest)` **returns the created
root entity** (`IEntityFactory.cs:15`), so a factory is directly invokable — which
is exactly what `CreateEntityCommand`'s `Func<World, Entity>` builder needs
(`CreateEntityCommand.cs:31`). `EntitySpawnRequest` is LDtk-coupled (an `LDtk`
`Layer` field + `CustomFields` dict, `EntitySpawnRequest.cs:39,45`) but constructs
fine with `Layer: null`; factories already read `layerDepth` from `CustomFields`
(`TileEntityFactory.cs:24`) — the natural per-item layer hook.

**Undo already supports strokes natively.** `EditorHistory.Push` applies live;
inside a `BeginTransaction`/`CommitTransaction` window N pushes collapse to one
`CompositeCommand` entry (`EditorHistory.cs:63-99`) — the gizmo's per-drag pattern.
A stroke of N spawned instances = N `CreateEntityCommand` pushes inside one
transaction = **one undo step**, with `CancelTransaction` reverting newest-first
(`EditorHistory.cs:104-113`) as the Escape path. No new machinery.

**Cursor + headless channel.** `CursorInputComponent` carries all three coordinate
spaces, held state (`LeftButton`), press/release edges, per-frame `Delta`,
`ScrollWheelDelta`, and `OutsideViewport` (`CursorInputComponent.cs:5-34`).
`EditorOpPlan` scripts `MoveCursor`/`LeftDown`/`LeftUp` per frame plus
`ToolbarAction` by name (`EditorOpPlan.cs:69-108`) — a multi-frame drag is already
expressible (see `HeadlessEditorOpTests.cs:176-182`), so **brush strokes are
headless-scriptable today** the moment a brush system consumes the injected drag.
No right/middle/scroll ops exist (none needed below).

**Proxy generalization seam.** `GizmoProxyComponent` is
`(Target, ProxyBindingKind, Index)` with `Index` reserved for sub-elements
(`GizmoProxyComponent.cs:46-61`). Selection border-picking is already
kind-agnostic (`SelectionSystem.cs:212` via `ProxyGeometry.TryGetWorldOutline`);
a new kind needs: an enum member, a `ProxyGeometry.TryGetWorldOutline` case
(`ProxyGeometry.cs:43-59`), snapshot/write-back cases in
`GizmoSystem.TrySnapshotProxyBinding`/`ApplyProxyDragEdit`
(`GizmoSystem.cs:325-345,393-420`), and proxy lifecycle wiring in
`ProxySyncSystem` — which is currently hard-coded to **one proxy per kind**
(`ProxySyncSystem.cs:91-92,130-133`) and must be generalized to a
`(kind, index)` list before spline control points (Wave F slice 1).

**`sources[]` is live schema, dead code.** `SceneData.Sources` is
`List<JsonElement>` (`SceneData.cs:44-45`); nothing writes or reads it — it
round-trips opaquely. Wave D defines its first typed entries.

**Restart survives correctly.** `EditorTransport.Restart` disposes everything not
tagged `EditorInfrastructureComponent`/cursor/`KeepAlive`
(`EditorTransport.cs:107-150`). Brush ghosts, palette chrome, canvas *entities*
are editor infrastructure or bake products; **source authoring entities are scene
content** and are correctly swept + rebuilt by the reload.

---

## Shared architecture for B–F (the three new seams)

Three cross-wave mechanisms fall out of the repass. They are introduced by the
first wave that needs them (noted per wave) and are the "uniquely ECS" backbone.

### S1 — Tool modality: one editor-state entity, one mode field

The editor already owns a single state entity carrying `GizmoStateComponent`
(`EditorOverlay.cs:118-129`). Extend it (per the "prefer adding fields" convention):

- `GizmoStateComponent` gains **`EditorToolMode Mode`** —
  `SelectTransform` (default) `| Place | Scatter | GroundPaint | Road`. The
  toolbar's tool buttons become a radio group over this field
  (`EditorToolbarAction` gains `ToolPlace`/`ToolScatter`/`ToolGround`/`ToolRoad`;
  dispatch cases in `EditorOverlay.DispatchToolbarAction`, buttons in
  `EditorChromeBuilder.DefaultButtons`).
- A new **`BrushStateComponent`** on the same entity holds the brush parameters
  shared by stroke tools: `Radius`, `SpacingFraction` (stamp every
  `SpacingFraction × Radius` of arc length, Photoshop-style, default 0.25),
  `Falloff` (0 = hard, 1 = soft), and the selected palette item / material index.
- **Modality rule:** `SelectionSystem` and `GizmoSystem` process viewport presses
  only when `Mode == SelectTransform`; brush/road systems only in their mode. This
  extends the existing click-ownership design (`GizmoStateComponent.PressClaimed`,
  premises "Selection picks MAX final LayerDepth…") with a coarser, unambiguous
  layer: mode decides which tool family owns the press *at all*; `PressClaimed`
  keeps resolving handle-vs-scene within `SelectTransform`. This mirrors Unity/
  Godot: activating a brush visibly deactivates the transform gizmo.

### S2 — Parametric sources are world data: authoring entities ⇄ `sources[]`

The decided format is `sources[]`; the decided runtime rule is bake-never-evaluate.
The ECS-native realization: **each `sources[]` entry materializes in-world as an
invisible authoring entity carrying a typed source component**
(`ScatterSourceComponent`, `GroundSourceComponent`, `RoadSourceComponent` — pure
data). Systems, proxies, and undo commands then operate on ordinary components:

- **Save:** `SceneWriter` gains a **source-serializer registry** (kind key →
  `(componentType, write, read)`, mirroring `ComponentSerializerRegistry`'s opt-in
  + loud-warning stance). Entities carrying a registered source component are
  **projected into `sources[]`** (`{ "kind": …, "id": …, "data": { … } }`) and
  **excluded from `entities[]`**. Unknown `kind`s read back as raw `JsonElement`
  and re-save verbatim (forward-compatible, mirroring "a reader ignores unknown
  entries" in `scene-format.md`).
- **Load:** `SceneReaderSystem` materializes one authoring entity per entry, then
  publishes a **`SourceBakeRequest { SourceId }`** for kinds whose bake output is
  not persisted (ground, road). Entity-output kinds (scatter) do **not** auto-bake
  — their bake is already in `entities[]` (see S3).
- **Linkage:** baked entities carry **`BakedFromSourceComponent { SourceId }`**
  (registered, key `core.BakedFrom`) so re-bake/erase/select-stroke can find a
  source's output. `SourceId` is a per-scene monotonic int persisted in the entry.
- **Bake is message-driven, never per-frame.** Bake systems subscribe to
  `SourceBakeRequest` (published on stroke commit, control-point edit commit, and
  scene load) — the same synchronous-subscription shape as `EntitySpawnSystem`.
  Nothing evaluates a spline or a seed in `Update`.
- **Bake-on-load runs in both run modes.** A shipped game loading a native scene
  with ground/road sources must bake them too (the editor is part of the game —
  the bake systems are scene-loading participants, not Edit-gated tooling). They
  are `RunNormally`, do work only when a request arrives, and live in
  `level-editor` (the module already owns the scene reader).

Why not "just serialize source components through the normal registry into
`entities[]`"? It would work mechanically, but `sources[]` keeps the file honest:
`entities[]` is exactly what exists at runtime after load (baked truth), and
`sources[]` is exactly the re-edit metadata — a reader that knows nothing about
tools can still load the baked world. This is the decided format put to work, not
a new decision.

### S3 — The persistence rule per output type

| Tool | Source (in `sources[]`) | Bake output | Persisted where | Re-bake on load? |
|---|---|---|---|---|
| Scatter (D) | stroke path + seed + brush/jitter settings | real entities (sprites via factories) | `entities[]` (with `core.BakedFrom`) | **No** — baked entities are truth; re-bake only on explicit re-edit |
| Ground (E) | ordered stroke list (path samples + brush settings + material) | pixels in canvas RTs | **not persisted** (RTs aren't serializable; null-AssetKey sprites reload invisible) | **Yes** — replay strokes into the canvas |
| Road (F) | control points + width + style + spacing | stamps into the ground canvas | not persisted (same as ground) | **Yes** |

Premise this creates: *"Entity-output sources persist their bake in `entities[]`;
pixel-output sources re-bake at load, in both run modes."*

---

## Wave B — stroke-sampling input layer

**Goal.** The shared brush substrate: while a brush-family tool is active, a
left-button drag inside the game viewport is sampled by arc-length spacing into a
stream of stamp events, with radius/falloff parameters, dispatched to whichever
tool systems subscribe — and the whole stroke is one undo step.

### B1 — How the substrate serves it (exact seams)

- **Cursor:** `CursorInputComponent.WorldPosition` + `LeftButton`/`LeftButtonPressed`/
  `LeftButtonReleased` + `OutsideViewport` are everything a sampler needs; the
  viewport-inset mouse mapping already nulls margin clicks
  (`ViewportManager.ScaleMouseToVirtualCoordinates` → `OutsideViewport`), so brush
  strokes can't leak into the chrome.
- **Registrar/overlay:** the sampler is one new update-pipeline entry,
  `editor.brushStroke`, constructed by `EditorOverlay` and exposed as a hook
  property (like `Gizmo`), woven **before `editor.gizmo`** (so the same frame's
  mode/claim is coherent for selection at draw-time, exactly like the gizmo's
  update-before-draw claim contract) and Edit-guarded (inert in Play).
- **Undo:** `EditorHistory.BeginTransaction` on stroke start /
  `CommitTransaction` on release / `CancelTransaction` on Escape or mode/transport
  exit — verbatim the gizmo drag pattern (`GizmoSystem.cs:312,373,430`).
- **Toolbar/chrome:** tool buttons + `[`/`]` size keys ride `EditorToolbarAction`,
  `EditorChromeBuilder.DefaultButtons`, and `DefaultEditorKeys` (hosts with their
  own mapping wire their own `EditorInputBindings`-style predicates).
- **Editor-op channel:** nothing to add — `ToolbarAction` selects the brush by
  name, `LeftDown`/`MoveCursor`/`LeftUp` script the drag
  (`EditorOpPlan.cs:69-108`). Brush strokes are headless-replayable from day one.
- **Overlay visuals:** the brush cursor ring (world-radius circle under the
  pointer) is emitted by the existing `editor.overlayPrep` draw entry through
  `OverlayProjection` (geometry moves with zoom, stroke width stays fit-scaled),
  clipped by `OverlayMeshClip` — the same native-resolution path as gizmo/proxy
  visuals.

### B2 — What is genuinely open (and calls)

- **Where stroke geometry lives while dragging:** private system state (like the
  gizmo's drag state) — chosen. It is per-interaction frame state, not world data;
  the *committed* stroke becomes world data in D/E via source components.
- **Sampling space:** world space (stamps land in the world; zooming out naturally
  spaces stamps farther apart in screen terms but identically in world terms —
  matches Unity terrain brushes, which are world-sized).
- **Stroke across the viewport edge:** pause sampling while `OutsideViewport`,
  resume on re-entry within the same stroke (no stamp interpolation across the
  gap). Simple and predictable.

### B3 — The uniquely-ECS shape

The brush is **not** an object with callbacks; it is a *message topology*:

- `BrushStrokeSystem` (the sampler) publishes three messages:
  `BrushStrokeStarted { StrokeId, Seed, Mode, BrushState snapshot }`,
  `BrushStamp { StrokeId, IndexInStroke, WorldPosition, Radius, Falloff, Mode }`,
  `BrushStrokeEnded { StrokeId, Cancelled }`.
- Tool systems (D's scatter, E's ground) are ordinary systems subscribing via
  `World.Subscribe<BrushStamp>` and acting only when `Mode` matches — the exact
  dispatch shape `EntitySpawnSystem` already uses (synchronous, in-stack). A
  gamedev adds a **custom stamp behavior** by writing one system + one
  subscription — no interface registry, no editor plumbing; it appears in the
  systems panel like everything else and can be toggled live.
- The pure math is `Brush/StrokeSampler` (arc-length accumulator with remainder
  carry across polyline segments; first stamp at the press point) — the
  `GizmoTransform`/`CameraNav` testable-pure-class split, unit-tested with no
  world.
- The **systems panel is the tool-tuning surface**: because the sampler and each
  stamp consumer are separate registrar entries, a designer can disable e.g. the
  scatter consumer and drag strokes that produce nothing but the (future) stroke
  debug overlay — live pipeline introspection as a brush debugger, for free.

### B4 — Familiar-outside UX

- Photoshop/Unity-terrain brush grammar: **radius ring cursor**, `[`/`]` size,
  spacing as a fraction of radius, hold-drag to paint, **Escape cancels the
  in-flight stroke** (one `CancelTransaction` — most engines can't cleanly cancel
  mid-stroke; bounded-undo transactions make it trivial here).
- One stroke = one undo step (Unity/Krita/Photoshop convention) — already the
  drag-coalescing contract.
- Activating a brush deactivates the gizmo visibly (mode radio in the toolbar,
  pressed-state tint) — Unity/Godot modality.

### B5 — Slice plan (dependency-ordered; tests per slice)

1. **Tool modality.** `EditorToolMode` on `GizmoStateComponent` +
   `BrushStateComponent` + toolbar radio actions + `SelectionSystem`/`GizmoSystem`
   mode early-outs. *Tests:* world-based — in `Scatter` mode a viewport press
   neither selects nor starts a gizmo drag; switching back restores both
   (extends `SelectionTests`/`GizmoTests` patterns).
2. **Pure sampler.** `Brush/StrokeSampler` (spacing, remainder carry, press-point
   stamp, falloff weight function). *Tests:* pure unit tests (spacing exactness
   across multi-segment paths; zero-length drag = single stamp).
3. **`BrushStrokeSystem` + messages + transaction ownership.** Viewport gating
   (`OutsideViewport` pause/resume), Escape/mode-exit/transport-play →
   `CancelTransaction`. *Tests:* world-based with injected `CursorInputComponent`
   (the `HeadlessEditorOpTests` in-process pattern): stamps at expected world
   positions; stroke opens/commits exactly one history transaction; cancel reverts
   live effects.
4. **Brush ring overlay + size keys.** `editor.overlayPrep` emission +
   `DefaultEditorKeys` `[`/`]` + host bindings. *Tests:*
   `OverlayProjectionTests`-style emission (ring scales with zoom geometry, not
   stroke width); key-edge tests.
5. **Headless smoke + docs.** Op-plan stroke against a probe subscriber; premise
   *"A brush stroke is one coalesced undo transaction owned by the sampler"* with
   `Tests:` filled; overview/flow updates.

---

## Wave C — free entity placement

**Goal.** A palette of game prototypes; click to place a real entity with correct
components, layer, and Y-sort fields; snap support; ghost preview; undoable.

### C1 — How the substrate serves it (exact seams)

- **Factories are the one creation authority** (`level-loading` premise):
  placement calls `factory.CreateEntity(world, new EntitySpawnRequest(id, pos, …))`
  **inside a `CreateEntityCommand` builder** (`CreateEntityCommand.cs:31` takes
  `Func<World, Entity>`; the factory returns the root — verified). The command
  tags `SceneObjectComponent` and snapshots the sub-graph → placed entities
  round-trip through the Wave-A writer and undo/redo restores whole sub-graphs
  (orbiting-orb players included) with zero new persistence code.
- **Snap:** `GizmoStateComponent.SnapEnabled`/`GridStep` (shared instance — the
  toolbar's snap toggle already drives it) quantizes the placement position.
- **Layer/Y-sort correctness is factory-owned:** factories set SOURCE sort fields
  (`SpriteInfo.LayerDepth`/`YSortOffset`) and already accept `layerDepth` via
  `CustomFields` (`TileEntityFactory.cs:24`) — the palette item carries the
  default layer; the derived depth re-computes next prep+sort frame (the
  SOURCE-not-derived premise does the rest).
- **Chrome:** the palette is native-resolution chrome in the **bottom strip**
  (`EditorChromeLayout` already reserves it), built by `EditorChromeBuilder`
  entities (`ScreenPosition` hit-test, `EditorInfrastructureComponent`, no
  `VisibleComponent` — the chrome rule).
- **Restart/transport:** placed entities are scene entities — swept and rebuilt by
  Restart's reload, correctly losing unsaved placements (the documented trade).

### C2 — What is genuinely open (and calls)

- **Palette source of truth:** screen-supplied. `EntitySpawnSystem`'s factory dict
  is private, and the editor module must stay game-agnostic (it never references a
  game type — verified). The screen hands the overlay an
  `EditorPalette` (list of `{ Id, Label, PreviewAssetKey?, IEntityFactory,
  DefaultCustomFields }`) exactly the way it supplies `EditorInputBindings` and the
  toolbar dispatch. *(Open question 6 covers the reference content.)*
- **Ghost fidelity:** the ghost is a sprite-only preview entity (palette preview
  asset, semi-transparent tint, `EditorInfrastructureComponent`, never
  `SceneObjectComponent`) — **not** a factory product (a factory-built ghost would
  carry colliders/physics and require careful un-tagging; not worth it).
- **Drag-from-palette vs click-place:** click the palette to arm, click the
  viewport to place (Godot convention). Placement auto-selects the new entity so
  the gizmo can immediately adjust — Unity's post-drop behavior.

### C3 — The uniquely-ECS shape

Placement is the **authoring path and the runtime path being literally the same
code**: the same `IEntityFactory` that spawns a Wall during LDtk parsing spawns it
under the designer's cursor — there is no "editor prefab" abstraction to drift
from the game. The palette is data handed to a system; the ghost is an entity; the
undo step is the same snapshot mechanism the delete key uses. A gamedev exposes a
new placeable thing by registering a factory — which they already did to make it
spawnable at all.

### C4 — Familiar-outside UX

- Palette strip with click-to-arm, ghost under cursor, click to stamp,
  **Escape/right-click disarms** (Unity/Unreal drag-drop and Godot tile/scene
  placement all converge on this grammar).
- Snap honors the global snap toggle + grid step (same key/button as gizmo snap —
  one snapping model, as in Godot).
- Placed entity lands selected with the move gizmo active.
- Repeated clicks keep placing (Unreal foliage-single-place behavior) until
  disarmed.

### C5 — Slice plan

1. **`EditorPalette` model + bottom-strip chrome.** Overlay accepts the palette;
   builder renders item buttons; click arms `Mode = Place` + selected item.
   *Tests:* `EditorShellTests` pattern — native-pixel bounds, click arms,
   Escape disarms; layout on resize.
2. **Ghost preview system.** `editor.placementGhost` entry; follows cursor
   (snap-quantized), hidden while `OutsideViewport`; despawns on disarm/mode
   exit/transport. *Tests:* world-based ghost lifecycle; snap quantization.
3. **Place = `CreateEntityCommand` around the factory.** Auto-select; layer
   custom-fields from the palette item. *Tests:* place → components/SOURCE fields
   correct; undo removes sub-graph, redo restores (reuses `UndoTests` snapshot
   assertions); placed entity round-trips save→load (`SceneRoundTripTests`
   extension).
4. **Headless placement + docs.** Op-plan: arm via `ToolbarAction`, click, assert
   the entity; premise *"Placement creates through the game's factories inside a
   snapshotting create command"*; overview/flow updates.

---

## Wave D — scatter brush

**Goal.** Paint many instances of a palette prototype along a stroke with seeded,
deterministic jitter (position within radius, rotation/scale/tint variation),
density control — output is real entities that render/Y-sort/save through the
standard pipeline.

### D1 — How the substrate serves it (exact seams)

- **B's stamp stream:** `ScatterToolSystem` subscribes to `BrushStamp` and acts
  when `Mode == Scatter`. Stroke start/end messages open/close nothing extra —
  the sampler already owns the history transaction, so per-instance
  `CreateEntityCommand` pushes coalesce to **one undo step per stroke** (verified:
  `EditorHistory.cs:63-99`).
- **C's palette + factory path:** each instance = the armed palette item's factory
  + jitter applied to `TransformComponent` (rotation/scale) and
  `SpriteInfoComponent.Color` (tint) after creation — both serialize (SOURCE
  fields), so variation round-trips with no extra format work.
- **S2/S3 source infra (introduced by this wave):** on stroke commit, one
  authoring entity with `ScatterSourceComponent { SourceId, PrototypeId, Seed,
  BrushSettings, PathSamples }` is created (inside the same transaction — undoing
  the stroke also removes its source); instances get
  `BakedFromSourceComponent { SourceId }`. Save projects it to a
  `{ "kind": "scatter", … }` entry.
- **Culling/Y-sort:** instances are Main-target sprites → `CullingSystem` culls
  them (verified `CullingSystem.cs:12`); flat props should target a non-Y-sorted
  layer (early-out at `YSortSystem.cs:42-43`), tall props the Y-sorted band —
  a per-palette-item flag.
- **Editor-op channel:** a scripted stroke + fixed seed = a fully deterministic
  scatter — the headless test story below.

### D2 — What is genuinely open → recommendations

- **Seed granularity (the deferred fork — resolve now): per-stroke seed.** Each
  stamp/instance derives its randoms from a pure hash
  `hash(strokeSeed, stampIndex, instanceIndex, channel)` — no `Random` object, no
  sequence state. Baked entities are ground truth (S3): loading never re-rolls
  anything; only an explicit **re-bake** of a stroke (after editing its settings)
  re-rolls that stroke. Per-stroke bounds the blast radius (matches how Unity
  terrain detail painting and Unreal foliage behave under re-paint) and keeps the
  source entry compact. Per-stamp persisted randoms are rejected: ~3× source
  data, erase bookkeeping, and the stability they buy only matters during
  re-bake — which is an explicit, rare act.
- **Density model:** per-stamp instance count derived from
  `Density × stampArea`, plus **min-spacing rejection** within the stroke (spatial
  hash of already-placed instance positions; reject samples closer than
  `MinSpacing`). This is the Y-sort/draw-call hotspot guard at the *authoring*
  end — the cheapest place to enforce it. Defaults per open question 5; the
  brush warns (chrome text) when a stroke hits its instance cap.
- **Eraser (open question 7):** if included — `Mode == Scatter` + a modifier (or a
  toolbar erase toggle) makes stamps push `DeleteEntityCommand`s for
  `BakedFromSourceComponent` instances within the radius; same
  one-step-per-stroke coalescing.

### D3 — The uniquely-ECS shape

- Scatter output is **not a special "foliage system"** (Unity terrain details and
  Unreal HISM foliage are parallel render paths with their own culling): every
  instance is an ordinary entity in the ordinary pipeline — selectable with the
  ordinary gizmo, deletable with the ordinary delete key, saved by the ordinary
  writer. The brush is *only* a faster way to call the factory.
- Determinism is a **pure function, not hidden state**: the jitter math is a
  standalone `Brush/ScatterJitter` class (hash → position-in-disc with falloff
  weighting, rotation/scale/tint ranges), unit-testable and identical headless —
  which turns "does the brush feel random but stable?" into a golden test.
- Re-bake is a **system reacting to a message** (`SourceBakeRequest`): despawn
  `BakedFrom` matches (as delete commands), respawn from the source's path+seed
  (as create commands) — one composite undo entry, so *re-bake itself is
  undoable*. Editors with bespoke scene models fight for this; here it is the
  existing command algebra.

### D4 — Familiar-outside UX

- Unity terrain-detail / Unreal foliage grammar: pick prototype, radius +
  density sliders (chrome), paint; variation ranges (rotation/scale/tint) as
  brush settings; erase with the same brush.
- Scattered things remain individually grabbable afterwards (Godot users expect
  this; Unity terrain users are *surprised* they can't — we advertise it).
- One stroke = one undo (all engines).

### D5 — Slice plan

1. **Source infrastructure (S2/S3, shared with E/F).** Source-serializer registry;
   `sources[]` typed write/read + unknown-kind opaque round-trip;
   authoring-entity projection (excluded from `entities[]`);
   `BakedFromSourceComponent` (+ serializer `core.BakedFrom`);
   `SourceBakeRequest`. *Tests:* golden `sources[]` round-trip; membership rules
   (authoring entity → `sources[]`, never `entities[]`); unknown kind preserved on
   re-save; `ComponentSerializerRegistryTest` pattern for `core.BakedFrom`.
2. **Pure jitter math.** `Brush/ScatterJitter` hash streams + disc sampling +
   min-spacing hash grid. *Tests:* determinism (same inputs → same outputs),
   range respect, spacing property test.
3. **`ScatterToolSystem`.** Stamp consumption, factory spawn + jitter application,
   per-stroke coalescing, density cap warning. *Tests:* world-based — scripted
   stroke produces the exact expected instance set for a fixed seed; one undo
   entry; min-spacing honored; instances carry SOURCE sort fields per palette
   flag (Y-sorted vs flat layer).
4. **Stroke commit → `ScatterSourceComponent` + re-bake.** Source entity in the
   transaction; `SourceBakeRequest` handler (despawn/respawn as one composite).
   *Tests:* undoing a stroke removes instances *and* source; re-bake after a
   settings edit replaces instances deterministically and is undoable; save→load
   keeps instances byte-stable (no re-roll).
5. **Headless integration + eraser (if approved) + docs.** Op-plan scatter stroke
   determinism test (in-process); density-cap premise + seed-granularity decision
   recorded in `roadmap.md`; premise *"Scatter bakes real entities at author time;
   the source is re-edit metadata"* with `Tests:` filled.

---

## Wave E — ground paint

**Goal.** Paint ground/terrain coverage as **pixels, not entities**: strokes lay
down a material with soft/hard falloff onto a persistent world-space canvas that
renders through the normal pipeline, saves as strokes in `sources[]`, and re-bakes
at load.

### E1 — How the substrate serves it (exact seams)

- **B's stamp stream** drives it (`Mode == GroundPaint`).
- **Rendering:** the canvas is chunked `RenderTarget2D`s
  (**`RenderTargetUsage.PreserveContents`** — verified necessity) whose chunks are
  ordinary **sprite entities** (`SpriteInfoComponent.SpriteSheet = chunk RT`,
  world-positioned, Background-band depth, non-Y-sorted, `Target = Main`) — so
  the canvas is culled, composited, and camera-transformed by the standard
  pipeline with **zero new render passes** (C1 holds). Precedent: the nine-patch
  render-to-texture cache (`SpritePrepSystem.cs:76-153`).
- **Bake step:** a draw-pipeline entry `editor.groundBake` (before the render
  passes) flushes a stamp queue into the chunk RTs via `SpriteBatch` (set target →
  draw brush quads → restore). It never clears (accumulation), unlike every
  `MasterRenderSystem` pass (`MasterRenderSystem.cs:142`) — which is exactly why
  it is its own small system and not a render pass.
- **Erase:** same path with a punch-out `BlendState` (zero color,
  `InverseSourceAlpha` alpha) — Reach-safe, **no shader**.
- **Scene format:** `GroundSourceComponent { SourceId, Material, Strokes[] }` on
  one authoring entity per canvas layer → `{ "kind": "ground", … }`; on load the
  reader publishes `SourceBakeRequest` and the bake system replays the strokes
  once (both run modes — S2). Canvas chunk entities are bake products: never
  `SceneObjectComponent`-tagged (a null-`AssetKey` sprite would reload invisible —
  verified `SceneReaderSystem.cs:126`).
- **Undo:** `GroundStrokeCommand` holds before/after pixel snapshots of the
  stroke's touched chunk regions (`Texture2D.GetData`/`SetData` at stroke
  boundaries — stroke-time cost, never per frame); pushed like any command, FIFO
  history cap bounds memory.

### E2 — What is genuinely open → recommendations

- **The deferred fork — alpha stamps into a RenderTarget vs splatmap shader:
  RESOLVE AS STAMPS-INTO-RENDERTARGET (the canvas).** Grounds (verified):
  the engine has **no custom-shader infrastructure** (`BasicEffect` only; the
  `.fx` files are dead code, no `Load<Effect>` anywhere); the web head is
  **Reach/SM3** and engine modules must carry no platform branch; and the mesh
  path a splatmap would likely ride is untextured. The canvas needs only
  `SpriteBatch` + blend states — Reach-safe, shippable on web v1, and it reuses
  the sprite pipeline end-to-end. A splatmap (per-pixel material-weight texture +
  blend shader) remains the *upgrade* path if the material count grows (open
  question 2) — it would be the engine's first custom shader and must resolve the
  head-owned-profile question then, not now. Record: **Wave E = canvas; splatmap
  = named future upgrade, desktop-first.**
- **Chunk geometry:** 1024×1024 texels @ 1 texel/world-unit, allocated on first
  touch, world-grid aligned; one sprite entity per chunk (a full-canvas single RT
  hits texture-size ceilings and wastes memory on sparse paint). Open question 1
  tunes the numbers.
- **Undo mechanism choice:** region snapshots (chosen) vs replay-all-strokes-minus-
  one. Replay is O(total strokes) per undo and grows unboundedly; snapshots are
  O(stroke bbox) and ride the existing bounded history. Snapshots win.
- **Context loss (web):** RT contents are volatile across GL context loss; the
  stroke list is the durable truth → a lost-context re-bake is the same code path
  as load-time bake. This is *why* ground must stay parametric even though its
  output is "baked" pixels.

### E3 — The uniquely-ECS shape

- The canvas is **entities all the way down**: chunks are sprite entities the
  culler/renderer treat like any other — a painted world region scrolls, insets,
  and composites with zero special cases, and the systems panel can literally
  toggle `editor.groundBake` off to freeze painting while leaving the canvas
  visible.
- Paint is **data flow, not canvas API**: `BrushStamp` messages → a queue → one
  bake system. A gamedev who wants a custom ground behavior (wetness map? decals?)
  subscribes another system to the *same* stamp stream — the stroke layer doesn't
  know or care.
- The stroke list is **world data** (a component on an authoring entity), so
  "show me where I painted" debug overlays, stroke-count HUDs, or a future
  stroke-eraser are ordinary systems querying ordinary components.

### E4 — Familiar-outside UX

- Unity terrain-paint / Krita ergonomics: material swatch (palette strip reuse),
  radius + falloff (hardness) + opacity, paint and erase as one tool with a
  toggle, stroke = one undo.
- The painterly, non-tile promise: falloff is baked into the brush stamp texture's
  alpha; overlapping strokes accumulate like real paint (alpha compositing), which
  tile-based editors can't do.
- What ships is what you painted — the canvas renders through the same pipeline in
  Play, no editor-only visualization to distrust.

### E5 — Slice plan

1. **Canvas chunk infrastructure.** Chunk math (pure: world→chunk mapping, region
   intersection), `PreserveContents` RT creation, chunk sprite entities,
   `editor.groundBake` draw entry + stamp queue. *Tests:* pure chunk-math tests;
   world-based allocation-on-touch; Demos-headless observe run asserts a painted
   frame is non-blank at the painted region (`GameTestRunner.RunDemosAsync` +
   `AssertScreenshotNonBlank` pattern).
2. **`GroundPaintSystem` (stamp consumer) + erase blend.** Falloff brush quad,
   material tint, erase `BlendState`. *Tests:* queue contents for a scripted
   stroke; small-canvas `GetData` pixel assertions (painted alpha where expected,
   erase clears).
3. **Pixel undo.** `GroundStrokeCommand` region snapshots inside the stroke
   transaction. *Tests:* paint → undo restores exact pixels (small-canvas
   compare); history cap eviction frees snapshots.
4. **Ground source + load-time re-bake.** `GroundSourceComponent` stroke append on
   commit; `sources[]` `"ground"` entry; `SourceBakeRequest` replay on load (both
   run modes). *Tests:* save→load reproduces the canvas (pixel compare, small
   canvas); a Play-mode load bakes too.
5. **Material selection UI + docs.** Palette-strip material swatches; premises:
   *"Pixel-output sources re-bake at load, in both run modes"*, *"Canvas chunks
   are bake products, never scene-serialized"*; fork resolution recorded in
   `roadmap.md`.

---

## Wave F — road/path tool

**Goal.** Lay a spline by clicking control points; drag points with real handles;
width + style; clean corners; output baked along the spline. A different
interaction model from B (discrete point edits, not strokes).

### F1 — How the substrate serves it (exact seams)

- **Proxies are the handles (the designed-for reuse).** Control points are
  `GizmoProxyComponent` bindings with a new
  `ProxyBindingKind.SplineControlPoint` and `Index` = the point's ordinal — the
  exact generalization the Wave-8b docs reserve. Verified touchpoints: enum member
  (`GizmoProxyComponent.cs:16`), outline case
  (`ProxyGeometry.TryGetWorldOutline`, `ProxyGeometry.cs:43-59`),
  snapshot/write-back cases (`GizmoSystem.cs:325-345,393-420`), and the
  **prerequisite refactor**: `ProxySyncSystem`'s one-proxy-per-kind fields
  (`ProxySyncSystem.cs:91-92`) become a `(kind, index)` proxy list. Selection
  border-picking needs **no change** (kind-agnostic, `SelectionSystem.cs:212`);
  the click-ownership claim, drag coalescing, and Editor-target overlay baking all
  apply unchanged.
- **Undo:** `SplineEditCommand` (before/after of one control point, or of the
  point list for add/delete) mirrors `ColliderEditCommand` — targets the road
  authoring entity, pushed per drag frame in the coalescing transaction.
- **Source (S2):** `RoadSourceComponent { SourceId, ControlPoints, Width, Style,
  Spacing, Tension }` on an authoring entity → `{ "kind": "road", … }`; the
  authoring entity is what you select to edit the road later (its proxies spawn on
  selection, exactly like collider proxies).
- **Bake:** on commit (`SourceBakeRequest`), stamps along the spline into
  **Wave E's ground canvas** — arc-length sampling via **B's pure
  `StrokeSampler`** (the road is a machine-generated stroke), stamp rotation =
  spline tangent.
- **Live preview without re-baking:** while dragging a point, a vertex-color
  ribbon/centerline from the **existing** `Polyline`/`GradientPath` mesh
  generators (`IMeshGenerator.cs`) is emitted through `editor.overlayPrep` —
  cheap, per-frame-legal (it's overlay chrome, not a parametric source
  evaluation), discarded on commit when pixels bake.

### F2 — What is genuinely open → recommendations

- **The deferred fork — stamps-along-spline vs textured mesh strip: RESOLVE AS
  STAMPS.** Verified grounds: the mesh path is `VertexPositionColor`-only with
  `TextureEnabled` never set — a textured strip **cannot be built today**; a
  vertex-color-only strip is a flat untextured ribbon (wrong for the painterly
  direction); adding UVs is a framework change with a precise, non-trivial
  blast radius (vertex type in `MeshData`/`DrawComponent`, every generator, the
  `BasicEffect` binding, `DrawSingleMesh`). Stamps reuse E's canvas wholesale:
  roads composite into the ground like painted strokes, which *is* the illustrated
  look. **The UV'd mesh path is recorded as a named future `rendering` framework
  change** (it also unlocks textured procedural shapes generally); if approved
  later, the road source is already parametric — re-baking it as a mesh strip is a
  new bake target, not a format change. *(Open question 3.)*
- **Spline type:** Catmull-Rom through on-curve points, `Tension` parameter
  (open question 8). Add point = click (appends after nearest end), insert = click
  on the curve between points, delete = select proxy + Delete key (routes through
  the existing `EditorCommandSystem` delete intent, retargeted to a point when a
  point proxy is selected).
- **Clean corners:** adaptive arc-length sampling — stamp density scales with
  local curvature (tighter turns → closer stamps) so corners stay continuous;
  sharp-corner miters are a mesh-strip concern and deferred with it.
- **Roads that must Y-sort (bridges over entities):** out of scope for F —
  canvas-baked roads are ground-flat by definition. A Y-sorting road would be
  scatter-style stamp *entities* along the spline (the machinery exists: D's
  consumer + B's sampler); noted as a follow-up, not built.

### F3 — The uniquely-ECS shape

- The road is a **component you select**, not a special editor document: the
  authoring entity is in the world, its control points materialize as proxy
  entities through the same `ProxySyncSystem` that serves colliders, its edits are
  the same command algebra, and its bake is a message-driven system. "Add spline
  editing to your own component" becomes: add a binding kind + a geometry case —
  the same three switch sites, now proven twice (colliders, roads).
- The bake pipeline composes across waves like systems should: F produces a
  synthetic stroke (B's pure sampler) consumed by E's paint substrate — three
  waves, one data path, no tool-to-tool coupling beyond messages and pure math.
- Headless: an op plan that arms the road tool, clicks four points, drags one
  proxy, and saves — replayed against the real systems — is a full road-authoring
  regression test with no mouse and no window.

### F4 — Familiar-outside UX

- Godot Path2D / Unreal landscape-spline grammar: click to lay points, drag points
  with visible handles, curve updates live, width as a tool setting; Unity-spline
  users lose tangent handles v1 (Catmull-Rom) — the most common complaint-free
  simplification (Unreal's landscape splines are also point-based by default).
- Point handles look and behave like the collider proxies the designer already
  knows (same cyan-family outlines, same drag feel, same one-undo-per-drag).
- Escape while laying points cancels the pending point; the road remains editable
  forever after (select it → handles reappear) — parity with every spline editor.

### F5 — Slice plan

1. **Proxy generalization.** `ProxySyncSystem` `(kind, index)` proxy list;
   existing box/convex behavior unchanged. *Tests:* all existing `ProxyTests`
   green; N-proxy spawn/despawn/selection-keeps-family for a synthetic multi-point
   binding.
2. **Pure spline math.** `Road/CatmullRom` (eval, tangent, arc-length table,
   curvature-adaptive sampling). *Tests:* pure — interpolation through points,
   tangent continuity, adaptive density on a hairpin.
3. **Road authoring.** `RoadSourceComponent` + road tool mode (click-add/insert,
   `SplineEditCommand`, point delete) + `SplineControlPoint` binding kind cases
   (geometry/snapshot/write-back). *Tests:* world-based — add/drag/delete points
   with undo at each step; proxy drag writes back into the component (the
   `ColliderEditCommand` test pattern).
4. **Live ribbon preview.** `Polyline`/`GradientPath` overlay emission during
   edits. *Tests:* `OverlayProjectionTests` pattern (geometry follows points,
   clipped to viewport).
5. **Bake along spline into the canvas + load re-bake.** Synthetic-stroke
   generation → E's paint path on commit; `sources[]` `"road"` entry; re-bake on
   load. *Tests:* deterministic pixel output for a fixed spline (small canvas);
   save→load reproduces; editing a point re-bakes (old pixels restored via the
   stroke-region snapshot mechanism, then re-stamped) and is undoable.
6. **Headless road authoring + docs.** Full op-plan regression; premises:
   *"Spline control points are gizmo proxies (kind + index), never bespoke handle
   plumbing"*, *"Road output bakes into the ground canvas; the spline is the
   durable source"*; fork resolutions recorded in `roadmap.md`; revisit the
   colliders-as-entities RFC criteria (the proxy layer now has 3 binding kinds —
   re-run its "editing pull" test per the RFC's decision criteria).

---

## Cross-wave invariants (additions to the existing set)

- **One press owner per frame, decided by mode first.** `EditorToolMode` picks the
  tool family; within `SelectTransform`, `PressClaimed` keeps arbitrating
  handle-vs-scene. No system processes a press outside its mode.
- **A stroke is one transaction, owned by the sampler.** Tool systems push
  commands; they never begin/commit/cancel the stroke transaction.
- **Entity-output sources persist their bake in `entities[]`; pixel-output sources
  re-bake at load, in both run modes.** Bake is message-driven
  (`SourceBakeRequest`) — nothing evaluates a source in a per-frame `Update`
  (the standing perf rule).
- **Bake products never scene-serialize.** Canvas chunk entities (and any future
  runtime-texture product) are re-derived from `sources[]`; a null-`AssetKey`
  sprite in `entities[]` is a bug, not a save.
- **Determinism is pure-function.** All brush randomness derives from
  `hash(seed, indices…)` — no `Random` state, identical headless and interactive.
- **Web capability stays a gate.** Every wave above ships Reach-safe (no shaders,
  16-bit-index-safe, blend-state-only). The two named desktop-first future
  upgrades (splatmap shader, UV mesh path) are framework changes with their own
  approval, never module-level platform branches.

## See also

- [`roadmap.md`](roadmap.md) — Waves A + 6–8b history; its B–F sections are
  superseded by this doc.
- [`MonoDreams/level-editor/docs/scene-format.md`](../../MonoDreams/level-editor/docs/scene-format.md)
  — the `sources[]` slot Wave D types.
- [`docs/web-targeting.md`](../web-targeting.md) — the Reach constraints behind
  the E/F fork resolutions.
