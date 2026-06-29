# Level-editor roadmap — Waves A–F

> The cross-session continuity artifact for the in-game level editor. It maps the
> full vision (an illustrated/painterly, non-tile 2D authoring experience delivered
> *inside* the running game) into waves, and for each wave names its **seam**,
> **dependencies**, **decisions made vs deferred**, and **where it plugs into the
> foundation** (the run-state model, the native scene format, the serializer
> registry). A fresh Claude Code session should be able to implement Waves B–F from
> this doc plus the per-module premises — without re-deriving the architecture.
>
> Read first: [`docs/CORE_TENETS.md`](../CORE_TENETS.md) §9 "The editor is part of
> the game"; [`MonoDreams/level-editor/docs/overview.md`](../../MonoDreams/level-editor/docs/overview.md)
> and [`premises.md`](../../MonoDreams/level-editor/docs/premises.md);
> [`MonoDreams/level-editor/docs/scene-format.md`](../../MonoDreams/level-editor/docs/scene-format.md).

## The two cornerstones (hold across every wave)

- **C1 — The editor is part of the game.** It is an in-game *mode* running the real
  pipeline (`CullingSystem → SpritePrepSystem → YSortSystem → MeshPrepSystem →
  TextPrepSystem → MasterRenderSystem`) and the real `Camera`. No parallel editor
  renderer; no second data model. The editor previews exactly what ships.
- **C2 — Editor tooling = ECS systems + components over the game's systems**, gated
  by the engine-wide run-state model (`GameState.RunMode` Play/Edit + `EditTimeBehavior`
  policy + the `GatedSystem` decorator). The contract is codified in the engine docs
  because it is the gamedev's AI agents that deliver later waves.

## The three foundation seams every wave plugs into

| Seam | Where it lives | Status |
|---|---|---|
| **Run-state model** — `GameState.RunMode`, `EditTimeBehavior`, `GatedSystem` | `MonoDreams/foundation/` (`State/GameState.cs`, `System/EditTimeBehavior.cs`, `System/GatedSystem.cs`) | **Live (Wave 1).** Tests: `MonoDreams.Tests/Foundation/RunStateGatingTest.cs`. |
| **Native scene format** — `version` / `camera` / `layers[]` / reserved `sources[]` / `entities[]` with `components{}` + `parent` | `MonoDreams/level-editor/Serialization/SceneData.cs`; spec in `docs/scene-format.md` | **Live (Wave 2).** |
| **Component-serializer registry** — opt-in `Type`→(write,read) by stable key; engine ships its serializers, game registers its own | `MonoDreams/level-editor/Serialization/ComponentSerializerRegistry.cs` + `EngineComponentSerializers.cs` + `SceneSerializer.cs` | **Live (Wave 2).** Tests: `MonoDreams.Tests/LevelEditor/ComponentSerializerRegistryTest.cs`. |

> **Implementation waves vs feature waves.** The *implementation* of the authoring
> substrate is split into the ledger's Waves 1–5 (foundation → registry → round-trip
> persistence → interactive editor → headless channel). The *feature* roadmap below
> (A–F) is the product vision: **Wave A = the whole authoring substrate** (ledger
> Waves 1–5), and **B–F** layer the three brush tools and their richer outputs on
> top. This doc is the feature roadmap.

---

## Wave A — authoring substrate (foundation + manual placement editor)

**Goal.** A working in-game editor mode: enter `Edit`, see the live game frozen,
select/move/rotate/scale entities with a gizmo, undo/redo, save and load a native
scene. No brushes yet — entities are placed/edited individually.

**Seam.** A purpose-built reference editor screen (`MonoDreams.Examples.Core`)
composing editor systems over the run-state-gated game pipeline in the **same world**.
Editor systems are pre-registered (inert in `Play`); flipping `RunMode` enters/exits
editing with no screen swap.

**Dependencies.** All three foundation seams above; `rendering` (gizmo meshes,
preview), `cursor` (selection/drag read `CursorInputComponent.WorldPosition`), `ui`
(`AutoLayoutBuilder` toolbar on UI/HUD), `level-loading` (`IPlatformServices` storage;
the new `LoadSceneRequest` message, separate from `LoadLevelRequest`).

**Decisions made.**
- **GAP-A = full component serialization** via the registry; round-trip reconstructs
  from components, never by re-running factories; the `ChildOfComponent` graph
  round-trips (sub-graphs like Player + orbiting children survive a save/load).
- **`Texture2D` → an additive optional `AssetKey`** on `SpriteInfoComponent` (the
  content key, never the live texture; rehydrated via `ContentManager.Load` on load).
- **Persisted sort fields are SOURCE** (`SpriteInfo.LayerDepth`/`YSortOffset`/
  `YSortDepthBias`), never the per-frame-derived `DrawComponent.LayerDepth`.
- **Native load is a dedicated `LoadSceneRequest`**, never `LoadLevelRequest` (which
  is LDtk-coupled and would clobber via the unconditional LDtk `Content.Load` /
  `Remove<CurrentLevelComponent>` path).
- **Selection topmost = MAX final post-YSort `LayerDepth`** with a selection-owned
  deterministic tiebreak (the renderer's insertion index is private).
- **Editor chrome = engine-native retained-mode UI** (`AutoLayoutBuilder`), web-capable,
  not ImGui. On the UI/HUD render target (not Main, `AutoLayoutBuilder`'s default).
- **Editor-overlay entities are standalone** — never `ChildOfComponent`-parented to
  game entities (so `HierarchySystem.DisposeOrphans`, live in `Edit`, can't
  cascade-dispose them); gizmo/selection meshes set `VisibleComponent` themselves.
- **Bounded undo** (configurable cap, FIFO eviction) with drag-coalescing (one drag =
  one step); undo entries are DATA + an applying system, not OO command objects.

**Decisions deferred.**
- **GAP-B = Blender-origin save deferred.** Blender-direct entities are untagged in
  Wave A → view-only; editing a Blender level can't save those props yet. Revisit when
  a content-driven format dispatch replaces the `Blender_` prefix hack.

**Plug-in points.** Game components register serializers on the registry
(`registry.Register(key, type, write, read)`); a screen opts a system into freezing by
wrapping it `GatedSystem(child, EditTimeBehavior.Freeze)`. `SceneObjectComponent` tags
save-roots (Wave 3); the writer serializes the tagged closure.

### Post-Wave-A usability (editor camera + menu entry)

Two usability gaps surfaced once the substrate was exercised, closed without a new wave:

- **Editor camera navigation is now part of the substrate.** `CameraNavSystem` (+ the pure
  `Navigation/CameraNav` math) drives the camera in `Edit`: middle-mouse **pan**, scroll-wheel
  **zoom** (clamped 0.25–4.0), and a **frame-scene** key that centres + zoom-fits the camera on the
  AABB of all content — the affordance that makes off-origin levels (e.g. `Blender_Level` at
  ~(1275,-530)) reachable. Edit-guarded; ordered before `CursorPositionSystem`. The editor owns the
  camera in `Edit` (`CameraFollowSystem` stays `Freeze`-gated), which is what made this an editor
  responsibility rather than a play-pipeline one.
- **Menu entry into the editor.** The reference menu reaches the editor through the existing
  `ScreenTransitionRequest` path — a per-level "Edit" button publishes
  `ScreenTransitionRequest(ScreenName.LevelEditor, levelId)`, reusing the generalized transition
  handler (no new screen-swap plumbing, no `Game1` hand-editing).

---

## Wave B — scatter tool (entity brush)

**Goal.** Paint many entities along a stroke (the "scatter" brush metaphor): a brush
stamps instances of a chosen prototype (e.g. foliage, rocks) with jitter on
position/rotation/scale.

**Seam.** A `ScatterTool` editor system reads the cursor stroke and creates entities
via the existing `IEntityFactory` / `EntitySpawnRequest` authoring path (factories
remain the one creation authority); each scattered entity gets `SceneObjectComponent`
so it round-trips through the Wave-A registry/writer unchanged. A scatter **source
descriptor** (brush settings + seed) is stored in the scene's reserved `sources[]`.

**Dependencies.** Wave A (registry, scene format, gizmo/undo); `level-loading`
(factory authoring path); `cursor` (stroke sampling).

**Decisions made.** Scattered output is **baked entities** (real `entities[]`), so it
renders and Y-sorts through the real pipeline with no new render path. The brush is
parametric (a `sources[]` descriptor), but the *output* is baked at author time.

**Decisions deferred (render fork — resolve in this wave).**
- **Scatter seed granularity** — does the `sources[]` descriptor store a single seed
  for the whole stroke, or a per-stamp seed list? Single seed = compact + reproducible
  but re-baking after a brush-setting edit re-randomizes; per-stamp = stable under
  edits but larger. **Pick when implementing B**; record the choice here.

**Constraint.** Dense scatter is a Y-sort hotspot — `YSortSystem` is per-frame per
visible entity and re-sorts on camera move. **Bake at author time; never re-evaluate
the scatter source per frame** (the unifying performance rule). Wave-A entity counts
are hand-scale; scatter is the first wave that can produce enough entities to matter.

**Plug-in points.** The `sources[]` array (reserved + documented in `scene-format.md`)
gains its first concrete entry kind here; the reader must learn to re-bake a scatter
source on load (or trust the baked `entities[]` and treat `sources[]` as re-edit
metadata — decide with the seed-granularity fork).

---

## Wave C — ground tool, parametric pass (pixel brush, stamp-composited)

**Goal.** Paint ground/terrain coverage (the "ground" brush): a pixel-coverage brush
that lays down ground material along strokes.

**Seam.** A `GroundTool` editor system; ground strokes recorded as a `sources[]`
descriptor and composited as **stamp sprites** (many `SpriteInfoComponent` entities)
in this wave — the simplest path that reuses the real sprite pipeline.

**Dependencies.** Wave A; Wave B's `sources[]` precedent; `rendering` (sprite stamps).

**Decisions made.** Stamp-composited ground (sprite entities) so it ships on the
existing render pipeline with no shader/render-target work.

**Decisions deferred (render fork — the C→E split).**
- **Ground splatmap-vs-stamps.** Wave C uses **stamps** (sprite entities). The richer
  **splatmap** path (a per-pixel coverage texture blended in a shader) is **Wave E** —
  deferred because it needs a custom render target + shader, and the **web Reach
  profile** constrains shader features (`GraphicsProfile.Reach` on BlazorGL — see
  `docs/web-targeting.md`). Record in E whether the splatmap is desktop-only or has a
  Reach-safe fallback.

**Constraint.** Stamp count can grow large — bake stamps at author time, keep the
ground source out of the per-frame path.

---

## Wave D — road tool (spline brush)

**Goal.** Draw roads/paths (the "road" brush): a spline the designer lays down; the
road follows the spline.

**Seam.** A `RoadTool` editor system; the road is a `sources[]` spline descriptor
(control points + width); output baked as **stamp sprites** along the spline in this
wave (mesh output deferred to F).

**Dependencies.** Wave A; the `sources[]` precedent (B/C); `rendering` (stamps).

**Decisions made.** Spline stored as control points in `sources[]`; output baked.

**Decisions deferred (render fork — the D→F split).**
- **Road mesh-vs-stamps.** Wave D uses **stamps** (sprite entities along the spline).
  The **mesh** road (a generated `VertexPositionColor` strip following the spline) is
  **Wave F** — deferred because of the mesh-no-UV constraint below.

**Constraint (perf).** A long road = many stamps; dense scatter + roads are the
Y-sort hotspots flagged for this wave. Bake at author time; never re-evaluate the
spline source per frame.

---

## Wave E — ground splatmap (shader/render-target pass)

**Goal.** Upgrade the ground tool from stamps (Wave C) to a **splatmap**: a per-pixel
coverage texture, blended in a shader, for smooth painterly ground.

**Seam.** A custom render target + a blend shader, composited into the Main draw stack
(still the real pipeline — C1 holds; the splatmap is a draw element, not a separate
renderer). The ground `sources[]` descriptor gains a coverage-texture reference.

**Dependencies.** Waves A + C (the ground source + stamp fallback); `rendering`
(render targets, custom effects).

**Decisions deferred → made here.**
- **Ground splatmap-vs-stamps** (carried from C): this wave implements the splatmap;
  Wave C's stamps remain the fallback.

**Constraint (the load-bearing one).**
- **Shader / web-Reach.** The web head runs `GraphicsProfile.Reach` (BlazorGL / WebGL
  ES2 — see `docs/web-targeting.md` and the `foundation` premise "The platform is
  selected by the head"). Reach restricts shader model + features (and rejects 32-bit
  mesh indices). **Decide in E:** is the splatmap shader Reach-safe, or is the splatmap
  desktop-only with the Wave-C stamp path as the web fallback? Engine source carries no
  `#if MONODREAMS_WEB` and no `GraphicsProfile` literal — any platform branch lives in
  the head, so a desktop-only splatmap must degrade gracefully to stamps on web without
  a module-level conditional.

---

## Wave F — road mesh (procedural mesh pass)

**Goal.** Upgrade the road tool from stamps (Wave D) to a **generated mesh** strip
following the spline, for clean continuous roads.

**Seam.** A road `IMeshGenerator` producing a `VertexPositionColor` strip from the
spline `sources[]` descriptor, rendered as a mesh `DrawComponent` through the existing
`MeshPrepSystem` / `MasterRenderSystem` mesh path (C1 holds — no new renderer).

**Dependencies.** Waves A + D (the road source + stamp fallback); `rendering`
(`IMeshGenerator`, `MeshData`, the mesh draw path).

**Decisions deferred → made here.**
- **Road mesh-vs-stamps** (carried from D): this wave implements the mesh; Wave D's
  stamps remain the fallback.

**Constraint (the load-bearing one).**
- **Mesh has no UV.** The engine's mesh path is `VertexPositionColor` **only — no UV /
  texture-coordinate path** (see the spec's codebase findings and `DrawComponent`'s
  mesh fields). A road mesh is therefore **vertex-colored**, not textured, until a UV
  mesh path is added to `rendering` (a separate framework change, not a level-editor
  change). Mesh rendering on web must also use 16-bit indices (the Reach 32-bit-index
  limit — `DrawComponent.Get16BitIndices()` already handles this for procedural meshes).

---

## Cross-wave invariants (the things that must keep holding)

- **C1/C2 never break.** No wave introduces a parallel renderer or a second scene model.
  Every brush output renders through the real pipeline; every tool is an ECS system gated
  by `RunMode`.
- **Bake, never re-evaluate per frame.** Every parametric source (scatter/ground/road)
  is baked at author/load time. `sources[]` is re-edit metadata; the per-frame path sees
  only baked `entities[]` / draw elements. Y-sort is the first-order cost (per-frame, per
  visible entity, re-sorts on camera move).
- **Round-trip is component serialization.** Every wave's output entities carry
  registered components and round-trip through the Wave-2 registry; a new serializable
  component type adds its serializer (engine: `EngineComponentSerializers`; game:
  `registry.Register`). Unregistered components are skipped-with-warning on write and
  fail-loud on load.
- **Web-capability is a gate, not an afterthought.** The shader (E) and mesh (F) forks
  are exactly where the web Reach profile bites; resolve the desktop-vs-web story in the
  wave that introduces the fork, and keep the branch in the head (never in a module).

## See also

- [`MonoDreams/level-editor/docs/scene-format.md`](../../MonoDreams/level-editor/docs/scene-format.md) — the native scene schema (the `sources[]` array B–F extend).
- [`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md) — the live + planned invariants.
- [`docs/CORE_TENETS.md`](../CORE_TENETS.md) §9 + [`docs/flows/level-editor.md`](../flows/level-editor.md) — the run-state contract + the interaction matrix.
- [`docs/web-targeting.md`](../web-targeting.md) — the Reach-profile limits the E/F forks must respect.
```
