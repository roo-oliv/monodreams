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
- **GAP-B = Blender-origin save deferred — OBSOLETE (wave BR).** This gap existed only for
  entities the live Blender parser produced; that parser was deleted in wave BR, so there are no
  Blender-origin runtime entities left to save. The committed `Blender_Level` is a native scene whose
  entities are ordinary tagged save-roots.

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
- **Menu entry into the editor — RETIRED by the transport model.** The per-level "Edit" buttons,
  `ScreenName.LevelEditor`, and `LevelEditorScreen` are gone: the run flag is the ONLY door, the
  editor is always visible under it, and `EditorTransport` (toolbar Play/Pause + Restart, headless
  `Play`/`Pause`/`Restart` ops) owns `RunMode` — Restart rebuilds the scene from the
  screen-recorded original load request and discards unsaved edits (see the transport premise in
  `MonoDreams/level-editor/docs/premises.md`).

### Wave 6 — composition seam (registrar + overlay + run flag)

Direct user feedback after hands-on testing: the editor must be one flag away in any game
screen, and the pipeline must become enumerable/toggleable for the upcoming systems panel.
Shipped under `MonoDreams/level-editor/Composition/`:

- **`EditorPipelineRegistrar`** — a screen registers each pipeline entry
  (`Add(name, system, policy)`), every entry is wrapped in a `GatedSystem` per its policy, and
  the registrar retains the ordered, named entry registry at runtime (`Entries`,
  `SetEnabled(name, bool)` = a both-modes master toggle on the gate's `IsEnabled`). **This is the
  seam the systems-panel wave binds to.** The edit-mode default is a registration-site
  declaration (never an interface on the system type); a declaration contradicting the policy
  throws until the runtime per-mode policy override lands (deliberate follow-up).
- **`EditorOverlay`** — the whole editor block (shared registry/serializer/history + gizmo-state
  entity, every editor system incl. the draw-side `Selection` and the plan-gated headless
  driver, the HUD toolbar + its dispatch) as reusable, individually-woven hooks over the
  screen's own world. `BindPipelines` hands it the screen's registrars for the panel.
- **`EditorRunFlag`** — `--editor` launch arg / `MONODREAMS_EDITOR=1` env var (Rider run
  configuration friendly): the desktop head registers every screen with `editorEnabled: true` and
  boots the transport Paused (`ScreenController.State.RunMode = Edit`). One composition path, no
  duplicated pipeline definition. (The interim `LevelEditorScreen` subclass and the F1 toggle were
  later retired by the transport model.) Deferred: the web head's flag (browsers
  have no args/env — needs a query-string switch via JS interop) and the InfiniteRunner overlay
  (no cursor pipeline; runner systems mutate transforms outside the Freeze block — needs its own
  policy-matrix pass). *(Both InfiniteRunner deferrals resolved in Wave 8a — see below; the web
  flag remains open.)*

### Wave 8a — universal overlay + systems panel

Direct user feedback: "the editor shouldn't care what screen we're using" and "we should be able
to see the ECS systems pipeline and manually activate or deactivate them". Shipped:

- **Universal overlay.** Under the run flag EVERY Examples screen composes the `EditorOverlay`
  through the registrar — `LevelSelectionScreen` (menu policies: `ui.interaction` Freeze so a
  click belongs to the editor; `layout` RunNormally, it is the menu's content placement) and
  `InfiniteRunnerScreen` (whole simulation block Freeze; the overlay provides its own cursor
  pipeline via `provideCursorPipeline` — the Wave-6 blocker) included. Screens without sprite
  prep gain the cull → sprite-prep → Y-sort chain under the flag so loaded scenes preview.
- **Target-aware selection + gizmo.** UI/HUD-target sprites hit-test the cursor's
  `VirtualPosition` (their transforms are virtual coordinates) and, on overlap, the composite
  order wins (Main < UI < HUD < Scroll); the gizmo drags a UI/HUD-target entity in virtual space
  with its overlays on the entity's own target (move/rotate/scale — the math is space-agnostic).
- **Systems panel** (`SystemsPanelSystem` + pure `SystemsPanelLayout`, in the shell's right
  strip): every registrar entry of BOTH pipelines, in order, as name + policy tag + live enabled
  checkbox; a row click toggles via `SetEnabled`; wheel scrolls whole lines; the panel refuses to
  disable its own entry. The per-mode runtime policy override (Wave-6 deferral) remains the
  natural follow-up so the panel can express "on in Edit, off in Play" instead of the both-modes
  master switch.

### Wave 8b — collider gizmo proxies (component-local spatial data)

Direct user feedback: clicking a collider's red debug outline did nothing — colliders are
component data (`BoxColliderComponent.Bounds`, `ConvexColliderComponent.ModelVertices`), not
entities, so nothing in the selection/gizmo path could grab them. Shipped:

- **Edit-time gizmo proxies.** `ProxySyncSystem` (entry `editor.proxySync`, woven after
  `editor.gizmo`) materializes one standalone proxy entity per collider on the selected entity —
  `GizmoProxyComponent` is the binding descriptor `(target, ProxyBindingKind, reserved index)` —
  as a cyan world-space outline re-derived every frame; proxies despawn on deselect / mode exit /
  target death. They are picked through the SAME selection ordering (border-only hit-test, so a
  sprite-covering collider never shadows its entity) and dragged through the SAME gizmo path
  (move tool); the write-back is a `ColliderEditCommand` against the **bound game entity**
  (shift `Bounds` / translate all `ModelVertices` via the inverse-transformed world delta,
  refreshing `WorldVertices` + `BroadPhaseAABB`), coalesced to one undo step per drag.
- **The generalization seam for later waves.** A new editable spatial field is a new
  `ProxyBindingKind` + a `ProxyGeometry` derivation case + a write-back case: the **road tool's
  spline control points (Waves D/F) should reuse this mechanism** (kind = control point,
  `Index` = the point's ordinal) rather than inventing per-tool handle plumbing.
- **Deferred:** per-vertex convex editing (the `Index` field is reserved for it); scale-tool
  resize of `Bounds`; drag-by-border (dragging currently grabs the move handle at the shape's
  centre, like any entity).

---

> **Waves B–F superseded (2026-07-03).** The sections below are the original
> sketch, kept for history. The **binding design for the next waves** is
> [`waves-b-f-design-review.md`](waves-b-f-design-review.md), which re-letters
> the waves (B = stroke-sampling input layer, C = free entity placement,
> D = scatter brush, E = ground paint, F = road/spline tool) and **resolves the
> deferred render forks**: ground = alpha stamps into persistent render-target
> canvas chunks (splatmap shader deferred as a named future upgrade); road =
> stamps along the spline baked into the ground canvas (the UV-textured mesh
> strip deferred as a named `rendering` framework change); scatter seed =
> per-stroke. Start any B–F implementation from that doc.

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

## Engine RFC — colliders as child entities with their own Transform (RESOLVED — the CE phase)

> Status: **RESOLVED (2026-07-10) — implemented as the colliders-as-entities phase (CE-A–CE-D) above.**
> The user directed it built with no backwards compatibility; the decision-criteria measurement landed as
> the collision premise's perf smoke (parity-or-better: the entity model reads an already-computed
> `WorldMatrix` instead of per-frame offset math). The Wave-8b proxy alternative it describes is now
> deleted (only convex vertex grips survive). The RFC text below is kept for history — the motivation and
> costs it named are exactly what the CE phase delivered and paid.
>
> Raised during Wave 8b (collider gizmo proxies). The original text follows.

**Proposal.** Model each collider as a **child entity** carrying its own `TransformComponent` +
a shape component, `ChildOf`-parented to the game entity, instead of collider components with
embedded spatial fields (`BoxColliderComponent.Bounds` offsets, `ConvexColliderComponent`'s
local vertices + per-frame `WorldVertices` derivation).

**Motivation.**
- **Authoring-model match with the Blender source.** In Blender a collider is an object (with
  its own transform) parented to the visual — the exporter flattens that into component fields
  today. Collider-as-entity would round-trip the source structure 1:1.
- **Universal gizmo without proxies.** A collider entity has a real `TransformComponent`, so
  the existing selection + gizmo + `TransformEditCommand` path would edit it directly — the
  whole Wave-8b proxy layer (binding descriptor, sync system, `ColliderEditCommand`) becomes
  unnecessary, and per-vertex/sub-shape handles become child entities too.
- **One spatial model.** `WorldVertices`' documented root-level-entity limitation (it reads the
  local `Position`, not `WorldPosition`) disappears — the hierarchy math is `HierarchySystem`'s,
  not the collision module's.

**Costs.**
- **Collision hot-path indirection.** Detection currently iterates `ColliderTagComponent`
  entities and reads collider + transform off the SAME entity, updating `WorldVertices` in
  place with local-position math. Collider children add per-collider entity indirection
  (parent-chain `WorldMatrix` walks, cache-unfriendly hops) inside the tightest per-frame loop
  in the engine, and the resolution write-back gets harder: contacts must correct the
  **parent's** position/velocity from a hit on the **child's** shape.
- **Migration.** Both level parsers (LDtk, Blender), the factories, the scene serializers, the
  debug system, and every `Has<BoxColliderComponent>()` query in game code change shape; the
  one-collider-of-each-type-per-entity premise becomes a children-set contract.
- **Lifecycle coupling.** `DisposeOrphans` cascade rules, undo delete-snapshots, and the scene
  writer's membership closure all now include collider children — mostly free (the sub-graph
  machinery exists), but every path must be re-verified.

**Decision criteria (measure before committing).**
- **Benchmark the hot path with indirection**: `WorldVertices` update + SAT narrowphase for a
  representative scene (hundreds of active convex colliders) in both models — component-local
  (today) vs child-entity (parent-chain world matrix per collider per frame). Collision is a
  per-frame hot path: accept the restructuring only if the regression is negligible at target
  entity counts (or recoverable via caching that doesn't reintroduce the split spatial model).
- **Resolution correctness**: a worked design for child-shape → parent-body position/velocity
  correction (including the freeze-flags gap already documented in the collision premises).
- **Editing pull**: if Waves D–F's sub-element editing (spline control points, per-vertex
  handles) makes the proxy layer grow past a couple of binding kinds, the balance shifts toward
  native child entities; if proxies stay small, the hot path keeps winning.

**Current alternative (shipped, Wave 8b).** Edit-time gizmo proxies: transient standalone
handle entities bound to `(entity, component, field)` by `GizmoProxyComponent`, synced from the
component every frame, writing back through `ColliderEditCommand` + the undo history. Zero
collision-runtime cost (proxies exist only while selected in Edit), zero data-model migration —
the trade is a thin editor-side indirection layer, which is exactly what this RFC would delete.

## Project persistence phase (PS1–PS6) — complete

Ran orthogonally to Waves A–F (it hardens *how* levels are saved/versioned/booted, not *which*
tools produce them). See [`project-persistence-plan.md`](project-persistence-plan.md) for the full
story. **Done:** PS1 canonical byte-stable serializer + stable scene-local ids; PS2 `game.mdproj`
manifest + `EditorProjectContext` (env/walk-up project-root resolution, fail-safe); PS3 Save writes
versioned `.mdscene` into the SOURCE tree; PS4 native-first `LoadLevelRequest` + `/copy:` bundling +
`startScene` boot; PS5 LDtk/Blender import-only + `Blender_Level` migrated + parser-asymmetry closed;
PS6 ship-readiness lint (`SceneLint` — zero `file:` keys) + **zero-touch bundling** (the editor
appends the MGCB `/copy:` entry on first save; `MgcbLevelBundle`) + docs consolidation.

**Deferred (with triggers):**
- **Native tile-layer batching primitive → migrate LDtk `Level_0`.** `Level_0`'s ~21k per-tile
  entities would make a per-entity `.mdscene` a multi-MB artifact; it needs a compact tile-layer
  representation first. Until then it stays import-only and off the reference menu. *Trigger:* a
  native scene needs a large tile grid (or `Level_0` must ship native).
- **Full NPC dialogue-affordance round-trip.** The migrated levels degrade runtime-derived
  affordances that hold live handles — `NPCInteractionIcon` (a live `Entity` ref) and the icon's
  `DynamicTextComponent` (a live font) are excluded, not serialized. *Trigger:* entity-reference
  serialization (index-based, like the `parent` link) + font asset keys land.
- **Scene-name / rename / new-scene UI.** The editor holds one id (manifest `startScene` / `untitled`);
  there is no in-editor rename or multi-level browser. *Trigger:* more than a couple of levels exist,
  or the designer needs to name/switch levels in-editor.
- **Cross-scene references / multi-level graph.** v1 persists + boots single scenes by id; an Exit
  trigger's string identity is the seam. *Trigger:* the first door between two levels.

## UX phase (UX-A–UX-D) — editor shell + project model

Ran orthogonally to Waves A–F (it re-founds *how the editor looks and how scenes are chosen*, not
*which tools produce them*), converging the shell structure on Blender and the visual identity on
Claude Code. The authoritative design is
[`editor-shell-ui-ux.md`](editor-shell-ui-ux.md); the invariants live in
[`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md).

- **UX-A — `EditorTheme` (strict palette + depth stack).** One `MonoDreams/level-editor/UI/EditorTheme.cs`
  is the single source of every color + depth in the module (chrome AND viewport overlays): warm-dark
  `Bg0..Bg4`/`Border*`/`Text*` ramps + intent roles (`Accent` = selected/primary, `Success` = on,
  `Warning`/`Danger` = destructive-adjacent/destructive), precomputed-opaque blends (the premultiplied
  rule), the shared `ControlFill` + `AdvanceHover` interaction recipe, and a source-scan lint forbidding
  any raw `new Color(` / named XNA token outside the theme file.
- **UX-B — tabbed shell (regions, splitters, tabs, scrollbars).** `EditorShellStateComponent` is the ONE
  model for the resizable region sizes + active tab per region + drag ownership; the region rects and the
  viewport inset derive from it. Splitters resize the right strip / bottom shelf; the right strip is
  **Scene | Systems | Project** tabs, the bottom shelf an **Assets** tab; scrollbars appear on overflow.
  (Toolbar de-crowding was scoped here but deferred → UX-E.)
- **UX-C — screen↔scene binding + Scenes panel + dirty gate.** Game screens declare `ScreenInfo(DisplayName,
  BoundSceneId, HostsSceneFiles)` at registration (foundation seam); the Project tab's `SceneCatalog` merges
  bound screens + `.mdscene` files under `LevelsPath`; **switching IS selecting** (no Load action) through the
  one initiator `EditorOverlay.SelectScene`, dirty-gated by the `EditorDialogSystem` `ConfirmSwitch` modal.
  `EditorHistory` gained a monotonic `EditVersion` + `MarkSavePoint` (`IsDirty`), the empty-save guard, and
  `NativeLevelLoader.TryPublishSceneLoad` (the source-first optional load a bound screen runs in `Load`).
- **UX-D — three-action Save dialog + source-first reload (this wave).** The toolbar Save button opens a modal
  chooser — **Save Scene** / **Save Project** (single-scene v1) / **Save Backup As…** (writes a dangling
  `<name>.mdscene`, no bundle/save-point, then reloads the bound scene via Restart) — replacing the deleted
  file-system navigator (`EditorFileBrowser`, the Load dialog mode, the toolbar Load button +
  `EditorToolbarAction.Load`). `NativeLevelLoader.CreateProbe` gained the resolved `EditorProjectContext` so a
  Restart-after-Save resolves **source-first** (the source tree wins over the stale bundle — pre-mortem #5),
  sharing `TryPublishSourceFirst` with UX-C's optional load; the bound menu/runner screens re-run their optional
  scene load inside `Transport.Reload`.

**UX-E was superseded by the UX2 phase.** The planned toolbar de-crowding (relocating the selection-context
actions off the bar) is subsumed by UX2's panel-local headers + context menus (UX2-B/-C/-D moved the
transport + tools + menus into the Scene header and the Order actions into context menus). See the UX2 phase
below.

## UX2 phase (UX2-A–UX2-F) — panel modularity, modes, camera rig

A second UX pass (user-confirmed 2026-07-08) that re-founds the shell layout on Unity/Blender lines and adds
the run-mode split the editor had deferred. Like the UX phase it runs orthogonally to Waves A–F. The
authoritative design is [`editor-shell-ui-ux-2.md`](editor-shell-ui-ux-2.md); the invariants live in
[`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md).

- **UX2-A — placement centering + the scale-composition fix.** `MasterRenderSystem` composes
  `scale = (Size / source) * element.Scale` so a gizmo-scaled sprite's drawn quad matches its hit-test quad;
  the palette ghost + placed stamp land with the sprite's visual centre at the cursor (one shared position
  function), feet-origin untouched.
- **UX2-B — left tabs + dedicated right Inspector + region-header framework.** The left region activates as the
  **Entities / Systems / Scenes** tab group; the right region becomes the dedicated **Inspector**; each region
  owns a header band (`EditorChromeLayout.TabStrip`/`RegionBody`), and the center region's **Scene panel header**
  is carved out of the game viewport (the transport relocates there). `LeftWidthPt` + a left splitter join the
  shell-state model; ops rename (`panel:tab <entities|systems|scenes>`).
- **UX2-C — `EditorIcons` procedural glyphs + tooltips.** A pure line/triangle geometry library (Lucide as the
  visual reference, nothing imported — the module ships no binary atlas) renders the transport/tool/window-bar
  buttons as screen-baked mesh glyphs tinted by state; a single pooled hover tooltip names each icon button.
- **UX2-D — `EditorContextMenu` + the Entity menu + Add/Create.** A data-driven popup (one model, two anchors)
  drives the viewport / Entities / Scenes right-click menus + the fixed `Entity ▾` header menu; **Order** relocates
  off the toolbar into the entity menus; right-click adds an empty entity / creates an empty scene.
- **UX2-E — the camera rig (view/authored split).** The editor materializes a standalone **camera-rig** entity
  from `scene.camera` (never scene membership); the shared `Camera` becomes the free VIEW, the rig holds the
  authored game-camera state, Save serializes the rig, a frustum glyph shows it when the view differs, and a
  Scene-header **Camera view** button (`view:camera`) snaps the view back onto it.
- **UX2-F — Scene / Game mode sandbox.** `EditorTransport` owns a `[Scene | Game]` `EditorViewMode` alongside
  `RunMode` (ONE owner). Entering Game snapshots the scene in memory (before Play flips RunMode) and looks
  through the game camera; edits are a sandbox; **Save is blocked** (`SaveBlockReason.GameMode`); exiting restores
  the snapshot **through the reader** (an in-memory `LoadSceneRequest(SceneData)` — the ONE restore path) and the
  captured dirty state + Scene view; Restart lands Scene mode; a scene switch exits Game first. The `[Scene | Game]`
  header toggle segments (ops `mode:scene`/`mode:game`) are ordinary `ToolbarButtonComponent`s the ONE
  `ToolbarSystem` hit-tests + renders tab-style, live in both transport states.

**Named leftover (UX2, not built).** The collider/vertex authoring buttons (`+Box`/`+Poly`/`-Col`/`+Vtx`) stay
on the window top bar this phase; their natural home is a future **Inspector "add component" surface** (UX2-D §4
noted it, deliberately deferred — relocating them needs the Inspector to grow an authoring affordance).

## UX3 phase (UX3-A–UX3-F) — game-mode integrity, overlays, shortcuts, modal transforms

A third UX pass (the five asks of 2026-07-09) hardening the Scene/Game split and adding the Blender-style
keyboard layer the editor had deferred. Like the earlier UX phases it runs orthogonally to Waves A–F. The
authoritative design is [`editor-shell-ui-ux-3.md`](editor-shell-ui-ux-3.md); the invariants live in
[`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md). (There is no
UX3-B — the numbering follows the design doc's ask-driven waves.)

- **UX3-A — Game-mode integrity + explicit modes.** Repro-first fix for the blank-Game-mode bug: a
  `camera: null` scene syncs the rig to the **post-load framed view** (not the pre-load origin), so entering
  Game mode lands on content; the exit-restore never applies a zeroed view snapshot (`CameraViewSnapshot.IsValid`).
  The header segments read **"Scene mode" / "Game mode"** and toggling INTO Game mode **auto-plays**.
- **UX3-C — icon polish.** Prominent filled arrowheads (≥22% of the box) on move/rotate/scale/undo/redo/
  restart/refresh; Snap becomes a closed square + inner 3×3 grid; Camera becomes a video-camera; Save becomes a
  beveled-top-right floppy — each a pure-geometry `EditorIcons` refinement with a geometry test.
- **UX3-D — viewport Overlays menu + grid.** `EditorContextMenuModel` gains checkable (`Toggle`) items; a
  Scene-header **Overlays** dropdown drives a session `ViewportOverlaySettingsComponent` (grid off / outline on /
  camera on); the world grid renders at the SHARED snap step (`GridSpacing == GizmoStateComponent.GridStep`),
  bounded (degrades to major-only when zoomed out), hidden in Game mode. Ops `overlay:grid|outline|camera on|off`,
  `overlay:spacing <n>`.
- **UX3-E — combo input (an engine feature) + the ONE shortcut table.** `foundation`'s `KeyChord`/`KeyChordTracker`
  (exact-modifier press-edge matching + the virtual `PlatformCommand` ⌘/Ctrl resolution) back the editor's ONE
  `EditorShortcuts` chord table read by `EditorShortcutSystem`, gated by the single `ViewportShortcutContext`:
  `Cmd/Ctrl+Z` Undo, `Cmd/Ctrl+Shift+Z` Redo, `Shift+A` Add menu, `Delete`, `Home` frame — the scattered
  per-action predicates consolidated, the bare `Z`/`Y` undo/redo removed (bare keys are tools).
- **UX3-F — modal transforms + the window status bar** (this wave; depends on UX3-E). Bare `G`/`S`/`R` enter a
  Blender-style modal transform over the selection (`ModalTransformSystem` + the pure `Transform/ModalTransform`):
  the mouse edits live through the SAME coalescing history as a gizmo drag (one session = one undo step), with
  X/Y axis locks + numeric entry (typed OVERRIDES the mouse, exact; a typed grab requires an axis) + snap on the
  mouse-driven result. The modal owns the pointer (consumes the cursor edges — the confirm-click never re-picks,
  pre-mortem #4) and the keyboard (`Modal.IsActive` ORs into `ShouldSuppressInput` + the shortcut gate; Escape
  cancels the modal, not the game/tool). The rig composes: G moves it, S → zoom (`CameraZoomEditCommand`), R
  refused. Ops `modal:grab|scale|rotate|axis x|y|digits <text>|cursor <dx> <dy>|confirm|cancel`. The **window
  status bar** (`EditorStatusBarSystem` + the pure `UI/StatusBarModel`) is one thin strip in the ONE viewport
  inset (below the assets shelf): the live modal readout / contextual status on the left, the scene id + mode +
  a Warning dirty dot on the right (ASCII-only; the dirty dot is a mesh).

## Prefab phase (PF-A–PF-E) — complete

The user's "build NPCs / dialogue zones / the Player as prefabs" ask (2026-07-09): prefabs are classes
instantiated as **LINKED instances with whole-component overrides**, creatable via code AND config, designed
in dedicated viewport tabs, with a **Chrome-DevTools-grade editable Inspector**. Like the UX phases it ran
orthogonally to Waves A–F. The authoritative design is
[`prefab-workflow.md`](prefab-workflow.md); the invariants live in
[`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md).

- **PF-A — DevTools Inspector.** The right-strip Inspector became editable: a filter field, type-colored
  values, and value / add / remove edits through undoable commands (`MemberEditCommand` — struct write-back
  via get-modify-`Set`; `AddComponentCommand` / `RemoveComponentCommand` — the `SpriteInfo⇔DrawComponent`
  pairing + Transform-not-removable + structural exclusions). Add candidates are the serializer registry's
  types minus present/structural (the honest "what this scene can persist"). Ops `inspector:filter|edit|add|remove`.
- **PF-B — viewport tabs + `ViewportContextStack`.** The Scene/Game mode toggle retired for a tab strip; ONE
  snapshot / sweep / reader-restore mechanism (pre-mortem #4). The Game tab is its first (discard) consumer —
  the UX2-F sandbox generalized, never a parallel path; leaving it discards + restores the Scene. The Scene
  tab is always index 0, never closable; a dirty Scene is never silently discarded.
- **PF-C — the `.mdprefab` core (test-first, no UI).** The `SceneData` schema reused verbatim with prefab
  rules (`PrefabWriter`: exactly one root, root position origin-normalized, no camera, cycle-refuse). A scene
  places a **compact `prefab` entry** — Transform + diff-based whole-component overrides (byte-equal ⇒
  inherited, byte-different ⇒ override; instance children NEVER serialized). ONE `PrefabExpander` shared by the
  reader, the `PrefabFactory` (`EntitySpawnRequest("prefab:<id>")`), and `PrefabPropagation`; fail-loud on a
  missing prefab, cycle-capped at load; `save → load → save` is a byte fixed point. Bundled zero-touch under
  `Content/Prefabs/`, resolved source-first via `PrefabFileSource`.
- **PF-D — the prefab UX.** A Prefabs shelf tab; prefab-context tabs (no camera rig — the four-fold gate,
  pre-mortem #8); **Create Prefab from Selection** (capture → replace with a linked instance, one composite)
  and **Create Empty Prefab**; **Save Prefab** + live propagation (the Restart rule clears history when
  instances rebuild); **Unpack** (dissolve the link, undoable); instance-children guardrails (the ONE
  `PrefabGuards.IsPrefabOwned` predicate — the root stays editable, children are refused). Ops `prefabs:list`,
  `prefab:edit|place|unpack|delete|create-from-selection|create-empty`, `dialog:prefab`, `panel:tab prefabs`.
- **PF-E — the acceptance walkthrough + hardening + this sweep.** An end-to-end, in-process story
  (`PrefabMilestoneTests`) building the NPC / dialogue-zone / Player prefabs, placing four linked instances,
  overriding one NPC's dialogue node, saving the scene (compact entries + zero serialized children),
  re-editing the NPC prefab and verifying propagation on the scene's restore (the override survives), the
  byte-stable round-trip with instances, then boot + play (the player's physics live, the zone's trigger
  collider fires) + a Restart-equivalent reload. The walkthrough surfaced **no** in-wave defects — the PF-A..D
  core held end-to-end. It also hardened **test isolation** (safe-by-construction: `GameTestRunner` pins every
  spawned process to an isolated `MONODREAMS_PROJECT_ROOT` temp tree so no editor run can ever write the real
  `Content.mgcb` / `Levels` / `Prefabs`; a collection-fixture + resolved-root tripwire, `ContentTreeIsolationTests`,
  fails if that regresses).

**Ledger (bisect archaeology).** PF-C's premises/tests reference `PrefabFormatTests` / `PrefabExpansionTests`
etc.; those helpers were not present on a clean PF-C checkout — they landed together with PF-D's `d270cb3`, so
`git bisect` across the PF-C..PF-D boundary should expect the prefab test surface to appear at PF-D.

**Deferred (named terrain, with triggers):**
- **Nested-prefab authoring.** The `PrefabExpander` recursion + cycle rules exist, but placing a prefab
  instance INSIDE a prefab tab is v1-refused with a hint. *Trigger:* a designer needs composite prefabs
  (a house prefab containing door/window prefabs).
- **Per-field overrides.** v1 is whole-component replacement (an edited component's whole body is the
  override). *Trigger:* two instances need to diverge on one field of a large component without forking the rest.
- **Tracked component removal.** A component the prefab HAS but that was REMOVED on an instance does not
  persist — re-expansion restores it. *Trigger:* an instance must legitimately drop an inherited component.
- **Prefab thumbnails.** The Prefabs shelf uses a generic package glyph; rendered per-prefab thumbnails are
  terrain. *Trigger:* the shelf grows past a handful of prefabs and the glyph stops disambiguating.
- **Prefab playgrounds.** Play is disabled in a prefab tab (a prefab never plays, v1). *Trigger:* authors want
  to test a prefab's behaviour in isolation without placing it in a scene.

## Colliders-as-entities phase (CE-A–CE-D) — complete

The user's directive (2026-07-10): "turn colliders into proper entities and no longer proxies … update
current examples and demos … no backwards compatibility." This **resolved the deferred engine RFC below**
(colliders as child entities with their own Transform) and permanently fixed the PF-G bug class (a
collider's world shape derived from a LOCAL offset — authored-in-prefab vs placed-in-scene divergence).
The authoritative design is [`colliders-as-entities.md`](colliders-as-entities.md); the invariants live in
the `collision` / `physics` / `level-editor` premises + `CORE_TENETS` §5.

- **CE-A — collision + physics core.** A collider IS an entity (a shape + its own `TransformComponent` +
  auto `ColliderTagComponent`); box = centered `Size` (no offset), convex = entity-local vertices; the
  world shape derives from the collider entity's `WorldMatrix`. `ColliderBody.Resolve` (nearest
  RigidBody/Velocity ancestor, else self) is the ONE body-resolution primitive; `CollisionMessage` carries
  four entities (`ColliderA/B` + `BodyA/B`); resolution writes the correction to the BODY (pre-mortem #1).
  Multi-collider bodies are legal (the one-per-entity premise retired).
- **CE-B — serialization v2 + migrator.** `SceneData.Version = 2`; a v1 file with an embedded collider is
  refused loud on a FILE read; the `monodreams migrate-colliders <path|dir>` CLI command (byte-canonical,
  idempotent, `--dry-run`, dir recursion; handles `.mdscene` AND `.mdprefab`) rewrites each embedded
  collider onto a collider CHILD entity (a zone reshapes in place so identity stays on the collider);
  committed `sample`/`Blender_Level` migrated in-repo.
- **CE-C — editor retarget (proxies die for whole shapes).** A collider is a first-class editor entity:
  border-picked on its world shape, moved/scaled by the ordinary gizmo + modal G/S/R (a box refuses
  Rotate — axis-aligned; a convex rotates), edited in the Inspector. **Add Collider ▸ Box / Polygon**
  creates a footprint-shaped child collider entity; **−Col** deletes it. Only convex VERTEX grips survive
  as proxies (`ColliderEditCommand.ForConvex`); the whole-shape `BoxColliderBounds`/`ConvexColliderShape`
  proxies + box-resize + `ColliderComponentCommand` are deleted.
- **CE-D — Examples/Demos sweep + milestones + docs close.** The consumer audit completed
  (`CollisionConsumerAuditTests` — every `CollisionMessage` consumer proven to read the correct side,
  pre-mortem #4); the physics/collision demo gained a headless render-path smoke; `IslandMilestoneTests`
  / `PrefabMilestoneTests` confirmed the current authoring story end-to-end; `PrefabColliderMigrationTests`
  proved the migrator handles a prefab with embedded colliders through `PrefabData.FromScene`; docs closed.

**Wave 8b (collider gizmo proxies) is SUPERSEDED by this phase** — the proxy layer it shipped is exactly
what the RFC below deleted. The Wave-8b section above is kept for history.

**Landed — wave BR: the Blender importer is retired** (user directive 2026-07-10, "we are getting rid of
it", no compatibility owed). Deleted `MonoDreams/level-blender/` wholesale (the `BlenderLevelParserSystem`
parser + `BlenderLevelData` types + module docs + the `Tools/blender_level_export.py` exporter plugin), the
Examples `importMode` Blender wiring in `LoadLevelExampleGameScreen`, the `blender-platformer` CLI preset
(`monodreams add level-blender` now errors like any unknown module; `list` no longer shows it), and the
Blender-shaped `LevelImporterTests` fixture. Decremented the module count (14 → 13) across `CLAUDE.md` /
`MODULES.md` / `README.md` / `CONTRIBUTING.md` / `docs/index.md` / `skills-config.md`, and swept the
flow/premise docs (the `Blender_` dispatch premises removed, `docs/flows/level-blender.md` deleted). The
committed native `Content/Levels/Blender_Level.mdscene` (origin Blender, now a native scene the game owns)
and its GreasePencil textures STAY — gated by `BlenderLevelTests`/`MigratedLevelTests` booting it native.
The LDtk import path (parser + one-way `LevelImporter`) is untouched.

**Named terrain (CE follow-ups, with triggers):**
- **Rotated axis-aligned hitboxes → use a polygon.** A `BoxColliderComponent` is intentionally
  axis-aligned (rotation ignored; the editor refuses Rotate on a box). *Trigger:* a designer needs a
  rotated rectangular hitbox — author it as a convex quad (which rotates) rather than adding OBB math to
  the box path.
- **Collider layers UI.** `ActiveLayers` is authored numerically today (no editor surface for the
  layer-membership pair filter). *Trigger:* a game grows past a couple of collision layers and needs a
  visual layer editor / matrix.
- **Per-collider debug filtering.** `ColliderDebugSystem` draws all collider entities' outlines at once.
  *Trigger:* dense scenes need to filter the debug overlay by layer / identity / selection.

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
