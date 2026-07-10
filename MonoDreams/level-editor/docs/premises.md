# level-editor — premises

> Technical invariants the engine assumes about the level editor: the editor
> runs as an in-game `Edit` mode over the **real** game pipeline (not a forked
> renderer or a parallel data model), gated by the `foundation` run-state model.
> Read this before changing the editor screen, its overlay entities, or the
> scene save/load path.
>
> **Status: Wave 5 (complete) + post-Wave-A editor usability + the transport model.** The run-state
> premise (Wave 1), the three serialization premises (Wave 2 — registry opt-in,
> AssetKey-not-live-texture, SOURCE-not-derived sort fields), the scene
> round-trip premise (Wave 3 — membership closure + the `LoadSceneRequest`
> reader + `Texture2D` rehydration), the Wave-4a interactive-editor invariants —
> overlay-standalone + delete-snapshot, bounded undo with drag-coalescing, and
> selection topmost — the Wave-4b gizmo + toolbar invariants — the gizmo applies
> a quantized (snap-on) or raw (snap-off) transform edit honoring Origin, and the
> toolbar's buttons drive the SAME shared editor instances — the Wave-5 headless
> editor-op channel (injected cursor state survives the input pass; the op
> channel holds the session open), and the post-Wave-A camera-navigation
> invariant (pan/zoom/frame-scene drive the camera directly, Edit-guarded,
> ordered before the cursor's world-pos derivation) are all live below, plus the
> **transport model** (the editor is always-on under the run flag; Play/Pause +
> Restart replace the retired F1 mode toggle; Restart rebuilds from the original
> load and discards unsaved edits), the **project-persistence PS1** invariant
> (canonical, byte-stable scene serialization + a persisted stable scene-local id
> ordering `entities[]`), and the **UX2 phase** invariants (UX2-B left tabs + right
> Inspector + region headers; UX2-C procedural icon buttons + tooltips; UX2-D context
> menus; UX2-E the camera-rig view/authored split; **UX2-F** the Scene/Game-mode
> sandbox — snapshot on enter, reader-shared restore on exit, Save blocked in Game
> mode, one-owner transport; and **UX3-D** the viewport Overlays menu — checkable
> (Toggle) menu items, the session overlay settings, the one-value grid = snap step,
> the bounded grid renderer, and the Game-mode overlay hide; **UX3-E** the ONE editor
> keyboard-shortcut chord table — Undo/Redo/Delete/FrameScene/AddMenu over the foundation chord layer,
> gated by a single viewport context, consolidating the editor keyboard bindings and removing the bare
> `Z`/`Y` undo/redo; and **UX3-F** the Blender-style modal transforms — bare `G`/`S`/`R` enter a
> coalesced-undo modal edit that owns the pointer + keyboard (axis locks, numeric entry, rig composition,
> Escape priority), plus the window status bar (one thin strip in the ONE inset, a pure formatting model)),
> and the **PF-A** phase (the DevTools-grade editable Inspector — a filter field, type-colored member
> values, inline value editing, and add/remove components through undoable commands, with the
> keyboard-ownership + registry-driven-candidate + component-pairing guardrails).
> No premise here ships `Tests: none yet`.

## The editor reuses the game's real pipeline via run-state gating, not a forked renderer

The level editor is an in-game **mode**, not a separate application. In
`RunMode.Edit` the same world, the same `Camera`, and the same draw stack the
player sees (`CullingSystem → SpritePrepSystem → YSortSystem → MeshPrepSystem →
TextPrepSystem → MasterRenderSystem`) stay live, so the editor previews exactly
what ships. Edit-mode behaviour is produced by **gating** the existing game
pipeline through `GatedSystem` + `EditTimeBehavior`, never by registering a
parallel editor renderer or maintaining a second editor-only scene model. Render
/ input / cursor and `HierarchySystem` run in both modes (`RunNormally`); game
logic / physics / camera-follow take `Freeze`; editor systems run on top,
Edit-guarded.

**Why:** cornerstone of the design (`docs/CORE_TENETS.md` — "The editor is part
of the game"): a separate editor renderer drifts from the game renderer and the
"what you edit is what ships" guarantee is lost. Reusing the pipeline also means
the editor inherits every rendering fix for free.
**Breaks:** a parallel renderer or a second data model means the edited scene and
the played scene diverge (a sprite that culls/Y-sorts differently in the editor
than in play); a black screen if the render module is wrongly frozen in Edit, or
physics that keeps moving entities while the designer is placing them if the
game systems are not `Freeze`-gated.
**Tests:** `MonoDreams.Tests/Foundation/RunStateGatingTest.cs` (the gating
mechanism this premise relies on: `Freeze` skips in Edit, `RunNormally` runs in
both); the matrix-level assertion that render stays live and physics freezes lands
with the editor screen in Wave 4.
**Depends on:** foundation — "Edit-time behavior is a per-system policy honored by
`GatedSystem`"; rendering — "Rendering systems run last in the pipeline".

## The component-serializer registry is opt-in per type; unregistered components are skipped-with-warning, not silently dropped

A scene serializes only the component types explicitly **registered** on the
`ComponentSerializerRegistry` (`Type` → a `(write, read)` pair keyed by a stable string).
The engine ships serializers for its serializable components via
`EngineComponentSerializers.RegisterEngineComponents` (`TransformComponent`,
`SpriteInfoComponent`, `EntityInfoComponent`, the colliders, `RigidBodyComponent`,
`VelocityComponent`, and the `ChildOfComponent` parent link); **game code registers
serializers for its own components** (e.g. `PlayerState`) through `registry.Register`.
Engine tags and transient state (`VisibleComponent`, `ColliderTagComponent`, cursor state,
`DrawComponent`) are deliberately **not** registered — they are re-derived by their systems.
When the writer encounters a component on an entity with no registered serializer, it
**skips that component and emits a `Logger.Warning`** — never a silent drop. (Registration
runs once at module/screen init, so the in-scope component set is explicit and greppable.)

**Why:** the round-trip must reconstruct from components, not by re-running factories
(GAP-A); opt-in keeps engine tags + transient state out of the file, and the loud warning
turns a dropped component into a visible signal rather than the missing-entity class of bug
(mirrors level-loading — "Unregistered factory identifiers log a warning"). Game-component
registration is the framework-not-library extension seam.
**Breaks:** registering by default (or auto-serializing every component) would write
transient tags (`VisibleComponent`) and unserializable state into the file; silently
dropping an unregistered component loses a designer's data with no signal.
**Tests:** `MonoDreams.Tests/LevelEditor/ComponentSerializerRegistryTest.cs`
(`Registry_OptIn_SkipsUnregisteredEngineTags`,
`UnregisteredComponentWarning.SerializeEntity_WithUnregisteredComponent_SkipsAndWarns_DoesNotThrow`,
`GameComponent_RegistersOwnSerializer_AndRoundTrips`, `Deserialize_WithUnregisteredKey_ThrowsLoud`).
**Depends on:** level-loading — "Unregistered factory identifiers log a warning and silently
drop the spawn" (the analogous loud-on-the-unexpected stance).

## `SpriteInfoComponent` serializes an `AssetKey`, never the live `Texture2D`

A `Texture2D` is a GPU resource, not serializable data. `SpriteInfoComponent` carries an
additive optional `AssetKey` (string, nullable, default `null`) — the content key the
texture was loaded from (e.g. `"Atlas/TX Player"`). The serializer persists the key and
the source sprite fields, and **never** the live `SpriteSheet`. On load the texture is
rehydrated via `ContentManager.Load(assetKey)` (Wave 3's reader); after a Wave-2 in-memory
deserialize, `SpriteSheet` is `null`.

**Why:** the live texture can't round-trip through JSON; the asset key is the serializable
stand-in. The field is additive + optional so every existing `SpriteInfoComponent`
construction site is unaffected (back-compat).
**Breaks:** attempting to serialize the `Texture2D` either fails or bakes a GPU handle that
is meaningless on reload; omitting the key leaves a loaded sprite with no way to find its
texture.
**Tests:** `MonoDreams.Tests/LevelEditor/ComponentSerializerRegistryTest.cs`
(`SpriteInfo_Serialization_CarriesAssetKey_AndSourceSortFields_NeverLiveTexture`,
`RoundTrip_ReproducesRegisteredComponents_AndParentLink`).
**Depends on:** rendering — `SpriteInfoComponent` is the source sprite-data struct.

## The serializer persists SOURCE sort fields, never the per-frame-derived `DrawComponent.LayerDepth`

`SpriteInfoComponent`'s serializer writes the SOURCE sort fields — `LayerDepth`,
`YSortOffset`, `YSortDepthBias` — and **never** `DrawComponent.LayerDepth`, which
`SpritePrepSystem` / `YSortSystem` rewrite every frame from those source fields and the
entity's world Y. Persisting the derived depth would bake one camera frame's Y-sort result
into the file; persisting the source fields lets the next prep+sort frame after load
recompute the same derived depth deterministically.

**Why:** the derived-value table's "persisted sort fields are SOURCE not derived" row — the
derived depth is a function of camera bounds + world Y, so it is not stable to persist.
**Breaks:** a scene saved while the camera framed one region reloads with every sprite's
depth frozen to that frame, so Y-sorting is wrong until something forces a re-sort.
**Tests:** `MonoDreams.Tests/LevelEditor/ComponentSerializerRegistryTest.cs`
(`SpriteInfo_Serialization_CarriesAssetKey_AndSourceSortFields_NeverLiveTexture` asserts the
SOURCE fields are written; the derived-depth *reproduction* across a full save→load frame is
`DerivedDepthReproductionTest`, planned with the Wave-3 reader).
**Depends on:** rendering — "Layer depth ownership" (`SpritePrepSystem` → `YSortSystem` →
`MasterRenderSystem` rewrite `DrawComponent.LayerDepth` each frame).

## Scene round-trip reconstructs from registered components, not factories

A scene saves the registered components of every `SceneObjectComponent`-tagged root **plus** each
root's `ChildOfComponent` descendant closure, so a factory's sub-graph (e.g. a player and its
orbiting orbs) round-trips with its parent graph intact even though only the root is tagged.
Transient / overlay entities (cursor, UI / HUD, the editor's gizmo / selection / toolbar, and the UX2-E
camera rig — never `SceneObjectComponent`-tagged, so it never enters `entities[]`, pre-mortem #4) are
untagged → excluded; Blender-origin entities are untagged in this wave (their save is deferred) →
view-only. `SceneWriter` computes the closure, serializes it through the Wave-2 `SceneSerializer`
into a `SceneData` (attaching the **camera rig's** state as `scene.camera` — UX2-E: the writer reads the
authored camera FROM the rig, not the live view — and the `DrawLayerMap` banding), and writes
the canonical JSON through `IPlatformServices.WriteAllText` into the versioned project source tree
(`ProjectRoot/LevelsDir/<sceneId>.mdscene` — PS3; see "The editor Save writes versioned `.mdscene`
into the project source tree"). Loading is a
**dedicated `LoadSceneRequest`** message — separate from `LoadLevelRequest` so it never triggers
(or, on failure, clobbers) the LDtk `Content.Load` / `Remove<CurrentLevelComponent>` path —
handled by `SceneReaderSystem` in two passes (create + deserialize each entity's components, then
wire the parent graph from the recorded indices). After deserialize the reader **re-tags each scene
root** — every top-level `entities[]` entry (no in-scope parent), mirroring `CollectMembership`'s
seed set exactly — with `SceneObjectComponent`, so **`save → load → edit → save` is a fixed point**:
`SceneObjectComponent` is transient editor state that never serializes, so without the re-tag a
reloaded scene has zero tagged roots and the next Save writes an empty scene, silently losing every
edit since loading. `ChildOf` descendants are not re-tagged (the writer auto-closes them from their
tagged ancestor), and bake products never reach the loop (they are never serialized → never in
`entities[]`; they regenerate on load and the writer excludes them from any tagged root's closure).
The reader then **rehydrates** each sprite's `Texture2D` from its `SpriteInfo.AssetKey` via
`ContentManager.Load`, and **fails loud** on a component key in the file with no registered
serializer (the registry throws; the load aborts with a clear message rather than silently dropping
data). A re-prep + Y-sort frame after load recomputes `DrawComponent.LayerDepth` identically, because
the SOURCE sort fields — not the derived depth — were persisted.

**Why:** the round-trip must reconstruct from components, not by re-running factories (GAP-A), so
edited state and factory sub-graphs survive; a dedicated load message keeps the native and LDtk load
paths independent; rehydration restores the live GPU texture the JSON cannot carry; failing loud on an
unknown component turns a dropped component into a visible error rather than the missing-entity class of
bug.
**Breaks:** sharing `LoadLevelRequest` would let a native-scene load clobber the LDtk
`CurrentLevelComponent`; serializing from factories would lose edited state; a missing membership
closure would drop a tagged root's children; **not re-tagging reloaded roots makes the next Save
write an empty scene — the designer's whole iterate-on-a-level loop silently loses all work since
loading**; persisting the derived depth would bake one camera frame's Y-sort into the file;
swallowing an unregistered key would silently lose a designer's data.
**Tests:** `MonoDreams.Tests/LevelEditor/SceneRoundTripTests.cs` (`SceneRoundTripGoldenTest` — tag a
sprite root + a `ChildOf` child, write, reload via `LoadSceneRequest`, assert Transform + `SpriteInfo`
SOURCE sort fields + `AssetKey` + texture rehydration + parent graph + camera/layers reproduce;
`MembershipFilterTest` — only tagged roots + their `ChildOf` closure serialize, transient/untagged
and Blender-style entities excluded, and the UX2-E camera rig excluded (pre-mortem #4); `DerivedDepthReproductionTest` — after reload, a prep + `YSortSystem`
frame recomputes the identical derived `DrawComponent.LayerDepth`;
`ReloadedSceneReTagsRoots_LoadEditSaveIsAFixedPoint` — save mixed content, reload, edit a loaded
transform, re-save: the same 3 roots reproduce, the boundary bake child stays excluded, and the edit
persists — the second save is empty without the re-tag).
**Depends on:** level-loading — `LoadLevelRequest` is LDtk-coupled (the asymmetry this premise routes
around); rendering — "Layer depth ownership" (`SpritePrepSystem` → `YSortSystem` re-derive depth each
frame); foundation — the `IPlatformServices` portability seam.

## A loaded sprite entity carries a `DrawComponent` (reader-restored) and the reader auto-frames the camera on content

`DrawComponent` is deliberately **not** serialized (its sprite fields are re-prepped every frame from
`SpriteInfoComponent` and its `LayerDepth` is per-frame-derived), so the scene reader reconstructs
entities with `SpriteInfoComponent` + `TransformComponent` but **no** `DrawComponent`. Because
`SpritePrepSystem` queries `[With(DrawComponent, SpriteInfoComponent, TransformComponent,
VisibleComponent)]`, a reloaded sprite without a `DrawComponent` is **never prepped, never drawn** — the
Main target stays the backbuffer clear color (the confirmed "reloaded scene renders blank" bug). So the
invariant: **an entity with `SpriteInfoComponent` must have a `DrawComponent`.** `SceneReaderSystem`
restores it after deserialize (`RestoreDrawComponents`) — a sprite `DrawComponent` whose `Target` is the
sprite's own `SpriteInfoComponent.Target`, mirroring `SpritePropFactory` (the authoring path) — for
every reconstructed sprite that lacks one. Only sprites are restored: `DrawComponent`'s mesh/text
payloads are not serializable today, so a sprite is the only serialized renderable.

Additionally the reader **positions the camera on load** — a view/camera split (UX2-E). What consumes
`scene.camera` depends on whether the editor is composed (the reader's optional `applyCameraToRig` seam):
- **Editor present** (the overlay wires the seam to `EditorCameraRig.SyncFromScene`): the free **VIEW** (the
  live `Camera`) **auto-frames on content** — centre + zoom-fit of the loaded content's world-space AABB via
  the pure `CameraNav` frame-scene math — so an off-origin scene (e.g. `Blender_Level` at ~(1275,−530)) is
  visible regardless of where the authored camera sits, and THEN (sequenced after the framing) `scene.camera`
  goes to the **rig** (the authored game-camera state lives there). A **null** `scene.camera` (every pre-UX2-E
  scene) makes the rig adopt the just-framed VIEW instead of the pre-load origin — the UX3-A default, "the
  authored camera starts on the content" (see "The editor splits the free VIEW from the authored camera rig").
- **Shipped** (no seam): the live camera IS the authored camera, so `scene.camera` is applied to it
  directly when present (respecting the authored view); a legacy camera-less scene auto-frames on content
  (byte-identical to pre-UX2-E — every prior scene saved `camera: null`).
In BOTH cases the camera is left untouched when a null `Camera` was supplied (the pure round-trip tests,
and the reference shipped reader that relies on `CameraFollowSystem`) or an **active**
`CameraFollowTargetComponent` is present (`CameraFollowSystem` owns it in Play — the reader must not
fight it). No content ⇒ auto-frame is a no-op. The `Camera` (the view) is an optional reader ctor param;
the `applyCameraToRig` seam is the editor-only rig materialization hook.

**Why:** the render pipeline's `SpriteInfoComponent ⇒ DrawComponent` pairing is what puts a sprite on
screen; the reader reconstructs the transient `DrawComponent` rather than serializing it (which would
bake a per-frame-derived depth). Auto-framing turns a technically-loaded-but-invisible off-origin scene
into a visible one without a manual "frame scene" keypress the first time.
**Breaks:** a reloaded sprite with no `DrawComponent` is invisible (blank backbuffer-colored screen);
overriding an active follow target's camera would fight `CameraFollowSystem` in Play; framing on empty
content would jump to a degenerate AABB.
**Tests:** `MonoDreams.Tests/LevelEditor/LoadedSceneRendersTests.cs`
(`ReloadedSprite_HasDrawComponent_SoItEntersThePrepQuery` — the reloaded sprite carries a sprite
`DrawComponent` targeting Main; `ReloadedScene_AutoFramesCameraAndPassesCulling_SoItRendersNonBlank` —
after load the camera sits on the off-origin content and the REAL `CullingSystem` tags the sprite
`VisibleComponent`, i.e. it reaches the draw path at the content region; `ReloadedScene_WithActiveFollowTarget_LeavesCameraAlone`
— an active follow target keeps the camera at its position, and the `DrawComponent` restore still runs).
The UX2-E split is protected by `MonoDreams.Tests/LevelEditor/CameraRigTests.cs`
(`RigMaterializesFromLoad_FileCameraBecomesRigState_ViewFramesContent` — editor path: file camera → rig,
view frames content; `ShippedReader_NoRigSeam_AppliesSceneCameraToTheLiveCamera` — shipped path: no seam →
scene.camera applied to the live camera).
**Depends on:** rendering — `SpritePrepSystem`'s `[With(DrawComponent, …)]` query and `CullingSystem`'s
`VisibleComponent` add (the draw-path gates); this file — "Editor camera navigation pans/zooms/frames
the scene directly" (the `CameraNav` frame-scene math reused), "The editor splits the free VIEW from the
authored camera rig" (the rig the editor path routes `scene.camera` to); "Y-sorted props use the
feet-origin convention, factory-applied" (`SpritePropFactory`, the pairing this mirrors); camera —
`CameraFollowTargetComponent` (the follow-target signal that suppresses camera positioning).

## Scene serialization is canonical and byte-stable; `entities[]` is ordered by a persisted stable scene-local id

Every native scene (and, later, the project manifest) is written and read through ONE canonical JSON
policy — `CanonicalJson` (`Serialization/CanonicalJson.cs`): a shared `JsonSerializerOptions`
(indented 2-space / LF newlines, invariant round-trippable floats, null fields omitted, trailing
`\n`) plus a converter that emits every `Dictionary<string,_>` (the entity `components{}` map) with
its keys in `StringComparer.Ordinal` order. Component bodies are produced through the same policy
(`CanonicalJson.SerializeToElement`), and set-valued fields (a collider's `activeLayers`) are written
sorted. The invariant: **`serialize(world)` is byte-identical across runs and machines, and
`load → save` equals the source file byte-for-byte.** Determinism additionally requires **stable
per-entity ids**: each serialized scene ROOT carries a persisted, monotonic, scene-local id
(`SceneEntityIdComponent`, written as the entry's `id` field) — assigned lazily at first
serialization (`SceneWriter.BuildScene` stamps any root lacking one, next-free = max present + 1),
preserved across `load → save` (the reader restores it from the file, the writer reads it back), and
`SceneWriter` orders `entities[]` by it with a **stable** sort (each root's `ChildOf` closure stays
contiguous and parent-before-child). A `ChildOf` descendant carries no id of its own; the id is
captured as a structural field (like `parent`), marked structurally-captured on the registry so it is
never written into `components{}` nor trips the unregistered-component warning. STJ's default float
format is culture-invariant and shortest-round-trippable (it normalizes `1.0`→`1`, which still
round-trips and re-serializes identically, so the fixed point holds). The later **additive `prefab`
field** on a `SceneEntityData` (PF-C — a linked prefab instance) is null/omitted for an ordinary
entity, so every pre-prefab scene stays byte-identical; see "Prefabs are LINKED instances…".
**Ids self-heal (PF-F): duplicates are re-stamped, never persisted.** A corrupt or double-loaded scene
can carry two roots with the SAME id (the user's `island2`); left verbatim the collision is stable
across `load → save` (the writer preserves restored ids) and the diff never recovers. So BOTH halves
re-stamp the later duplicates to the next free id: the reader (`SceneReaderSystem.RetagSceneRoots`) on
load — the scene loads and heals in-world — and the writer (`SceneWriter.BuildScene`) on save, mutating
the live `SceneEntityIdComponent` and notifying the count. One heal converges the file to distinct,
byte-stable ids; the invariant "ids are unique, monotonic, preserved" holds again.

**Why:** meaningful `.mdscene` git diffs and tractable merges — the precondition for versioning
levels — require deterministic bytes: STJ does not sort object keys by default (the live
component-storage order would leak into the file), a `HashSet`'s enumeration order is unspecified, and
per-session entity/`EditorId` order would reshuffle `entities[]` on a re-save and churn the diff. A
persisted stable id makes a one-entity move a one-line diff instead of a reshuffle.
**Breaks:** unsorted component keys / raw `activeLayers` / per-session ordering churn the diff on
every re-save (line-level noise, unmergeable); a locale-dependent float writes `0,1` and breaks
cross-machine stability and re-parse; overloading the per-session `EditorId` as the persistent id
conflates the render tiebreak with scene identity and loses the id on reload; a bare
`WriteIndented=true` at any call site bypasses the policy and re-introduces churn.
**Tests:** `MonoDreams.Tests/LevelEditor/SceneCanonicalSerializationTests.cs`
(`Serialize_SameWorldTwice_IsByteIdentical`; `StableIds_AssignedMonotonically_AndPreservedAcrossReload`
— build → save → reload → save-again is byte-identical to the source, ids restored;
`DifferentInsertionOrder_SameStableIds_IsByteIdentical` — order-independence via id ordering;
`MovingOneEntity_TouchesOnlyThatEntitysLines` — a transform move is a minimal, localized diff;
`ComponentMapKeys_AreOrdinalSorted` — component keys + `activeLayers` sorted;
`Floats_UnderNonInvariantCulture_UsePeriodDecimal` — a comma-decimal `CurrentCulture` still emits `.`;
`NewRootAfterLoad_GetsNextFreeStableId`); PF-F self-heal:
`MonoDreams.Tests/LevelEditor/DuplicateIdSelfHealTests.cs` (`Writer_TwoRootsShareAnId_ReStampsTheLaterOne_AndConvergesByteStable`,
`Reader_DuplicateIdsInFile_LoadSucceeds_LaterDuplicateReStampedInWorld`).
**Depends on:** this file — "Scene round-trip reconstructs from registered components, not factories"
(the round-trip whose bytes this makes deterministic); foundation — the `IPlatformServices` write seam.

## Editor-overlay entities are standalone; delete snapshots the disposed sub-graph

Editor-overlay entities — the selection marker / gizmo handles / toolbar widgets the editor
itself creates — are **never** `ChildOfComponent`-parented to a game entity. They stand alone.
The reason is `HierarchySystem.DisposeOrphans`, which runs **in Edit** (HierarchySystem is
RunNormally, not frozen): it cascade-disposes any `ChildOf` entity whose parent is no longer
alive. If a gizmo handle were parented to the entity it decorates, deleting that entity would
silently cascade-dispose the gizmo too. Correspondingly, an editor **delete is never a bare
`entity.Dispose()`**: it is a reversible `DeleteEntityCommand` that **snapshots the disposed
sub-graph** (the entity plus its `ChildOf` descendant closure, serialized through the Wave-2
`SceneSerializer`) at construction time, so undo reconstructs the whole sub-graph — components and
parent graph — from the snapshot. `SceneObjectComponent` is transient editor state (not in the
serializer registry), so the command records whether the root was tagged and re-applies the tag on
restore.

**Why:** `DisposeOrphans` (live in Edit) cascades through `ChildOf`; an overlay parented to a game
entity would be collateral on delete, and a bare dispose of a sub-graph would be un-undoable.
**Breaks:** the gizmo/selection overlay vanishes when its host entity is deleted; a delete that
cannot be undone (the children, or the whole sub-graph, are lost with no snapshot).
**Tests:** `MonoDreams.Tests/LevelEditor/EditorRunStateTests.cs` (`HierarchyLiveInEditTest` — Edit
keeps HierarchySystem propagating, so the dispose-orphan path is live in Edit) and
`MonoDreams.Tests/LevelEditor/UndoTests.cs` (`DeleteUndoSnapshotTest` — delete an entity with a
`ChildOf` child, undo restores both with their components + parent graph).
**Depends on:** foundation — "Children are disposed with their parents" (`HierarchySystem.DisposeOrphans`).

## Bounded undo with drag-coalescing

The editor's `EditorHistory` is bounded: it holds at most a configurable cap of undo entries, and a
push past the cap evicts the **oldest** (FIFO) — an old edit drops off rather than blocking the new
one. `Undo` on an empty undo stack and `Redo` on an empty redo stack are **no-ops** (no exception),
so the toolbar can wire the buttons unconditionally. **Drag-coalescing**: `BeginTransaction` opens a
coalesced transaction during which pushed commands apply live (the edit shows on screen) but
accumulate; `CommitTransaction` collapses the whole accumulation into a **single** history entry, so
one full gizmo drag = exactly one undo step. Commands are DATA + an `Apply`/`Revert` pair
(`IEditorCommand`), never behavior-laden OO objects — the history only sequences them.

**Why:** unbounded history is a memory leak in a long editing session; un-coalesced drags would make
one mouse-drag dozens of undo steps; an exception on empty-stack undo would crash a toolbar that
can't know the stack is empty.
**Breaks:** memory growth (no cap / no eviction); a drag that takes N undos to reverse; a crash on
the first undo with nothing to undo.
**Tests:** `MonoDreams.Tests/LevelEditor/UndoTests.cs` (`UndoBoundedCapTest` — push cap+2, history
holds exactly cap, oldest evicted, undo stops at the oldest retained, empty-stack undo/redo no-op;
`DragCoalescingTest` — a transaction of many pushes commits one entry that one undo reverses whole)
and `MonoDreams.Tests/LevelEditor/GizmoTests.cs` (`DragCoalescingTest` — the gizmo path: a drag of N
`TransformEditCommand`s inside one transaction commits one entry that one undo restores to the
pre-drag transform, redo re-applies the whole drag).
**Depends on:** —.

## Selection picks MAX final `LayerDepth` with a selection-owned tiebreak, target-aware

Click-to-select picks the **topmost** sprite under the cursor — the one the composite shows
frontmost. Candidates are hit-tested in their own coordinate space (Wave 8a): a **Main**-target
sprite is world-space and tests the cursor's `WorldPosition`; a **UI/HUD/Scroll**-target sprite is
screen-space (its transform is virtual coordinates) and tests the cursor's `VirtualPosition` — the
letterbox-scaled, pre-camera coordinate that never desyncs from on-screen UI when the camera moves.
**Editor**-target entities (the editor's own chrome) are never candidates. When candidates from
different targets overlap under the cursor, the **final composite order wins** (Main below UI below
HUD below Scroll — `FinalDrawSystem`'s layer order), because that is what the designer sees on top;
within a target the key is **MAX final post-Y-sort `DrawComponent.LayerDepth`**, read **after**
`YSortSystem` has run this frame (selection is ordered at the end of the draw prep), mirroring
`MasterRenderSystem`, which sorts on the same final depth. For an **exact-depth tie**, selection
cannot use the renderer's tiebreak (its per-frame insertion index is private), so it owns a
deterministic one: each candidate gets a stable monotonic `EditorIdComponent` the first time the
selection system sees it (first-seen / creation order), and the larger id — the later-seen entity,
which an undisturbed scene draws last — wins the tie. Hit-testing honors the sprite's rotation,
scale, origin and offset (it inverts the exact draw transform), and a click on empty space clears
the selection. Single-select for Wave A (marquee/multi-select is a later extension). The system is
Edit-guarded (inert in Play), and a plain `ISystem` iterating its own candidate set — NOT an
`AEntitySetSystem`, whose `Update` early-outs on an empty set (a scene with zero rendered sprites
must still border-pick proxies and click-empty clear).

**Click-ownership: the gizmo owns its presses.** `GizmoSystem` publishes a frame-scoped claim
(`GizmoStateComponent.PressClaimed`) on **every** Edit frame it runs: true when the press edge
landed on the active tool's handle (a proxy target forces the Move handle) or while a handle drag
is in progress, false otherwise. Selection must skip a claimed press **entirely** — no re-pick, no
click-empty clear — because the rotate ring and scale handle routinely lie OUTSIDE the selected
sprite's bounds (and a collider proxy's centre move-handle often sits over empty space): processing
that press as a scene click clears the selection (or re-picks an overlapped sprite) in the very
frame the gizmo began the drag, which cancels the drag and despawns the overlays/proxies one frame
later. **Ordering dependency:** the claim is written by the gizmo in the UPDATE pipeline and read
by selection at the end of the DRAW pipeline, so the same frame's claim is always already written
when selection runs; reordering selection before the gizmo would make the claim one frame stale.
A release is never processed (selection acts only on the press edge), so releasing over empty
space never clears; a genuine click on empty space — no handle, no sprite, no proxy border — still
clears, and a click on another sprite away from every handle still re-picks. Above the claim sits
the coarser **tool modality**: selection processes viewport presses only while
`GizmoStateComponent.Mode == SelectTransform` (see "Viewport presses belong to exactly one tool
family" below) — in `Place` mode the palette owns every viewport press.

**A viewport pick on a prefab-owned child redirects to the instance ROOT (PF-G, Unity's model).**
After the topmost hit is found, a VIEWPORT selection resolves it through
`SelectionSystem.ResolveViewportSelection` (`PrefabGuards.InstanceRootOf`): if the hit is a strict
`ChildOf` descendant of a `PrefabInstanceComponent` root, the ROOT is selected instead — so clicking
anywhere on a placed instance selects, and therefore moves/rotates/scales, the whole instance rather
than its prefab-owned child (whose gizmo/modal/inspector edits the instance-children guardrail
refuses). A hit on a plain entity, an instance root itself, or a non-child candidate (a collider
proxy, a boundary, the camera rig) is unchanged. Both viewport picks share the redirect — this
system's left/right press AND the overlay's `menu:open viewport` op. The **Entities tree** deliberately
does NOT redirect: it selects a child directly for inspection (edits still refused with the status
hint), which is why the redirect lives at the viewport-pick sites, not inside `SelectExclusive` (which
the tree/panel rows call).

**Why:** the selected entity must be the one the designer sees on top; on-screen UI lives on the
UI/HUD targets in virtual space (hit-testing it with the camera-relative world point would desync
the pick the moment the camera moves), and those targets composite above the world — so target
rank precedes depth; within a target, matching the render front means reading the same final depth
the renderer sorts on, and the tie must break on a key selection can observe (the renderer's index
can't be). The click-ownership claim exists because the gizmo (update pipeline) and selection
(draw pipeline) deliberately process the SAME `LeftButtonPressed` edge each frame — without an
explicit owner, every press on a handle outside the sprite is simultaneously a valid drag-start
and a valid click-empty/re-pick (the user-reported "rotation and scale handles aren't clickable
outside the entity's bounds" bug).
**Breaks:** picking the back sprite of an overlapping stack (reading source depth, or pre-Y-sort
depth); a UI/HUD sprite unpickable (or mis-picked) after a camera pan because it was tested in
world space; a world sprite stealing the pick from the HUD element drawn over it; the editor
selecting its own chrome; a non-deterministic / unstable pick on an exact-depth tie; a
rotated/scaled sprite mis-picked because the hit-test ignored its transform; without the claim —
a rotate/scale-handle press outside the sprite clears the selection and kills the drag the same
frame, a handle press overlapping another sprite re-selects it and retargets the drag mid-flight,
and a proxy's centre-handle press deselects the proxy and despawns the family; without the
prefab-instance redirect — a viewport click on a placed instance selects its prefab-owned child, whose
edits the guardrail refuses, so the instance reads as unmovable (the user-reported PF-G bug).
**Tests:** `MonoDreams.Tests/LevelEditor/SelectionTests.cs` (`ViewportPick_OnPrefabInstanceChild_RedirectsSelectionToInstanceRoot`
+ `ViewportRightClick_…` — a pick on an instance's child selects the editable root, not the owned child;
`SelectExclusive_OnInstanceChild_SelectsChildDirectly_NoRedirect` — the tree path is unchanged;
`ResolveViewportSelection_RootForOwnedChild_UnchangedOtherwise` — the pure redirect rule;
`SelectionTopmostTest` — stacked sprites
on different depths, click selects MAX final depth, click-empty clears, hit-test honors
rotation/scale/origin; `SelectionOrderingTest` — exact-depth tie resolves by the selection-owned
`EditorId` tiebreak, deterministically; `SelectionTargetAware*` — UI sprite picked via
`VirtualPosition`, Main via `WorldPosition`, HUD wins an overlap with Main regardless of raw depth,
Editor-target never a candidate, the pure cross-target rule; `GizmoTests.GizmoUiTargetTest` — the
gizmo drags a HUD-target entity in virtual space; its overlay VISUALS land on the Editor layer);
click-ownership: `GizmoTests.ClickOwnershipTest_*` (rotate/scale handle press outside the sprite
bounds keeps the selection and the drag completes as one undo step; a handle press over another
sprite does not re-pick; held-drag frames and a spurious mid-drag press never re-pick or clear;
release never clears; genuine click-empty still clears; a press on another sprite away from every
handle still re-selects) and `ProxyTests.ProxyClickOwnershipTest_MoveHandlePressAtShapeCentre_KeepsProxySelectedAndDrags`
(the proxy variant, in a sprite-less world — also protecting the plain-`ISystem` rule).
**Depends on:** rendering — "Layer depth ownership" (`SpritePrepSystem` → `YSortSystem` →
`MasterRenderSystem` derive + sort on final `DrawComponent.LayerDepth`) and the `FinalDrawSystem`
layer order (Main, UI, HUD, then screen-space overlays); this file — "The gizmo applies a
quantized (snap-on) or raw (snap-off) transform edit, honoring Origin" (the drag whose ownership
the claim protects).

## The gizmo applies a quantized (snap-on) or raw (snap-off) transform edit, honoring Origin

A gizmo drag computes the selected entity's new transform from the drag-start state plus the cursor
motion via the pure `GizmoTransform.Compute`. **Move** offsets the position by the world-space cursor
delta; **rotate** adds the signed angle swept (start cursor ray → current cursor ray) about the
entity's world pivot; **scale** multiplies the scale by a factor derived from the drag distance. With
**grid-snap off** the raw result is applied; with **snap on** the world-space result is quantized —
the move position to the grid step, the rotation to the rotation step, the scale to whole steps.
Rotate and scale pivot about the entity's <b>world pivot</b> (the world location of its `Origin`) and
the local `Origin` field is preserved unchanged through every edit. The math is separated from
`GizmoSystem` (which owns only the drag lifecycle + the overlay meshes) so it is unit-testable
without a world, a cursor, or a GraphicsDevice. The gizmo + selection-highlight overlay entities are
standalone (never `ChildOf`-parented); their VISUALS are native-resolution chrome — screen-baked by
`EmitOverlays` (the `editor.overlayPrep` draw entry) on the Editor target through `OverlayProjection`
with fit-scaled (never zoom-scaled) sizes and **no** `VisibleComponent` (the chrome rule) — while the
handle HIT-TESTS stay world-space, sized `constant/Camera.Zoom` so the grab region matches the
constant on-screen visual.

**Why:** the contract's derived-value rows "grid-snap quantum applied world-space, honor origin" and
the editor-overlay-standalone rule; a designer dragging with snap on must land on grid lines, and a
rotate/scale must spin/grow about the entity's pivot rather than translate it.
**Breaks:** a snap-off drag that quantizes (or vice versa) surprises the designer; a rotate/scale that
moves the entity (pivoting about the wrong point) or that mutates `Origin`; an overlay parented to the
selected entity gets cascade-disposed on delete; a `VisibleComponent` on an overlay pulls it into
`MeshPrepSystem`, which overwrites the identity `WorldMatrix` its screen-baked vertices require.
**Tests:** `MonoDreams.Tests/LevelEditor/GizmoTests.cs` (`GizmoTransformSnapTest` — move/rotate/scale
with snap off = raw delta and snap on = quantized; rotate and scale preserve `Origin` and pivot about
the world pivot); `MonoDreams.Tests/LevelEditor/OverlayProjectionTests.cs` (emission space/target/
zoom-invariance/clipping).
**Depends on:** rendering — `MasterRenderSystem` renders a mesh `DrawComponent` per target; this file
— "The editor shell insets the game viewport and renders its chrome at native resolution" (the
Editor layer + the device-pixel space the visuals bake in); foundation —
`HierarchySystem.DisposeOrphans` (why overlays are standalone).

## The editor toolbar's buttons drive the same shared editor instances; the chrome is native-resolution on the Editor target, always on while the editor is composed

The engine-native toolbar (the engine's `SimpleButtonComponent` / `ButtonMeshPrepSystem` /
`DynamicTextComponent` primitives, no ImGui) lives on the **Editor** render target — a target at
native window resolution composited 1:1 over the whole window (never Main, never the virtual-res
HUD) — across TWO button rows (UX2-B): the **window top bar** hosts the editing actions, and the
**Scene panel header** (the center region's header band carved out of the viewport) hosts the
**transport** (`EditorChromeBuilder.DefaultButtons` vs `HeaderButtons`). Both rows are ordinary
`ToolbarButtonComponent` entities, so the **one** `ToolbarSystem` hit-tests and dispatches both;
buttons are sized in physical pixels and each binds a click to an `EditorToolbarAction`.
`ToolbarSystem` hit-tests the cursor's `ScreenPosition` (backbuffer **device** pixels — the raw mouse
× the viewport manager's `DevicePixelRatio`; the chrome sits in the margins where the virtual mapping
is null and `VirtualPosition` is frozen) against the button `Bounds` and hands the action plus the
frame's `GameState` to a dispatch supplied by the overlay — which wires the TRANSPORT buttons in the
Scene panel header (Play/Pause — one toggle whose label `ToolbarSystem` syncs with the state — and
Restart) through the shared `EditorTransport`, **Save by OPENING the three-action Save
dialog** (Save Scene / Save Project / Save Backup As… — the write runs in the dialog's action callback
through the shared `SceneWriter`, UX-D), Undo/Redo on the **same** `EditorHistory`, snap-toggle flipping
the shared `GizmoStateComponent.SnapEnabled`, and tool-select setting the shared
`GizmoStateComponent.Tool`. **There is no Load button** — `EditorToolbarAction.Load` and the file
navigator were removed in UX-D; the only load affordance is selecting a scene in the **Scenes panel**
(see "Game screens declare their bound scene …"). There is exactly one `EditorHistory` / one gizmo-state
entity / one `EditorTransport` — the toolbar never constructs a second. Under the transport model
the toolbar is live in BOTH transport states (the chrome pass always renders while the editor is
composed): the transport buttons dispatch always — they are how you leave either state — while the
EDITING buttons (tools / Save / Undo / Redo / Snap) dispatch only while Paused (`Edit`) and
render with the theme's disabled fill (`EditorTheme.BgDisabled` + `TextDisabled`) while Playing (an
undo racing live physics would be surprising; a viewport click belongs to the game). Every button
fill/label color comes from `EditorTheme` and eases through the shared hover fade (see "Every
level-editor color and depth is an `EditorTheme` role"). Also suppressed: while a shell splitter or
scrollbar drag owns the pointer (`EditorShellStateComponent.IsDragging`), the toolbar dispatches
nothing — a drag that happens to release over a button must not fire it (the shell-state premise's
weave-order-independent drag token).

**UX2-B/-C/-D: transport + tools relocated; Order left the bar; the Entity dropdown joined the header.**
UX2-B moved the transport (Play/Pause + Restart) off the window bar to the Scene panel header
(`HeaderButtons`); UX2-C then moved the transform-tool cluster
(`ToolMove`/`ToolRotate`/`ToolScale`/`ToolBoundary`/`ToggleSnap`) there too, so the Scene header reads
**transport cluster · separator gap · tool cluster** (the last transport button's index is the
`separatorAfterIndex` of `EditorChromeLayout.ButtonRowIn`, inserting a wider `ClusterGap`). **UX2-D moved
the within-band Order (`OrderForward`/`OrderBack`) buttons OFF the toolbar entirely** into the entity
context menus — the `EditorToolbarAction`s and their dispatch stay (the menus fire them), only the
buttons are gone; and it **appended a fixed `EntityMenu` ("Entity") text button + a ▾ caret mesh** to
the header (its dispatch opens the entity context menu below it). The window bar (`DefaultButtons`) now
keeps **Undo / Redo / Refresh** (ICON buttons) plus the still-text **collider/vertex** authoring
actions (their future home is a follow-up, not built this wave). **PF-F: Save left the window bar** for
the Scene panel header (see the Camera-view note below) — ONE Save affordance. Icon buttons are ~square (their width is
the `ButtonHeight`); text buttons stay label-width (the Entity button reserves an extra caret allowance).
The dispatch/gating is unchanged (the ONE `ToolbarSystem` still hit-tests both rows; editing buttons dim
while Playing, transport always live; `EntityMenu` is an editing action). How the buttons DRAW — the
procedural icon meshes + the hover tooltip — is its own premise ("Toolbar icon buttons are procedural
meshes tinted by state; a pooled tooltip names them on hover").
**UX2-E: the Scene header gained a right-corner "Camera view" nav button** (`EditorToolbarAction.CameraView`,
the video-camera icon) — right-anchored via `EditorChromeLayout.SceneHeaderNavButton` (opposite the
left-anchored transport/tool row, the Blender nav-corner affordance), a fixed header affordance separate
from `HeaderButtons`. It is an ordinary `ToolbarButtonComponent`, so the ONE `ToolbarSystem` hit-tests +
dispatches it and bakes its glyph; an editing action (Paused-only), it snaps the editor VIEW onto the
camera rig (`view:camera` — see "The editor splits the free VIEW from the authored camera rig").
**PF-F: the Save icon button now sits in this same right cluster, just LEFT of the Camera-view button**
(`_saveButton`, an `EditorToolbarAction.Save` icon button, not part of `HeaderButtons`): the ONE
`ToolbarSystem` hit-tests + dispatches + bakes its floppy glyph, dims it on the Game tab / unresolved
project via the shared save-guard, and makes its tooltip **context-aware** — "Save Scene" on a
scene/game tab, "Save Prefab" in a prefab tab.
**PF-B: the Scene header leads with the viewport TAB STRIP** (`[ Scene ] [ ▶ Game × ]`) — the retired
`[Scene | Game]` mode toggle's replacement. It is a **separate** system (`ViewportTabStripSystem`, woven
in the `editor.toolbar` group), NOT `ToolbarButtonComponent`s: a tab dispatches by SLOT index
(`SwitchToTab` / the dirty-gated `CloseTab`), so it carries `ViewportTabComponent` and is
**descriptor-driven** from `EditorShellStateComponent.ViewportTabs` (the `ViewportContextStack` writes
them; PF-D appends a prefab tab with no renderer change — see "The viewport context stack …"). The strip
lives at the header START; the transport row is offset right by the fixed
`EditorChromeLayout.ViewportTabsReservedWidth` (one geometry source, replacing the mode-toggle reservation).
Tab visuals mirror the panel tabs (active = `Bg1` fill + `Accent` underline; inactive = `Bg0 → Bg2`
hover-faded fill; a `▶` play marker on the Game tab; a `×` close, `TextMuted → Danger` on hover), and it is
**live in both transport states** (leaving the Game tab must work while Playing). **Overflow risk:** the
window row still lays out left-to-right with no wrap/scroll (`ButtonRow`); UX2-C's narrow icon squares plus
relocating the 5 tools shortened it materially, and UX2-D removing the 2 Order buttons shortened it further
(the collider/vertex text buttons remain); the tab strip + the transport offset consume a fixed slice at the
header's left, but the header wraps nothing either.

**Why:** the contract item 14 (engine-native, web-capable, no ImGui) + the Wave-7 user directive
"the editor tools shouldn't overlay the game screen but be placed around it … highres and
readable, independent from the game resolution or fonts": the old HUD-virtual toolbar was
authored at 800×600 and upscaled — blurry and low-contrast over light levels. A second history
would split undo state; a Main-target toolbar would scroll with the world camera; the transport
buttons must work while Playing or there is no way back to Paused.
**Breaks:** a toolbar that news up its own history (its undo button can't reverse the gizmo's
edits); hit-testing `VirtualPosition` (frozen in the margins — buttons dead or misfiring); editing
buttons live while Playing (undo fighting live physics); transport buttons Edit-guarded (Playing
becomes a one-way door).
**Tests:** `MonoDreams.Tests/LevelEditor/ToolbarTests.cs` (`ToolbarWiringTest` — tool-select sets the
tool, snap-toggle flips the flag, Save invokes `SceneWriter` through a fake `IPlatformServices`,
Undo/Redo drive the shared history, empty-stack undo is a no-op — there is no Load action;
`WindowBar_IsSlimmed_ToolsRelocatedToTheHeader` — the transport AND the transform tools are in
`HeaderButtons` not `DefaultButtons` (UX2-C); `IconButtons_BakeGlyphMeshes_TintedByState` — the icon
buttons carry a glyph mesh + no label and tint by state; `HeaderTransport_DispatchesFromTheHeader_WhilePlaying_WindowEditingInert`
— the header PlayPause dispatches through the one `ToolbarSystem` while Playing, window-bar Save is inert);
`MonoDreams.Tests/LevelEditor/EditorTransportTests.cs` (`SceneHeader_LeadsWithTheTransportButtons` —
the header leads with the transport, then the tool cluster);
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (native `ScreenPosition` hit-test dispatches in
Edit, misses outside the bounds, inert in Play; window buttons in the top bar + transport in the
Scene header; the header ToolbarButton count is `HeaderButtons.Length + 1` — the Camera-view button
only, PF-B: the mode-toggle segments retired, the tab strip is its own `ViewportTabComponent` system);
`MonoDreams.Tests/LevelEditor/ViewportTabStripTests.cs` (the PF-B tab strip that replaced the mode
toggle — descriptor-driven render, active `Bg1` + underline, the Game `▶` + closable `×`, body →
`SwitchToTab` / `×` → `CloseTab`, DPR-2 within the scaled header, Play spawns + activates the Game tab).
**Depends on:** ui — `SimpleButtonComponent` / `ButtonMeshPrepSystem`; cursor —
`CursorInputComponent.ScreenPosition`; level-editor — "The editor shell insets the game viewport
and renders its chrome at native resolution", "Bounded undo with drag-coalescing", "Scene
round-trip reconstructs from registered components, not factories".

## Toolbar icon buttons are procedural meshes tinted by state; a pooled tooltip names them on hover

The editor toolbar's transport (Play/Pause, Restart), transform tools (Move/Rotate/Scale/Boundary/Snap),
window-bar Save/Undo/Redo/Refresh and the UX2-E Scene-header **Camera view** (a video-camera glyph)
buttons render a procedural ICON mesh instead of a text label (UX2-C).
`EditorIcons` is a **pure geometry library**: each glyph is a line/triangle primitive list
authored in a unit box and instantiated into a pixel rect — the `SystemsPanelLayout.ArrowTriangle`
disclosure-caret pattern generalized to a whole set. **Lucide is the visual REFERENCE only; nothing is
imported** (no content-pipeline step — the source-distributed module ships no binary atlas, and
`Content.mgcb` is a guarded file), and every shape stays to a few visual strokes — a recognisable outline
plus its interior marks — so it reads at ~16pt logical.
`ToolbarSystem` bakes each icon button's glyph EVERY frame into a screen-baked `DrawComponent` (identity
`WorldMatrix`, native `Editor` target, `EditorInfrastructureComponent`, **no `VisibleComponent`, no
`SimpleButtonComponent`**) — the same mesh-chrome rules the disclosure arrows and gizmo overlays obey —
in a state-driven colour: `TextDisabled` while inert → `Success` for the Snap toggle when on → `Accent`
for the ACTIVE radio tool (Move/Rotate/Scale/Boundary, resolved from the shared `GizmoStateComponent`
via `EditorToolbarAction.IsActiveIn`) → else a hover-fade `Text1`(idle)→`Text0`(hovered). Play/Pause
swaps its glyph AND tooltip with the transport state (`EditorIcons.Resolve` — the icon analog of the old
label swap). An action with **no** icon (`EditorIcons.ForAction` returns null — the Order/collider/vertex
selection-context actions this wave) stays a TEXT button with its label rendered; **text stays where text
is content** (tabs, rows, menus, dialog actions). DPR scaling is **pure rect scaling** (every vertex is
`rect.TopLeft + unit·rect.Size`, thickness/arrowheads are rect fractions), and Undo/Redo + Restart/Refresh
are exact horizontal **mirrors** (the same shape drawn with `u → 1-u`).

**UX3-C shape refinements (hands-on legibility feedback).** Load-bearing glyph shapes, each protected by a
pure geometry test: every filled **arrowhead** (move — all 4 heads / rotate / scale — both heads / undo /
redo / restart / refresh) is sized from the shared `ArrowSize` so its extent is **≥22% of the icon box**
(the old ~0.13 heads were "hard to even see"); **Snap** is a **closed square border + an inner 3×3 grid**
(2 vertical + 2 horizontal division lines at the thirds — not the open `#` hash it read as); **Camera** is
the classic **video-camera** (a body rectangle in the left ~56% + a small top tab + a filled triangle whose
apex touches the body's right edge, base on the far right — not the old frustum trapezoid); **Save** is the
classic **floppy** (outer outline with a **beveled top-right corner**, the shutter plate displaced slightly
**left** of centre, the label plate **centred**). Scale is now a double-headed diagonal arrow, so "both
heads" is literal.

Hovering any icon button ~0.45s (`EditorTooltip.HoverDelaySeconds`) shows **ONE pooled tooltip** — a
`Bg2` box + `Border` outline mesh + a `Text0` label on the `Editor` target — near the cursor, offset a
few points and **clamped to the window** (`EditorTooltip.Position`), above the dialog band
(`EditorTheme.Depths.Tooltip` > `DialogLabel`), so it is never occluded. `EditorTooltipSystem` scans the
buttons for the one whose per-button `HoverSeconds` clock (advanced — and **reset to 0 on move-off /
press** — by `ToolbarSystem`, which weaves before it in the `editor.toolbar` group) crossed the delay,
reads its `Tooltip` text, and parks the visual (empties the box mesh + blanks the label) when none. The
tooltip is live in BOTH transport states (hovering a transport button while Playing still explains it).
The tooltip preserves the discoverability the label-free icon buttons would otherwise lose.

**Why:** the same rationale as "Panel disclosure arrows are triangle MESHES, not font glyphs" — the mesh
path is font-independent (the BitmapFont has no icon glyphs), DPR-crisp and theme-colored — now extended
to a whole icon set; importing Lucide's TTF/SVG would add a content-pipeline step to a source-distributed
module against a guarded `Content.mgcb`. A dozen text labels made the bar wide and read as prose, not
tools; icons de-crowd it, and the tooltip keeps every button self-describing.
**Breaks:** giving an icon/tooltip mesh a `VisibleComponent` or `SimpleButtonComponent` pulls it into
`MeshPrepSystem`/`ButtonMeshPrepSystem`, which overwrite the identity `WorldMatrix` its absolute-pixel
vertices require; a raw `Color`/XNA token in `EditorIcons`/`EditorTooltip*` trips the palette lint (they
take an `EditorTheme` role); an un-clamped tooltip draws off-screen; not resetting `HoverSeconds` on a
press leaves a stale tooltip over a drag; a tooltip below the dialog band is occluded by a modal.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorIconsTests.cs` (every glyph's geometry stays inside its
rect + bakes the given colour; Undo/Redo + Restart/Refresh are exact horizontal mirrors; DPR scaling is
pure rect scaling; `CenteredIconRect` centers + doubles; `ForAction`/`HasIcon`/`Resolve` mapping;
`IsActiveIn` radio + snap; plus the UX3-C shapes — `Arrowheads_AreProminent_AtLeast22PercentOfTheBox`,
`Snap_IsAClosedSquareBorder_WithAnInner3x3Grid`, `Camera_IsAVideoCamera_BodyRectPlusTopTabPlusTriangleApexOnTheBodyEdge`,
`Save_IsAFloppy_BeveledTopRight_ShutterLeftOfCentre_LabelCentred`);
`MonoDreams.Tests/LevelEditor/EditorTooltipTests.cs` (the delay gate, the
offset + window clamp incl. box-wider-than-window, symmetric padding, DPR-2 doubling);
`MonoDreams.Tests/LevelEditor/ToolbarTests.cs` (`IconButtons_BakeGlyphMeshes_TintedByState` — icon
buttons bake a glyph mesh + carry no label and tint Accent/Success/TextDisabled by state, text buttons
keep a label; `WindowBar_IsSlimmed_ToolsRelocatedToTheHeader`).
**Depends on:** level-editor — "The editor toolbar's buttons drive the same shared editor instances …"
(the buttons these skin + the transport/editing gating), "Panel disclosure arrows are triangle MESHES,
not font glyphs" (the pattern this generalizes), "Every level-editor color and depth is an `EditorTheme`
role" (the icon/tooltip colours + the new `Depths.Tooltip` band + the lint), "The editor shell insets the
game viewport and renders its chrome at native resolution" (the `Editor` target + the no-`VisibleComponent`
mesh-chrome rule); rendering — `LineMeshGenerator` / `FilledTriangleMeshGenerator` / `FilledRectangleMeshGenerator`
/ `RectangleOutlineMeshGenerator` (the primitives) and the mesh `DrawComponent` draw path
(`MasterRenderSystem` skips an invalid/empty mesh — how a parked tooltip hides).

## The editor shell insets the game viewport and renders its chrome at native resolution

While the editor is composed (the run flag — the shell is CONSTANT across transport states, it
never collapses while Playing) the game composite (Main/UI/HUD layers) renders into a **smaller
centered viewport** with chrome margins reserved around it (Blender-style: a thin global top bar; a
**left panel strip** — the Entities/Systems/Scenes tab group, UX2-B; a **right panel** — the dedicated
Inspector; a thin bottom shelf; AND — carved out of the game viewport itself, just below the top bar —
the center region's **Scene panel header** band, so the game-viewport **top inset is `TopBarHeight`
+ `SceneHeaderHeight`**; `EditorChromeLayout` owns the numbers), while the chrome itself renders on
`RenderTargetID.Editor` — a render target at **native window resolution**, recreated on resize
(`EditorChromeRenderSystem`), composited 1:1 over the whole window via `RenderLayer.Native`, with
opaque dark panel backgrounds so it reads over any level. The inset lives on the `ViewportManager`
(`SetViewportInset` / `ClearViewportInset`) — the **single source of truth** — so FinalDraw
compositing and `ScaleMouseToVirtualCoordinates` follow the same rectangle **including the left
margin and the Scene-header top inset** (the Scene header is chrome margin now, so a press on it is
`OutsideViewport` — it never leaks to a viewport tool — exactly like the toolbar bar; pre-mortem #6:
one inset source, never a second rect): clicks inside the inset viewport map to correct world
positions with no extra math, clicks in the margins map to null (`CursorInputComponent.OutsideViewport`
is set, muting selection picks / gizmo drag-starts / camera-nav zoom+pan) and are consumed by the
chrome in screen space against `ScreenPosition`.
`EditorShellSystem` keeps everything applied each frame (inset, chrome relayout on window
resize, and the pointer: the OS cursor is the one visible pointer in both transport states — it
must reach the chrome — with the game cursor sprite hidden) and its `Dispose` restores both (the
`ViewportManager` and host `Game` outlive the screen). With the editor not composed (no run
flag) the inset is zero and the composite is the historical full-window letterbox,
**byte-identical**. Chrome entities carry no `VisibleComponent` (only the Main pass
consults it; its presence would pull mesh chrome into `MeshPrepSystem`, which overwrites the
identity `WorldMatrix` their absolute-pixel vertices require).

**Device pixels are the shell's one space (HiDPI).** On macOS Retina, MonoGame DesktopGL creates
its window without `SDL_WINDOW_ALLOW_HIGHDPI`, so the stock backbuffer is LOGICAL-point-sized and
the OS upscales it ~2× — even "native" chrome was blurred by that upscale. Under the editor run
flag the desktop hosts call `EditorHiDpi.TryEnable` (first Update + every resize): it re-backs
the window's GL surface at device resolution (AppKit `wantsBestResolutionOpenGLSurface`) and
widens `PresentationParameters` to `window points × backingScaleFactor` **without**
`ApplyChanges` (which would grow the OS window). From then on ONE invariant holds:
`ViewportManager.ScreenWidth/Height`, chrome layout/hit-test rectangles, and
`CursorInputComponent.ScreenPosition` are all **backbuffer device pixels** —
`ViewportManager.DevicePixelRatio` carries the ratio, `CursorInputSystem` multiplies the raw
(logical) mouse by it, and every chrome layout metric (`EditorChromeLayout` /
`SystemsPanelLayout` point constants, and the label glyph scale `LabelScale × DPR`) is scaled by
it so the chrome keeps its physical on-screen size while gaining pixel density. DPR 1 (every
non-editor / non-mac / kill-switched run, `MONODREAMS_EDITOR_HIDPI=0`) is byte-identical to the
pre-DPR behavior. The **editor overlays** (gizmo handles, selection outline, collider-proxy
outlines) share the shell's native-resolution Editor target instead of the virtual-resolution
Main target: `EditorOverlayPrepSystem` (draw-pipeline entry `editor.overlayPrep`, after
`editor.selection`) projects their world/virtual geometry to screen pixels through the pure
`OverlayProjection` (camera view matrix → aspect-fit destination; sizes scale by the fit factor,
**never** the camera zoom — replacing the old `1/Zoom` compensation with the same apparent size)
and clips every mesh to the game viewport rectangle (`OverlayMeshClip`); they occupy the low
depth band (proxy 0.02 < gizmo 0.04 < panels 0.1) so the opaque chrome panels cover them over
the margins. Hit-testing is untouched — world/virtual space, exactly as before.

**Why:** the Wave-7 user directives — "the game screen … rendered in the center … the editor
tools … placed around it, just like in Blender" and "highres and readable, independent from the
game resolution or fonts" — plus the follow-up directive "the editor should be rendered at the
native screen resolution … like Flutter … this applies to the in-game overlays: the gizmos, and
entity boundaries": the overlays used to rasterize at the game's virtual resolution on Main and
get upscaled (chunky), and on Retina even the chrome was OS-upscaled 2×. Keeping the inset on
the `ViewportManager` is what makes the mouse mapping follow the smaller game viewport
automatically; keeping ONE device-pixel space is what keeps chrome hit-tests aligned at any DPR.
**Breaks:** an inset applied only to compositing (not mouse mapping) desyncs every world pick by
the margin offsets; chrome on the virtual-resolution HUD is upscaled and blurry again; a leaked
inset (no dispose restore) squeezes the next screen into a corner; `VisibleComponent` on chrome
double-offsets the panel meshes; unscaled chrome metrics at DPR 2 halve the toolbar's physical
size and desync every chrome hit-test from the pointer; overlay sizes scaled by zoom fatten or
vanish the handles as the camera zooms; unclipped overlays draw gizmo lines over the letterbox
bars.
**Tests:** `MonoDreams.Tests/Rendering/ViewportInsetTests.cs` (inset math centered/aspect-correct,
zero-inset = legacy letterbox byte-identical, set+clear restores, resize recomputes, mouse maps
inside / nulls in margins, pixel-perfect uses the available area);
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (panels — **left + right + top + bottom + the
Scene-header band** — cover exactly the inset margins, the top inset == `TopBarHeight` +
`SceneHeaderHeight`, window-bar buttons sit in the top bar + transport buttons in the Scene header,
relayout on resize, the shell stays composed while Playing + dispose restore, `OutsideViewport` press
never picks; DPR: scale-2 metrics double incl. left + Scene header, scale-1 is the pre-DPR layout,
chrome hit-test space == chrome render space at DPR 2);
`MonoDreams.Tests/LevelEditor/OverlayProjectionTests.cs` (world→screen through camera + inset
destination, virtual space ignores the camera, zoom moves geometry but never emitted sizes,
viewport clipping, gizmo handle size constant across zoom on the Editor target, proxy outline
screen-baked at the expected pixels).
**Depends on:** rendering — "The viewport inset moves compositing and mouse mapping together",
"Three render targets, two behaviors" (+ `ViewportManager.DevicePixelRatio`); cursor —
`CursorInputSystem` (ScreenPosition × DPR) and `CursorPositionSystem` (sets `OutsideViewport`);
foundation — "Default RunMode=Play" (the flag-off/Play path must stay byte-identical).

## Every level-editor color and depth is an `EditorTheme` role; visual translucency is precomputed opaque

`MonoDreams/level-editor/UI/EditorTheme.cs` is the **single source of every color and depth** in the
module — chrome (toolbar, right strip, dialog, palette) AND viewport overlays (gizmo handles,
selection outline, collider-proxy / boundary / trigger outlines). Roles carry intent, not decoration:
`Accent` = "selected / the primary action", `Success` = "on/enabled", `Warning` / `Danger` =
"destructive-adjacent / destructive"; the `Bg0..Bg4` / `Border*` / `Text*` ramps are the neutral
warm-dark palette; the overlay colors (`OverlayAccent`, `GizmoAxisX/Y`, `OverlayBoundary`, …) are
migrated verbatim from their XNA named colors, so the migration is byte-identical to the pre-theme
render. `EditorTheme.Depths` consolidates the one Editor-target depth stack in one place (overlays
0.02–0.04 < panel 0.1 < row fill 0.3 < buttons 0.5 < checkbox mark 0.55 < thumbnail 0.56 < chip 0.58
< labels 0.6 < dialog 0.70–0.86) — the overlay systems' render-depth constants alias it
(`ProxySyncSystem.ProxyLayerDepth = Depths.ProxyOverlay`), so no render-depth literal lives elsewhere
(`SelectionSystem`'s pick-ranking constants are a separate concern — selection z-order, not the render
stack). No color literal lives anywhere else in the module either: a source-scan test forbids
`new Color(` and any `Color.<name>` token (allowlisting only `Color.Lerp` / `Color.Transparent`)
outside `EditorTheme.cs`, so adding a color means adding a role, consciously.
**Because the Editor mesh path composites premultiplied alpha, every "translucent" mesh fill is a
precomputed OPAQUE blend** — `AccentSoft` is `Accent` blended into `Bg1`, never `Accent × α` (which
would blow out near-white); the only alpha in the theme is on SPRITE tints (`GhostTint`), where alpha
is legitimate. Interaction states share one recipe — `EditorTheme.ControlFill(disabled, selected,
pressed, hoverProgress)` maps a widget's state to its fill (idle `Bg2` → hover `Bg3` → pressed `Bg4`;
selected/armed `AccentSoft` + an `Accent` border/edge; disabled `BgDisabled`) — and the ~120ms hover
fade is the engine's framerate-independent ease (`EditorTheme.AdvanceHover`, speed 18, the
`ButtonVisualSystem` recipe). Fade progress lives on each widget's OWN component/struct
(`ToolbarButtonComponent.HoverProgress`, the palette card/band/trigger structs, dialog-system fields)
— NEVER on a pooled right-strip row, which highlights INSTANTLY (a fade keyed to a repurposed pool
entity would smear across scroll).

**Why:** the pre-theme de-facto palette scattered across `EditorChromeBuilder`, `EditorDialogSystem`,
`PalettePlacementSystem` and `EditorPanelSystem` drifts — colors diverge, intent blurs, and a
"subtle" translucent mesh fill turns near-white under premultiplied compositing. One typed source + a
lint keeps the strict palette strict and the depth stack legible in one place.
**Breaks:** re-introducing a raw `Color` or a named XNA token in the module fails the lint; "fixing"
`AccentSoft` to `Accent × alpha` (or giving any mesh fill partial alpha) renders it near-white (the
premultiplied-alpha bug); keying a hover fade to a pooled row smears the highlight across scroll
(pre-mortem #6); a fill that bypasses `ControlFill`'s priority order loses the
disabled/selected/pressed states.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorThemeLintTests.cs` (no `new Color(` / named token
outside `EditorTheme.cs`; the theme file itself is the one that names them);
`EditorShellTests.ChromeBuilder_PanelsAreOpaqueAndCoverTheMargins` (panel fills stay opaque
`A==255`); `EditorPanelTests.PooledVisuals_AreBoundedByTheVisibleWindow` (the pooled row's label +
three screen-baked meshes — arrow, row fill, accent bar — stay window-bounded);
`PalettePlacementTests.GhostLifecycleTest_FollowsCursorSnapsParksAndDespawns` (the ghost sprite tint
is the theme's `GhostTint`).
**Depends on:** rendering — "The mesh render path uses premultiplied alpha — UI fills must be opaque"
(why translucency is precomputed opaque); level-editor — "The editor shell insets the game viewport
and renders its chrome at native resolution" (the one Editor target + depth stack these colors paint
into), "The editor toolbar's buttons drive the same shared editor instances" (the toolbar fills +
hover fade), "The editor's panels: a LEFT tabbed panel (Entities/Systems/Scenes), a dedicated RIGHT
Inspector, and a region-owned header framework" (the pooled-row background fill + selected-row accent
bar + the tab fills).

## Editor camera navigation pans/zooms/frames the scene directly, Edit-guarded, before the cursor's world-pos derivation

In `RunMode.Edit` the editor — not `CameraFollowSystem` — drives the shared `Camera` (the §9
interaction matrix: camera-follow is `Freeze`-gated). **The shared `Camera` is the free editor VIEW —
whatever the viewport looks through — NOT the authored game camera.** The authored game-camera state
(what `scene.camera` persists) lives on the standalone **camera rig** (UX2-E — see "The editor splits
the free VIEW from the authored camera rig" below); `CameraNavSystem` moves the VIEW freely without
touching the rig, and **Save serializes the rig, never the view**, so panning/zooming the editor no
longer moves the game camera. `CameraFollowTargetComponent` semantics are untouched: in Play the
follow system drives the same shared `Camera` as before. `CameraNavSystem` provides the view drive:
**pan** (middle-mouse drag → the camera moves the opposite way to the cursor's virtual-pixel delta so
the grabbed world point stays under the cursor — `Position -= virtualDelta / Zoom`) and **zoom** (scroll
wheel → a geometric step on `Camera.Zoom`, clamped to a sane range, default 0.25–4.0). **Frame-scene** —
centre + zoom-fit on the AABB of all renderable content (every `SpriteInfoComponent` +
`TransformComponent` entity, via the pure `GizmoTransform.SpriteWorldQuad` corners, with a margin; **no
content is a no-op**) — is the **public `CameraNavSystem.FrameScene()` method** (UX3-E), TRIGGERED by the
editor-shortcut table (Home) or the headless `view:frame` op, not a keyboard predicate on this system
(the editor keyboard bindings were consolidated into `EditorShortcuts` — see "The editor's keyboard
shortcuts are ONE chord table …"). Pan/zoom are **Edit-guarded** in `Update` (inert in Play — they must
not fight `CameraFollowSystem`); `FrameScene()` itself is unguarded and relies on its callers gating on
Edit (the shortcut context gate; the op is Edit-driven). The system is registered **before
`CursorPositionSystem`** so the camera mutation it makes this frame is the camera state
`CursorPositionSystem` reads when deriving the cursor's world position — no one-frame lag between a
pan/zoom and the cursor's world coordinate. Pan reads the cursor's **virtual** (pre-camera) position,
so it never feeds back on the camera it just moved. The math (pan sign, zoom clamp, AABB centre/fit)
is the pure, world-free `Navigation/CameraNav` so it is unit-testable without a real `Camera` or cursor.

**Why:** off-origin levels (e.g. `Blender_Level`'s content at ~(1275,-530)) are unreachable with a
pinned editor camera; frame-scene is the "jump to the level" affordance. Ordering before
`CursorPositionSystem` keeps picking/gizmo hit-tests consistent the frame after a pan/zoom. The
Edit-guard keeps every play screen byte-identical and stops the nav from fighting camera-follow in Play.
**Breaks:** a pinned camera (no pan/zoom) means a designer can't reach off-origin content; the wrong pan
sign scrolls content away from the cursor; running in Play fights `CameraFollowSystem` for the camera;
ordering after `CursorPositionSystem` lags the cursor's world coordinate one frame behind the camera;
framing on empty content would jump/zoom to a degenerate AABB instead of no-op'ing.
**Tests:** `MonoDreams.Tests/LevelEditor/CameraNavTests.cs` (`Pan_AtZoomOne_MovesCameraOppositeTheDrag`,
`Pan_AccountsForZoom`, `Pan_ViaSystem_MiddleDrag_KeepsWorldPointUnderCursor` — pan sign + zoom scaling;
`Zoom_ScrollIn_MultipliesUp_ScrollOut_MultipliesDown`, `Zoom_ClampsAtBounds`,
`Zoom_ViaSystem_ScrollStepsAndClamps` — geometric step + clamp; `FrameScene_CentersOnContentAabb`,
`FrameScene_NoContent_IsNoOp`, `ContentBounds_NoQuads_ReturnsNull` — centre on AABB + no-content no-op;
`CameraNav_InPlayMode_IsInert` — Edit-guarded).
**Depends on:** rendering — the `Camera` class (`Position`/`Zoom`/`VirtualScreenToWorld`); cursor —
`CursorInputComponent` (`MiddleButton` / `ScrollWheelDelta` / `VirtualPosition`) and `CursorPositionSystem`
(which derives the cursor's world position from the camera — hence the ordering); foundation — the
run-state model (`GameState.RunMode` + the `Freeze`-gated `CameraFollowSystem` the editor replaces in Edit);
this file — "The editor splits the free VIEW from the authored camera rig" (the rig this view drive is now
distinct from), "The editor's keyboard shortcuts are ONE chord table, gated by a single viewport context"
(the Home shortcut + the `view:frame` op that trigger `FrameScene`).

## The editor splits the free VIEW from the authored camera rig; Save serializes the rig, not the view

Under the editor the shared `Camera` is the free **VIEW** (`CameraNavSystem` pans/zooms/frames it — see
above). The **authored game-camera state** — the position/zoom/rotation `scene.camera` persists — lives on
a standalone **camera rig** entity the overlay materializes (`EditorCameraRig`): a `TransformComponent`
(position = the camera centre, so the ordinary gizmo moves it via a `TransformEditCommand` — the write-back
target is the rig's own transform, needing no new proxy machinery) + a `CameraRigComponent` (zoom + rotation;
the immutable virtual size stays on the shared `Camera`) + `EditorInfrastructureComponent` + a mesh
`DrawComponent` on the native `Editor` target (the frustum glyph, identity `WorldMatrix`, **no**
`VisibleComponent` — the chrome rule; `CullingSystem` ignores it, having no `SpriteInfoComponent`). **The
`.mdscene` format is unchanged** — `scene.camera` stays the persisted form; the rig is just where the editor
holds it. Invariants:

- **The rig is NEVER `SceneObjectComponent`-tagged**, so it never enters `entities[]` (pre-mortem #4);
  `SceneWriter.BuildScene` reads `scene.camera` FROM the rig (the overlay passes `EditorCameraRig.AsCamera()`),
  never the live view — so **moving the VIEW never dirties the scene nor changes what Save writes**, while
  moving the RIG is an ordinary undoable transform edit (dirty as any edit).
- **It is materialized/re-synced from `scene.camera` on every load** (`SceneReaderSystem`'s optional rig seam →
  `EditorCameraRig.SyncFromScene`); Restart/reload/switch rebuild its STATE, not its IDENTITY — the rig entity
  carries `EditorInfrastructureComponent`, so it survives the transport's teardown sweep and the reload re-syncs
  it (like every other scene rebuild — unsaved rig moves are discarded).
- **Null-camera default = the post-load view (UX3-A).** A scene that persists a camera syncs it to the rig
  verbatim (exact). A scene that persists `camera: null` — **every pre-UX2-E scene** (the UX2-E audit) — makes
  the rig adopt the **post-load VIEW**: the reader sequences the rig sync **after** it auto-frames the free view
  on content (`SceneReaderSystem.ApplyCamera` passes the rig `scene.camera ?? <the just-framed view>`), so **"the
  authored camera starts on the content"**, never the rig's pre-load origin ctor default. This is the UX3-A
  blank-Game-mode fix: without it, entering Game mode (which snaps the view onto the rig — `Camera := rig`) lands
  on empty world and the scene "disappears"; and because the Game-mode snapshot re-persists the origin rig,
  returning to Scene mode restores the Scene view + entities but never cures the origin rig — so **every**
  Game-mode entry is blank ("returning doesn't help"). The first Save then writes the on-content `scene.camera`,
  so the null-camera class evaporates as scenes are saved.
- **It is selectable — via the Entities tree row AND a viewport border-pick — and gizmo-editable** through the
  ORDINARY editor path. **Tree row (UX2-G):** the Entities tree folds the rig in as a **"Camera" row** even though
  it carries `EditorInfrastructureComponent` (which the tree normally hides) — it is the ONE explicit infra
  include; every OTHER infra entity stays hidden. It has no `EntityInfoComponent`, so the tree/Inspector labeler
  special-cases it to "Camera"; clicking the row selects it exactly like any entity (two-way with the viewport
  selection AND the Inspector, which reflects its `TransformComponent` + `CameraRigComponent`). This tree row is
  the **guaranteed** selection path. **Viewport border-pick:** `SelectionSystem` folds it into the SAME pick as
  the collider proxies — a **border-pick on its frustum world-rect** at `ProxyBorderPickDepth`, at the SAME
  `ProxyBorderPickTolerancePixels` (÷ zoom) tolerance the proxies/boundaries use (the frustum's fill never shadows
  a sprite under it). **Gizmo (UX2-G — no longer Move-only):** BOTH Move and Scale are legal (see the gizmo's
  `ResolveTool`). **Move** drives the rig's own transform via a `TransformEditCommand`; **Scale** edits its
  authored `CameraRigComponent.Zoom` via `CameraZoomEditCommand` — a bigger frustum ⇒ a LOWER zoom
  (`newZoom = beforeZoom / dragFactor`, the SAME `GizmoTransform.ScaleFactor` drag mapping a sprite scale uses),
  clamped to the camera-nav range `CameraNavSystem.DefaultMinZoom`..`DefaultMaxZoom` (0.25..4.0), drag-coalesced
  into one undo step and dirtying the scene like any edit; the frustum glyph + the border-pick both re-read the
  live rig zoom, so they track the drag frame-by-frame. **Rotate stays disabled** for the rig (forced to Move,
  as UX2-E left it — rig rotation editing is a future wave). It is a **first-class entity**, not a collider proxy,
  so it uses NO `ProxyBindingKind` (the proxy seam is for component-local spatial data that is NOT its own entity;
  the rig IS an entity — its `TransformComponent` and `CameraRigComponent.Zoom` ARE the edited data — so Scale
  writes back to the rig's own component, never a proxy). It is **not deletable** — `EditorCommandSystem.DeleteSelection`
  refuses it with a loud warning.
- **The glyph** draws the rig's frustum world-rect (virtual resolution ÷ rig zoom, centred on the rig) as bounds
  + the X of corner diagonals, through the existing overlay-projection path (`EditorOverlayPrepSystem` →
  `EditorCameraRig.EmitGlyph`, on the `Editor` target, clipped to the game viewport, in the `EditorTheme.CameraGlyph`
  role at `Depths.CameraGlyph`). It shows only while the view **differs** from the rig (position/zoom epsilon —
  `CameraRigGlyph.PositionEpsilon` = 0.5 world units, `ZoomEpsilon` = 1e-3); when they match ("you ARE the camera")
  it hides (empty mesh), as it does outside Edit. A large pan that scrolls the frustum off-screen clips it to
  nothing (`OverlayMeshClip`) — "pan back to see it".
- **Back-to-camera-view**: the Scene-header right-corner nav button (`EditorToolbarAction.CameraView`, the Camera
  frustum icon) + the `view:camera` op snap the view onto the rig (`Camera := rig`), so the view matches and the
  glyph hides. An editing action (dispatches Paused only).
- **Shipped games (no editor overlay) have no rig.** `SceneReaderSystem` with no rig seam applies `scene.camera`
  to the live camera directly when present (respecting the authored view), else auto-frames — the reader's
  camera split (see the reader premise). The reference shipped screen passes the reader no camera, so
  `CameraFollowSystem` still owns it, byte-identical to before.
- **Play / the Game tab** (PF-B, wired): entering Play, `CameraFollowSystem` (unfrozen) drives the shared `Camera`
  as today; spawning the Game tab sets `Camera := rig` (`SnapViewToRig`), reading the rig state this premise makes
  available — and Play from the Scene tab spawns the Game tab AND auto-plays. The rig glyph is scene-context-only
  (`ActiveContextKind == Scene`). **PF-D (wired):** a prefab context has NO rig — the gate is four-fold: the glyph
  (this rule), the "Camera" tree row (`EditorPanelSystem.MaterializeScene` skips the rig fold in a prefab context),
  the reader's rig sync (`LoadSceneRequest.SuppressCameraRig` skips `ApplyCamera`'s rig seam), and the writer (a
  prefab-context snapshot is camera-less + `PrefabWriter` nulls the camera). See "The prefab UX … (PF-D)".

**Why:** the user's ask — "the camera visible when you're not in camera view", Blender's bounds + X glyph, a
back-to-camera-view button — requires separating the free editor view from the authored game camera; today
panning the editor LITERALLY moved the game camera (Save captured the live view). The rig makes the authored
camera a thing the designer sees (the glyph) and edits (select + move) without disturbing the view, and Save now
captures authored truth, not wherever the designer happened to be looking.
**Breaks:** tagging the rig `SceneObjectComponent` serializes a camera entity into `entities[]` and breaks the
format (pre-mortem #4); capturing the live view at Save (the pre-UX2-E behaviour) means panning the editor
silently re-authors the game camera; a `ChildOf`-parented or non-infrastructure rig is cascade-disposed by the
live `DisposeOrphans` or the Restart sweep; a fill-based (not border) frustum pick shadows every sprite inside
the frustum; a `VisibleComponent` on the glyph pulls it into `MeshPrepSystem`, which overwrites the identity
`WorldMatrix` its screen-baked vertices require; a deletable rig strands the authored camera.
**Tests:** `MonoDreams.Tests/LevelEditor/CameraRigTests.cs` (`FrustumWorldCorners_*` + `ViewMatchesRig_*` — the
pure glyph math + epsilon; `RigMaterializesFromLoad_*` — file camera → rig state, view frames content;
`NullCameraLoad_RigAdoptsPostLoadView_NotThePreLoadOrigin` — UX3-A: a `camera: null` scene's rig adopts the
post-load framed view, not the pre-load origin; `SaveReadsRig_NotView` +
`MovingTheView_DoesNotChangeWhatSaveWrites_NorDirtyTheHistory`;
`CameraRig_IsNeverSceneMembership`; `Glyph_HiddenWhenViewMatchesRig_*` + `Glyph_DprAndInsetProjection_ClipsToTheGameViewport`;
`SnapViewToRig_*`; `RigBorderPick_SelectsTheRig_*` + `RigMoveDrag_IsOneUndoStep_UndoRestores`;
`RigScaleDrag_EditsZoom_NotTransformScale_OneUndoStep_UndoRestores_Dirties` +
`RigScaleDrag_ClampsZoomToTheCameraNavRange` (UX2-G — Scale → zoom, one undo step, dirties, clamped);
`RigDelete_IsRefused_*`; `RigSurvivesRestart_AndReSyncsFromTheFile`; `ShippedReader_NoRigSeam_AppliesSceneCameraToTheLiveCamera`);
`MonoDreams.Tests/LevelEditor/EditorPanelTests.cs` (`SceneTree_IncludesTheCameraRig_LabeledCamera_AndSelectsIt` —
the rig appears as a "Camera" tree row, other infra hidden, clicking it selects it) +
`MonoDreams.Tests/LevelEditor/EntitySceneTreeTests.cs` (`Build_IncludesTheCameraRig_EvenThoughItIsInfrastructure`) +
`MonoDreams.Tests/LevelEditor/GizmoTests.cs` (`ScaleFactor_MapsDragXToAUniformFactor_FlooredAboveZero` — the
shared drag→factor mapping the rig zoom divides by);
`MonoDreams.Tests/LevelEditor/SceneRoundTripTests.cs` (`MembershipFilterTest` — the rig excluded);
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (the header carries the extra Camera-view nav button).
**Depends on:** this file — "Editor camera navigation pans/zooms/frames the scene directly" (the view drive the
rig is now distinct from), "A loaded sprite entity carries a `DrawComponent` … and the reader auto-frames the
camera on content" (the reader's editor/shipped camera split), "Selection picks MAX final `LayerDepth` …" (the
border-pick ordering the rig joins), "The gizmo applies a quantized … transform edit" (the move it is edited by),
"Collider shapes are edited through standalone gizmo proxies" (the border-pick sibling — the rig is a first-class
entity, not a `ProxyBindingKind`), "The transport's Restart rebuilds the scene …" (the sweep the rig survives),
"Editor-overlay entities are standalone …" (the standalone + infra rules), "Every level-editor color and depth is
an `EditorTheme` role" (the `CameraGlyph` role + depth), "Toolbar icon buttons are procedural meshes …" (the
Camera icon + the nav button); rendering — the `Camera` class + `MasterRenderSystem`'s mesh Editor pass; camera —
`CameraFollowTargetComponent` (untouched; drives the shared `Camera` in Play).

## Injected editor cursor/op state survives the input pass; the op channel holds the session open

The editor is driven headlessly — with **no real mouse** — by two cooperating seams. First,
`CursorInputSystem` gains a `SkipHardwareRead` flag (mirroring `AKeyboardInputHandlingSystem.SkipHardwareRead`):
when set it does **not** call `Mouse.GetState()` and does **not** overwrite any
`CursorInputComponent` field, so an injected cursor state (world / virtual / screen position, delta,
the left-button down + press/release edges, scroll) survives the input pass untouched. The flag
defaults to `false`, so every existing screen is byte-identical (back-compat). Second, the editor-op
channel — an `EditorOpPlan` (a scripted list of `MoveCursor` / `LeftDown` / `LeftUp` / the
transport ops `Play` / `Pause` / `Restart` / `ToolbarAction` ops, each on a frame index) consumed by
`EditorOpReplaySystem` — injects that cursor state, drives the transport (through the bound
`EditorTransport`), and fires toolbar actions through a dispatch callback, so a test reproduces
select → gizmo-drag → undo → save (and play → pause → restart) against the **real** editor
systems. The driver
**holds the session open**: it requests exit only after its op queue drains plus a configurable tail,
so the input-replay channel's auto-exit-on-drain (which fires when its keyboard commands run out)
never kills the editor-op run before its ops + the harness's assertions complete. The driver is
registered only when a plan is present, so a normal Play run pays nothing.

**Why:** the headless integration test must exercise the real selection / gizmo / toolbar systems
with a scripted cursor (refuter HIGH 6); without the `SkipHardwareRead` seam the per-frame hardware
read would clobber the injected cursor, and without the session-hold the input replay's
auto-exit-on-drain would end the run before the editor ops complete.
**Breaks:** a hardware read that overwrites the injected cursor (selection / gizmo never see the
scripted click); a run that exits the instant the keyboard replay drains, before the editor ops run;
a per-frame cost in normal Play (the driver must be plan-gated, not always-on).
**Tests:** `MonoDreams.Tests/LevelEditor/HeadlessEditorOpTests.cs` (`HeadlessEditorOpTest` — with no
real mouse, the editor-op channel injects a click that selects a sprite, a move-drag that moves it,
a release that commits one undo step, an Undo that reverts it, and a Save that exports the scene
through a fake `IPlatformServices`; asserts the entity moved then reverted and the saved scene
matches expected, and that the driver requested exit exactly once);
`MonoDreams.Tests/LevelEditor/EditorTransportTests.cs`
(`EditorOps_PlayPauseRestart_DriveTheTransportHeadlessly`).
**Depends on:** cursor — `CursorInputSystem` (the `SkipHardwareRead` seam); foundation — input replay
(the auto-exit-on-drain the session-hold guards against).

## The pipeline registrar is the composition seam: named, ordered, gate-wrapped, runtime-toggleable — and it owns the hierarchy

A screen composes its update (and draw) pipeline through
`EditorPipelineRegistrar.Add(name, system, policy, enabledInEditByDefault?)` for single systems and
`AddGroup(name, policy, children, kind?, runner?)` for composite blocks. The registrar wraps
**every** entry in a `GatedSystem` per its declared `EditTimeBehavior` (a `RunNormally` gate is a
pass-through, so uniform wrapping costs two boolean checks and buys a uniform toggle handle) and
**retains** the entry tree at runtime — name, policy, gate + child refs, parent/children/depth,
current enabled state — exposing `Entries` (flattened pre-order: a group immediately precedes its
children), `Roots` (the tree), `SetEnabled(name, bool)`, `GetEnabledState(name)`, `GetEntry`.
**Groups: screens never pre-build opaque composites for anything they want visible.** DefaultEcs
`SequentialSystem`/`ParallelSystem` do not expose children post-construction, so a pre-built
composite registered as one entry hides its systems from the panel. `AddGroup` inverts that: the
screen registers NAMED children (auto-prefixed — `"logic"` + `"movement"` → `"logic.movement"`;
arbitrary nesting; a childless group throws) and the registrar builds the composite itself
(`SequentialSystem`, or `ParallelSystem` with a required runner for
`PipelineCompositeKind.Parallel`) over the children's gates, wrapped in ONE gate carrying the
group's policy — exactly where the old opaque composite's gate sat, so run-mode behaviour is
unchanged by a migration (a `Freeze` group still freezes the whole block in Edit).
**Enabled semantics: the toggle axis lives on the LEAVES.** A leaf's `SetEnabled(false)` flips its
own gate's `IsEnabled`: a master kill switch that stops that system in **both** modes, orthogonal to
the per-mode policy. A group has no toggle state of its own: its `EnabledState` is **derived** from
its descendant leaves (all → `On`, none → `Off`, some → `Mixed` — the tri-state the panel renders),
and `SetEnabled` on a group **cascades** to every descendant leaf. The group's own gate enforces the
group *policy* only — the toggle seam never flips it, so the derived state can never claim "enabled"
while a hidden group switch blocks everything. Unknown names throw loudly (listing what is
registered). The edit-mode default is declared at the **registration site**, never by an
interface/attribute on the system type — the same engine system may be frozen in one game's editor
and live in another's, so baking the policy into the type would force one game's choice on all (ECS
purity: the decision to run belongs to the assembler). Today `enabledInEditByDefault` must agree
with the policy's Edit column (a contradiction throws; honouring it needs the runtime per-mode
policy override, a deliberate follow-up — a silently-recorded no-effect declaration would be worse).
The registry lives on the screen (fields) and is bound onto the `EditorOverlay` via `BindPipelines`
— the seam the systems panel enumerates and toggles. Nothing in the reference compositions stays
opaque: every composite block of every screen (Examples + Demos) is a group with named children.

**Why:** the systems-panel needs a live, ordered, named view of the real pipeline with a toggle per
entry — and the user's direct feedback ("not all systems appear to enable and disable, some appear
condensed") showed that any pre-built composite is a blind spot; the plain game screen + the editor
screen must share ONE composition path (the `--editor` run flag just flips which entries are
added), or their gate matrices silently diverge.
**Breaks:** without the retained registry the panel has nothing to bind to; a pre-built composite
hides ~11 systems behind one row (the original complaint); per-screen ad-hoc `GatedSystem` wrapping
lets the editor screen and the flagged game screen drift apart; a toggle that only worked in one
mode would let a "disabled" system keep running in the other; a group gate used as the toggle axis
would lie through the derived tri-state.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorPipelineRegistrarTests.cs` (Freeze skips in Edit /
runs in Play; RunNormally runs in both; `SetEnabled(false)` stops a system in BOTH modes — including
a Freeze entry in Play; enumeration + execution preserve registration order; unknown name throws
loudly; duplicate name / add-after-build / contradicting edit-default throw; the entry exposes
policy + gate + child refs; groups: a Freeze group builds one gate around a Sequential composite and
freezes all children in Edit, Parallel kind runs all children and throws without a runner, a
childless group throws, nested groups prefix names / track depth / flatten pre-order, duplicate
child names throw while the same local name under another group is fine, a leaf toggle stops exactly
that system, a group toggle cascades to all descendant leaves, `GetEnabledState` derives
all/none/mixed through nesting, and the group gate's own `IsEnabled` is never the toggle axis).
**Depends on:** foundation — "Edit-time behaviour is a per-system policy honoured by `GatedSystem`".

## The editor run flag composes the always-on editor and the transport owns RunMode (and drives the viewport tab stack)

The editor run configuration — the `--editor` launch arg **or** the `MONODREAMS_EDITOR=1`/`true`
environment variable, both settable in an IDE run configuration and parsed by the pure
`EditorRunFlag.IsEnabled` — is the **ONLY way into the editor**. It makes the host register every
screen with `editorEnabled: true` (composing the `EditorOverlay`: selection, gizmo, undo, toolbar,
camera nav, scene save/load, headless channel) and boots the transport **Paused**
(`ScreenController.State.RunMode = RunMode.Edit`). From there the editor is ALWAYS visible — no
key toggles it away (the F1 mode toggle is retired end-to-end) — and `RunMode` is flipped
exclusively by the `EditorTransport`: the toolbar's Play/Pause + Restart buttons or the headless
`Play`/`Pause`/`Restart` ops. Since PF-B the transport ALSO **drives the `ViewportContextStack`** (the
viewport tab lifecycle) while remaining the ONE `RunMode` owner: Play from the Scene tab spawns the Game
tab, and `Transport.ActiveContextKind` (the active tab's kind) is the mode signal — the retired
`EditorViewMode` is gone (see "The viewport context stack …"). The boot
mutation is an explicit host-level opt-in **after**
construction: `GameState` still *constructs* as `Play` (preserving foundation's "Default
`RunMode = Play` preserves all existing pipelines"). With the flag off — the default — screens
compose **without** the overlay (nothing editor-related is constructed) and, because nothing then
ever flips `RunMode` and every registrar gate is a pass-through in Play, behave exactly as before
the editor existed. There is no menu entry into the editor and no dedicated editor screen — the
per-level "Edit" buttons and `LevelEditorScreen` were removed with the transport model.

**Why:** the gamedev iterates by launching their normal run configuration with one flag — no
dedicated editor build, no menu detour — and every game screen gets the editor for free through the
one shared composition path; a mode-toggle key made the editor a state the designer could
accidentally leave (the direct user directive: "when the game is started in editor mode, it should
always show the editor" — play/pause/restart replace the toggle).
**Breaks:** a flag that defaulted on (or a boot mutation baked into `GameState`'s constructor) would
flip every unflagged run into Edit — frozen physics, a black gameplay screen; a separate editor-only
pipeline definition would silently drift from the game's; a lingering toggle key would collapse the
shell mid-session with no transport affordance to bring it back.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorRunFlagTests.cs` (arg + env-var parse, including
off-values and whitespace; the flag defaults off; boot run mode at the GameState level — constructed
default is Play, flag-on composition yields Edit, flag-off stays Play);
`MonoDreams.Tests/LevelEditor/EditorTransportTests.cs` (boot-Paused mapping + the transport toggle
driving Freeze-gated systems).
**Depends on:** foundation — "Default `RunMode = Play` preserves all existing pipelines" (the
constructed default this flag deliberately does not change); this file — "The pipeline registrar is
the composition seam", "The transport's Restart rebuilds the scene from the original load request
and discards unsaved edits".

## The editor overlay is universal: under the run flag, every screen of every host composes it

The editor is **host- and screen-agnostic**: under the editor run flag, EVERY screen of EVERY host
— the Examples menu, game screen, and infinite runner; the Demos launcher and all four module demo
screens — builds its pipelines through the `EditorPipelineRegistrar` and composes the
`EditorOverlay` over its own world/camera/layers. A menu or a demo is as editable a scene as a
level. The overlay is **self-sufficient** where a screen or host lacks a prerequisite: a screen
with no cursor pipeline (the runner is keyboard-only) asks the overlay to provide one
(`provideCursorPipeline: true` → the overlay's own `CursorInputSystem`/`CursorPositionSystem` + a
minimal invisible cursor entity — no textures, since the OS pointer is the visible pointer while
the editor is composed); a host with no keyboard-action mapping layer (Demos) uses the engine's
`DefaultEditorKeys` (Delete/Z/Y/Home, entry `editor.keys`) instead of inventing edge detection; a screen with no
sprite prep gains the cull → sprite-prep → Y-sort chain under the flag so loaded scenes preview; a
screen whose `DrawLayerMap` has no Y-sorted layer (or that creates a minimal map just for the seam,
like Demos' `DemoEditor.CreateLayers`) degrades gracefully (Y-sort passes depths through and
selection picks on the final source-derived `LayerDepth`). **PF-F:** a screen that supplies no asset
catalog/bands but has a resolved project gets the palette anyway — the overlay builds a default catalog +
bands from the screen's own layer map (see "The palette lists assets as cards …"), so the Assets + Prefabs
tabs are universal too, not just on the game screen. Per-screen edit policies are declared at
the registration site: menus and demo UIs freeze `ui.interaction` in Edit (a click belongs to the
editor, never to a screen transition — the toolbar's Play transport button or the systems panel
re-arms it) but keep `layout` live
(the auto-layout solver is the screen's content placement; freezing it would boot an unlaid-out
screen under `--editor`); simulations freeze whole (the runner's treadmill block, the physics
demo's ball pipeline, the camera demo's follow/lag-zoom/hit camera writers). With the flag off, no
screen constructs anything editor-related and every pipeline is behaviourally identical to its
pre-editor shape. The recipe a new host/screen follows is `overview.md` § "Adding the editor to a
screen/host"; the composed pipeline is observable via the `EditorOverlay.LogComposition` log line.

**Why:** direct user directive — "the editor shouldn't care what screen we're using, if it's a menu
or anything else" (and, post-hands-on, "I can see the editor in Examples but not on Demos" — a host
outside the composition path is the same gap one screen up); one shared composition path per screen
prevents the per-screen gate matrices from silently drifting.
**Breaks:** a screen (or host) outside the overlay is invisible to the editor (the original
complaints: no editor on the menu; no editor on Demos); an overlay composed without the cursor
pipeline on a cursor-less screen makes selection/gizmo/toolbar read a cursor that never updates;
menu buttons live in Edit navigate away mid-editing, tearing the screen down under the editor.
**Tests:** `MonoDreams.Tests/IntegrationTests/UniversalOverlayTests.cs` (LevelSelection + runner
under `MONODREAMS_EDITOR=1` log their composed `editor.*` entries — the runner's including the
overlay-provided `editor.cursorInput`/`editor.cursorPosition`; the menu run exits through the
editor-op channel, the runner through replay auto-exit);
`MonoDreams.Tests/IntegrationTests/DemosEditorOverlayTests.cs` (all five Demos screens under
`MONODREAMS_EDITOR=1` log their composed `editor.*` entries headless, and a flag-off Demos run
composes nothing — with `RunDemosAsync` pinning the env flag off unless a test opts in); flag-off
behavior is protected by the entire pre-existing suite.
**Depends on:** this file — "The pipeline registrar is the composition seam" and "The editor run
flag opts game screens into the overlay"; foundation — "Edit-time behaviour is a per-system policy
honoured by `GatedSystem`".

## The systems panel renders the registrar tree with tri-state group checkboxes and toggles it through the registrar

The systems panel — the Wave-8a resident of the shell's right strip — lists EVERY entry of BOTH
bound pipelines (update, then draw), in execution order, as `name + policy tag + checkbox`. Since
the registrar owns the hierarchy, the listing is a **tree**: the flattened pre-order enumeration
puts a group row immediately above its children, each row indented by `Depth`
(`SystemsPanelLayout.IndentPerDepth`); child rows show their `LocalName` (the indentation conveys
the group) and repeat the policy tag only when their declared policy **differs** from their
group's (`[freeze]` = off in Edit by policy; `RunNormally` renders untagged). A LEAF checkbox is
two-state (filled = enabled, empty = disabled). A GROUP checkbox is **tri-state**, derived from its
descendant leaves (`PipelineEnabledState`): all → filled, none → empty, mixed → filled with a dark
**minus bar** over it (the Gmail/Material indeterminate mark — a dedicated fill-only bar entity at
`EditorChromeBuilder.CheckboxMarkDepth`, visible only while Mixed). Clicking any row flips it via
`EditorPipelineRegistrar.SetEnabled` with the **Gmail click convention**: checked or indeterminate →
everything under it off; unchecked → everything on (for a leaf this degenerates to the plain
toggle) — so "freeze the whole logic block" and "just silence the spawner" are both one click while
editing. The panel is chrome: native-pixel rows on `RenderTargetID.Editor` (no `VisibleComponent`),
laid out by the pure `SystemsPanelLayout`, hit-testing the cursor's raw `ScreenPosition`, hidden in
Play by the chrome pass and interaction-inert there by its own Edit guard. It scrolls by whole
lines on the mouse wheel over the strip (scrolled-out rows — bars included — are parked off-screen;
a partial line would bleed over the top/bottom bars, which share the target). It binds lazily
through `EditorOverlay.BindPipelines`. One protection: the panel **refuses to disable its own entry
AND any ancestor group of it** — its gate off (directly, or as cascade collateral) means no update,
no hit-test, and no UI path back. **Group rows collapse** (the deferred follow-up, now live): a
group row carries a disclosure arrow (`SystemsPanelLayout.ArrowGutter`/`ArrowRect`) whose click
hides/shows its children (the collapsed set lives in the pure-data `EditorPanelStateComponent`);
clicking the group row **body** still cascades the enabled toggle (Gmail), so collapse (arrow) and
enable (body) are distinct zones. The Systems panel is now one **section** of the right-strip
`EditorPanelSystem` (see the sections premise below), which renders through the pure
`EditorPanelModel` and pools its row visuals over the visible window (dynamic content — the Scene
tree + Inspector change every frame — so it re-purposes a bounded pool rather than one entity per
row).

**Why:** direct user directives — "we should be able to see the ECS systems pipeline and manually
activate or deactivate them", then "I'd like a way for all systems to be displayed, even when some
are nested in a sub pipeline … activate/deactivate the whole sub pipeline or system by system
(would need a partial checkbox … like Gmail/Material UI that puts a minus sign within the
checkbox)"; the registrar's group support was built as exactly this binding seam.
**Breaks:** toggling outside the registrar (e.g. mutating the child's own `IsEnabled`) fights game
logic that drives the same flag and bypasses the one documented seam; hit-testing `VirtualPosition`
makes the strip's rows dead (the chrome sits where the virtual mapping is null); partial-line
scroll bleeds rows over the toolbar; a self-disabling panel — or an ancestor cascade that disables
it — bricks the editor UI for the session; a two-state group checkbox would lie about a
partially-disabled block.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorPanelTests.cs` (rows mirror both pipelines' entries
+ policy tags; checkboxes reflect live enabled state; a row click calls `SetEnabled` and the gated
system actually stops in both modes — side-effect counter — and a second click re-arms it; live in
both transport states; wheel scroll in whole clamped lines; pooled visuals bounded by the visible
window; the panel refuses to disable itself; a group row's **arrow** click collapses/expands its
children while its **body** click cascades the enabled toggle Gmail-style) and
`MonoDreams.Tests/LevelEditor/EditorPanelModelTests.cs` (the pure row model: the group checkbox maps
On/Mixed/Off to filled / filled-with-minus-bar / empty and tracks the registrar; group collapse
hides children yet keeps the group row; child rows show the local-name label).
**Depends on:** this file — "The pipeline registrar is the composition seam" (the binding + the
derived tri-state), "The editor shell insets the game viewport and renders its chrome at native
resolution" (the strip, the `ScreenPosition` rule, the no-`VisibleComponent` rule), "The editor's
panels: a LEFT tabbed panel (Entities/Systems/Scenes), a dedicated RIGHT Inspector, and a
region-owned header framework" (the panel this section — the Systems tab — lives in).

## The editor's panels: a LEFT tabbed panel (Entities/Systems/Scenes), a dedicated RIGHT Inspector, and a region-owned header framework

`EditorPanelSystem` is ONE parameterized system (an `EditorPanelRole`), not two classes: the **left
strip** is the `LeftTabs` role — a **tab bar** (**Entities | Systems | Scenes**) over a body of
collapsible sections — and the **right strip** is the `RightInspector` role — a slim title header
over the selection-bound component list (UX2-B moved the tab group to the left region and made the
Inspector its own dedicated panel; the old right-strip tab shell is gone). Both share ONE
`EditorPanelStateComponent` (injected by the overlay — ECS purity, state in a component) and the same
pooled-row machinery, `SystemsPanelLayout` scroll model, and native-resolution chrome rules (Editor
target, no `VisibleComponent`, `ScreenPosition` hit-test, live in both transport states). The left
active tab lives in `EditorShellStateComponent.ActiveLeftTab` (the shell-state premise below).

**The panel-header framework.** Each region owns a **header band** at the top of its region rect
(`EditorChromeLayout.TabStrip`/`RegionBody` split it off; the body is where rows render):
- The **left** header is the **tab bar** — per-tab persistent widgets (each with its OWN hover-fade
  progress, never a pooled row — pre-mortem #6): the active tab a `Bg1` fill merging into the body +
  a 3pt `Accent` underline, an inactive tab a `Bg0` fill (hover-fading toward `Bg2`) + a `Text1`
  label. A left-strip **splitter** sits on the region's right (viewport-facing) edge.
- The **right** header is a slim **title** ("Inspector") — no tabs. Its splitter is on the left edge.
- The **bottom** shelf header is its `Assets` tab strip (unchanged).
- The **center** region's header is the **Scene panel header** — chrome carved out of the game
  viewport (part of the ONE inset; see "The editor shell insets the game viewport …"), hosting the
  transport now and later the tool cluster / Entity menu / mode toggle / camera button (empty slots).

`EditorPanelModel` has two pure assemblers (unit-testable with hand-fed inputs — no world, no
GraphicsDevice): **`Build(activeTab, …)`** for the left panel and **`BuildInspector(…)`** for the
right. The three left tabs:

- **Entities** — the world's entities as a **tree** (the `Entities` section, collapsible), the tree
  ALONE (the Inspector left for its own panel). Built by the pure `EntitySceneTree.Build`: roots
  first, each entity's `ChildOfComponent` descendants nested one indent deeper (pre-order), with
  **editor-infrastructure entities hidden** (the `EntitySet` is `With<TransformComponent>
  Without<EditorInfrastructureComponent>`, so chrome / gizmo overlays / proxies / the cursor / the
  state entities never appear); a child of a hidden entity re-parents to its nearest included
  ancestor. **The ONE infra exception (UX2-G): the camera rig** — although it carries
  `EditorInfrastructureComponent`, `MaterializeScene` unions in a second `With<CameraRigComponent>`
  set so the rig IS folded back into the pool (every OTHER infra entity stays hidden), giving the
  designer a way to select + inspect the authored camera from the tree (see "The editor splits the
  free VIEW from the authored camera rig"). Each row is labelled by its `EntityInfoComponent` name
  (else type, else a stable panel-local id — and the camera rig, which has no `EntityInfoComponent`,
  is special-cased to **"Camera"**) and is **selectable**: clicking a row sets `SelectedComponent`
  (the same tag `SelectionSystem` sets from a viewport click — and the panel's chrome click is
  `OutsideViewport`, so `SelectionSystem` never clobbers it), highlighted in the tree.
- **Systems** — the pipeline listing (the systems-panel premise above), with per-group collapse.
- **Scenes** — project info rows (the project root path, **middle-truncated** to the strip width, and
  the levels dir) plus the **Scenes list**: one selectable row per `SceneCatalog` entry, the current
  entry rendered selected (`AccentSoft` + `Accent` bar) with a `Warning`-colored `●` prefix when
  dirty. Clicking a row switches through the dirty-gated select flow (see "Game screens declare their
  bound scene; the Scenes panel lists screens + scene files and switching IS selecting").

The **dedicated Inspector panel** (right) lists the selection's **attached components**
(`ComponentInspector.Inspect` over DefaultEcs `ReadAllComponents`); each row expands (on demand) to
its **member values** (guarded per-member so an arbitrary component never throws); no selection →
"(no selection)". It has no in-body section header (the region's slim header IS the title). Because
both panels read the world's single `SelectedComponent`, **selection is two-way ACROSS the panels**:
a tree click in the LEFT panel updates the RIGHT Inspector the next frame. **PF-A upgrades this
Inspector into an editable Chrome-DevTools element/styles pane** — a filter field, type-colored member
values, inline value editing, and add/remove components through undoable commands — see "The Inspector is
editable, DevTools-style: filtered rows, type-colored values, and value/add/remove edits through undoable
commands".

**Ops grammar delta (UX2-B).** `panel:tab <entities|systems|scenes>` switches the left tab (the
bottom shelf's `assets` tab op is unchanged); a **section op activates the tab that hosts its section
first** (`EditorPanelModel.HostTab`), so `panel:systems`/`panel:entities` (renamed from
`panel:scene`), `panel:group <name>`, `panel:select <name>` keep working; `panel:inspect <type>`
expands a component's members in the Inspector panel (no tab activation — the Inspector is always
shown). **`panel:inspector` is REMOVED** — the Inspector dissolved into the standalone right panel,
so there is no section to toggle. When rows overflow the body the panel draws a **slim scrollbar**
(a `Border` track + a `BorderStrong` proportional thumb, draggable — see the shell-state premise;
the left panel's own token is `LeftScrollbar`, the Inspector's is `RightScrollbar`), hidden when
they fit. **PF-A adds the editable-Inspector ops** `inspector:filter <text>` / `inspector:edit
<Component.Member> <value>` / `inspector:add <key>` / `inspector:remove <key>` (the editable-Inspector
premise).

**Why:** the UX2-B design (editor-shell-ui-ux §1–§2, the confirmed Unity-style arrangement): a
left tab group + a dedicated right Inspector, each region owning its header, is the modularity that
gives any future tool a home. Parameterizing ONE panel system over two regions honours the
no-duplicate-ways tenet (a second class would fork the pooling / scroll / hit-test). Selection
integrates both ways so the viewport, the tree, and the Inspector are one selection, not three.
**Breaks:** two panel classes fork the pooled-row machinery (the tenet violation); building every
section every frame (no tab filter) re-stacks the old crowded strip; state on the system instead of
a component violates ECS purity; two state components (not one shared) split the collapse vs the
Inspector-expand state; not hiding `EditorInfrastructureComponent` entities floods the tree with
chrome/gizmo/proxy noise; one-entity-per-row (instead of pooling) unbounds the entity count;
reflecting a component without per-member guards crashes the editor on the first throwing getter; a
tree selection that `SelectionSystem` could clear (or vice-versa) would make the viewport and tree
fight over one `SelectedComponent`; a section op that did not activate its tab would silently no-op.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorPanelModelTests.cs` (tab filtering — Entities tab shows
the tree not Inspector/Systems, Systems tab shows only Systems, Scenes tab shows info + the scene
list; `HostTab` maps a section to its tab; `MiddleEllipsis` keeps head+tail; section collapse; scene
rows indent + highlight + subtree collapse; `BuildInspector` lists + expands with NO section header);
`MonoDreams.Tests/LevelEditor/EditorPanelTests.cs` (the Systems tab mirrors both pipelines with policy
tags; an Entities row click sets `SelectedComponent`; `panel:select` headless; editor-infra hidden;
the camera rig folded in as a selectable "Camera" row while other infra stays hidden —
`SceneTree_IncludesTheCameraRig_LabeledCamera_AndSelectsIt` (UX2-G);
section-header + group-arrow collapse; pooled visuals bounded by the window + the fixed tab/scrollbar
overhead; the RightInspector-role panel lists + expands; **`LeftTreeClick_UpdatesTheRightInspectorPanel`**
— a tree click in the left panel binds the right Inspector, two-way across panels);
`MonoDreams.Tests/LevelEditor/EditorShellStateTests.cs` (a section op activates the host tab;
`SetActiveTab`); `EntitySceneTreeTests.cs` / `ComponentInspectorTests.cs` (the tree + inspector data
model).
**Depends on:** this file — "The editor shell's region sizes, tabs, and drag ownership live in one
shell-state component" (the active tab + the left/right scrollbar drags + `LeftWidthPt`), "The systems
panel renders the registrar tree …" (the Systems tab), "The editor shell insets the game viewport …"
(the strips + the Scene-header inset + chrome rules), "Selection picks MAX final `LayerDepth` …"
(`SelectedComponent`); foundation — `ChildOfComponent` (the tree edges), `EntityInfoComponent` (row
labels).

## The Inspector is editable, DevTools-style: filtered rows, type-colored values, and value/add/remove edits through undoable commands (PF-A)

The dedicated right-strip Inspector (`EditorPanelRole.RightInspector`) is an **editable** element/styles
pane over ECS, modeled on Chrome DevTools. Its body leads with a **filter field**
(`EditorPanelStateComponent.InspectorFilter`) that narrows component AND member rows by a
case-insensitive substring on names+values (a component survives on a type-name match — showing all its
members — or on any member match — showing the matching members); `Esc` clears + unfocuses. Each member
value renders **type-colored** via `InspectorValue.Role`/`ForRole` — the documented mapping: numbers
`Info`, strings `Warning`, `true` `Success` / `false` `Danger`, enums `Accent`, null/empty/unsupported
`TextMuted` — for read-only AND editable rows. Clicking an **editable** member (a writable field/property
of type `float`/`int`/`string`/`Vector2`/`bool`/enum) edits it: a `bool` toggles immediately, an enum
cycles to the next member, and the rest open an inline field seeded with the current value; `Enter`
commits, `Escape`/click-elsewhere cancels, and a parse failure keeps the field open shown in `Danger`.
Every edit — value, add, remove — is exactly one undoable command on the SHARED `EditorHistory`
(dirty-tracked): `MemberEditCommand` (reflection get → mutate → `Set` write-back, so a STRUCT component's
edit sticks — pre-mortem #5), `AddComponentCommand`, and `RemoveComponentCommand` (snapshotting the
removed value field-for-field). The trailing **+ Add component** row opens a **filterable command-palette
popup** (`EditorContextMenuSystem.OpenFiltered`) whose candidates are the serializer registry's registered
types (`ComponentSerializerRegistry.RegisteredComponents`) MINUS the types already on the entity MINUS
structural (`ChildOfComponent`/`SceneEntityIdComponent`, `registry.IsStructural`) MINUS never-addable
(`SpriteInfoComponent`/`BoundaryComponent` — authored by the palette / boundary tool). Guardrails:
`TransformComponent` is not removable (its `×` refuses with a status hint); structural components render
no `×`; adding/removing `SpriteInfoComponent` also adds/removes the paired transient `DrawComponent`, and
undo restores BOTH (pre-mortem #6). **Keyboard ownership:** while the filter is focused or a member is
being inline-edited, `EditorPanelSystem.OwnsKeyboard` is true — the composing screen ORs it into the host
keyboard's `ShouldSuppressInput` and the shortcut gate ORs it into `ViewportShortcutContext` (a new
`InspectorEditing` term) — so typing (`g`/`s`/`r`/Delete/a name) in a field never fires an editor chord or
a game key. The whole surface is headless-drivable through `inspector:filter|edit|add|remove` ops
(`edit <Component.Member> <value>`, the member being the LAST dotted segment so a `core.Transform.Position`
key parses).

**Decisions (documented):** `SpriteInfoComponent` is EXCLUDED from the Add candidate list — a sprite needs
an asset, so it is placed via the palette (the `AddComponentCommand` still honors the pairing if a headless
op force-adds it); the delete affordance is an inline `×` in the component row's right gutter (`Danger` on
hover when deletable, `TextMuted` + refusing for `TransformComponent`); the inline field + the filter
accept the constrained lowercase char set (letters/digits/`.`/`-`/`,`/space) shared with the dialog —
arbitrary values arrive via the ops.

**Why:** the design's Chrome-DevTools north star + the framework-not-library tenet — reflection over
`ReadAllComponents` plus the serializer registry makes every serializable component inspectable/addable with
no per-type UI code; routing every edit through the ONE history keeps undo/dirty coherent; the
registry-driven candidate set is the honest "what can this scene persist".
**Breaks:** a struct edit without the get-modify-`Set` write-back silently vanishes (pre-mortem #5);
adding/removing `SpriteInfoComponent` without its paired `DrawComponent` returns the blank-sprite class of
bug (pre-mortem #6); removing `TransformComponent` leaves an entity with no spatial component; a second
history / edit path splits undo; typing in a field that leaked to the shortcut layer fires G/S/R/Delete
mid-edit; offering a structural component in Add corrupts the entity's scene identity.
**Tests:** `MonoDreams.Tests/LevelEditor/InspectorEditingTests.cs` (`MemberEditCommand` struct write-back +
undo/redo + the class-component Vector2 property + the read-only refusal; the parse matrix incl. invariant
culture + `Vector2` "x, y" + enum-by-name + the enum cycle; the type-color `Role`/`ForRole` mapping;
`AddComponentCommand`/`RemoveComponentCommand` + the SpriteInfo⇔DrawComponent pairing BOTH ways incl. undo;
the registry `RegisteredComponents`/`IsStructural`/`TypeForKey` accessor + `InspectorAddCandidates.Derive`;
the default-initializer table; the `ViewportShortcutContext.InspectorEditing` gate);
`EditorPanelModelTests.cs` (the filter row + optional add row; filter narrowing by component name / member
name / member value, case-insensitive; member type-color + editability; the per-component delete
affordance); `EditorPanelTests.cs` (click a bool → toggles, an enum → cycles, a value → an inline field +
`Enter` commits + dirty + `Escape` cancels; the filter field focus + type + `Esc` clears + unfocuses;
Transform-`×` refused while RigidBody-`×` removes + undo restores; the Add row raises the request +
candidates exclude present/structural); `EditorContextMenuTests.cs` (the filterable popup narrows live +
picks by path); `ComponentInspectorTests.cs` (the member type/editability/role enrichment).
**Depends on:** this file — "The editor's panels: a LEFT tabbed panel …, a dedicated RIGHT Inspector …"
(the panel this upgrades), "Bounded undo with drag-coalescing" (the history every edit pushes to), "The
editor history tracks a dirty save-point signal" (the dirty bit each commit moves), "A loaded sprite entity
carries a `DrawComponent` …" (the pairing rule the add/remove enforce), "The component-serializer registry
is opt-in per type …" (the candidate source), "Editor context menus are a data-driven popup …" (the popup
this makes filterable), "The editor's keyboard shortcuts are ONE chord table …" (the gate the field
extends); foundation — the `ShouldSuppressInput` host-keyboard seam.

## The editor shell's region sizes, tabs, and drag ownership live in one shell-state component; splitters resize it and the inset derives from it

`EditorShellStateComponent` (pure data on one editor-infra entity) is the **single source of truth**
for the resizable region sizes (`LeftWidthPt` and `RightWidthPt` clamped to 180..600, `BottomHeightPt`
clamped to 96..320 — defaults 240/280/168 mirror `EditorChromeLayout.LeftPanelWidth`/`RightPanelWidth`/
`BottomBarHeight` byte-for-byte), the **active tab per region** (`ActiveLeftTab`, `ActiveBottomTab`),
the **viewport tab strip** (PF-B — `ViewportTabs`, the ordered `ViewportTabDescriptor` list, +
`ActiveViewportTab`), and the **drag ownership** (`ActiveDrag`). `EditorChromeLayout`'s region methods and
`ViewportInset` take these sizes (defaulting to the constants when omitted), so the game-viewport inset,
the FinalDraw compositing, the mouse mapping, and every chrome system's per-frame layout **all derive from
the one model** — the existing single-source invariant, now runtime-adjustable. The `ViewportTabs`
descriptors are the tab strip's **render source** — the `ViewportContextStack` is their ONE writer
(rewritten on every tab spawn / close / switch), and `ViewportTabStripSystem` renders + hit-tests ONLY
them, so the strip is data-driven (a future prefab tab is one more descriptor; see "The viewport context
stack …"). UX2-B activated the left region (the tab group) UX-B had reserved at 0; marked terrain: a
region→panels map (`RegionPanels`) models the layout as data (Left = the tabs, Right = the Inspector,
Bottom = Assets) and reserves a `MenuBar` at size 0, so a future menu bar / drag-docking is a state
mutation, not a rearchitect.

**Splitters** are 4pt drag zones on each region's **viewport-facing edge** (the left strip's right
edge, the right strip's left edge, the bottom shelf's top edge), inside the reserved margin (so a
press there is `OutsideViewport` and never a game click) and clear of the row/card content.
`EditorShellSystem` owns them: a press claims `ActiveDrag`, a drag resizes the region (device-px
delta → points via the DPR, clamped — the left splitter grows dragging RIGHT, the right splitter
grows dragging LEFT), and the shell relayouts the chrome + re-applies the inset **the same frame** (no
cached region rect survives a resize — pre-mortem #2). **Scrollbar thumbs** (left strip, right strip,
bottom shelf — each its own `Left`/`Right`/`Bottom` token) share the SAME `ActiveDrag` field, owned by
their own panel/palette. The token is claimed on the press edge and **held through the release edge,
cleared the frame after** (when the button is fully up): so on the release edge every other chrome
consumer still sees a non-`None` value and stands down — making "a splitter/scrollbar drag never also
fires a panel row / card / tab / toolbar click" independent of pipeline weave order (pre-mortem #3).
The toolbar additionally suppresses all dispatch while `IsDragging` (a drag that releases over a
button must not fire it). New ops: `shell:left <pt>` / `shell:right <pt>` / `shell:bottom <pt>`
(resize, clamped) and `panel:tab <…>` (switch a tab).

**Why:** UX-B makes the strip/shelf resizable and tabbed, and the inset+mouse-mapping must never
desync from what compositing shows — so region sizes and the active tab must live in ONE model that
every consumer re-reads each frame. The single drag token is what keeps a splitter drag from
double-firing as a click without relying on system order.
**Breaks:** two sources for a region size desync every world pick by the margin delta (the class of
bug the viewport-inset premise exists for); an unclamped resize hides the viewport or a panel; a
splitter drag that also fires a row/card/toolbar click on the same press (no shared token) mis-toggles
a system or arms a prop mid-resize; a cached region rect across a splitter-drag frame mis-hits every
control.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorShellStateTests.cs` (left + right + bottom region-size
clamp + defaults == the chrome constants; marked-terrain regions incl. the now-active Left;
`ViewportInset`/`LeftPanel`/`RightPanel`/`BottomBar`/`SceneHeader` honour the runtime sizes; tab-strip
/ `RegionBody` / left+right+bottom splitter / scrollbar geometry with DPR-2 doubling of every new
metric; a live right-splitter drag AND a live left-splitter drag through `EditorShellSystem` grow the
strip, clamp, and release the token the frame after; a foreign drag mutes a panel click; a panel
scrollbar-thumb drag (`LeftScrollbar`) scrolls the rows; the headless
`panel:tab`/`shell:left`/`shell:right`/`shell:bottom` ops reach the named dispatch);
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (the DPR-1 inset — left + Scene-header top inset —
and the DPR-2 doubling, the splitter/tab fills opaque on the Editor target).
**Depends on:** rendering — "The viewport inset moves compositing and mouse mapping together" (the
inset this feeds); this file — "The editor shell insets the game viewport and renders its chrome at
native resolution" (the chrome layout + device-pixel space), "The editor right strip is a tabbed
shell …" (the active tab + scrollbar this state drives), "The palette lists assets as cards …" (the
bottom shelf's scrollbar + tab strip).

## Panel disclosure arrows are triangle MESHES, not font glyphs

Every collapsible row in the right strip (section headers, pipeline group rows, scene-tree entities
with children, inspector component rows) shows its disclosure caret as a **filled triangle mesh** —
`FilledTriangleMeshGenerator` fed the three points from the pure `SystemsPanelLayout.ArrowTriangle`
(right-pointing ▸ collapsed, down-pointing ▾ expanded), baked into a raw `DrawComponent`
(`Type = Mesh`, identity `WorldMatrix`, native `Editor` target, **no `VisibleComponent`**, no
`SimpleButtonComponent`) that the panel refills each frame in the arrow gutter (`ArrowRect`) — exactly
the screen-baked-mesh pattern the gizmo overlays use. An arrow with no collapse (a non-collapsible row,
or a parked pool slot) is hidden by **emptying its mesh** (an invalid mesh is skipped by
`MasterRenderSystem`), the mesh analog of parking a text entity off-screen. The pooled row visual is
therefore one `DynamicText` label + one mesh arrow (plus the pipeline-row checkbox/minus-bar), still
bounded by the visible window. The click hit-zone is unchanged — `ArrowRect` still splits the
collapse-caret from the enable-toggle body — so the systems-panel enable-vs-collapse distinction holds.

**Why:** the pre-mesh panel drew the caret as the ASCII `v`/`>` because the editor's BitmapFont is not
guaranteed to carry the Unicode triangle glyphs — which looked like a stray letter (Rider hands-on
feedback: the `v` reads as a typo, not a disclosure arrow). Drawing it as a mesh removes the font-glyph
dependency entirely and gives a crisp, native-resolution, DPR-correct Blender-like caret. Meshes are
already the editor's font-independent draw path (gizmo handles, selection outlines), so this reuses it
rather than adding a new draw component (the no-duplicate-ways tenet). **This same "meshes, not font
glyphs" rationale now covers the whole toolbar icon set** (UX2-C) — the caret was the seed of
`EditorIcons` (see "Toolbar icon buttons are procedural meshes tinted by state …").
**Breaks:** rendering the caret as a font glyph reintroduces the coverage dependency (a missing glyph
box, or the `v` mis-read as text); giving the arrow entity a `VisibleComponent` (or a
`SimpleButtonComponent`) would pull it into `MeshPrepSystem`/`ButtonMeshPrepSystem`, overwriting the
identity `WorldMatrix` its screen-baked vertices require; parking a mesh by position (instead of
emptying it) leaves the last triangle drawn at an off-screen coordinate that the identity matrix ignores.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorPanelTests.cs`
(`ArrowTriangle_PointsRightWhenCollapsed_DownWhenExpanded` — the pure geometry's orientation;
`DisclosureArrow_IsAMesh_NotATextGlyph` — the panel emits triangle-mesh arrows and NO `v`/`>` label;
`GroupArrowMesh_OrientationTracksTheExpandedState` — the group row's arrow mesh matches the
expanded ▾ then, after an arrow-click collapse, the collapsed ▸ triangle; `GroupArrowClick_Collapses…`,
`WhilePlaying_StaysInteractive`, `Wheel_ScrollsByClampedLines`, `PooledVisuals_AreBoundedByTheVisibleWindow`
stay green through the change).
**Depends on:** this file — "The systems panel renders the registrar tree …" and "The editor's panels:
a LEFT tabbed panel (Entities/Systems/Scenes), a dedicated RIGHT Inspector, and a region-owned header
framework" (the rows whose carets these are), "Toolbar icon buttons are procedural meshes tinted by
state …" (the toolbar icon set this pattern seeded); rendering — the mesh
`DrawComponent` draw path (`MasterRenderSystem` skips an invalid mesh) and `FilledTriangleMeshGenerator`;
"The editor shell insets the game viewport …" (the Editor target + the no-`VisibleComponent` chrome rule).

## Collider shapes are edited through standalone gizmo proxies; write-back targets the bound component, through the undo history

Colliders are **not** entities — `BoxColliderComponent.Bounds` (an entity-relative rectangle) and
`ConvexColliderComponent.ModelVertices` (local-space vertices; `WorldVertices` is derived) are
component-local spatial data on the game entity, so neither the selection (which picks rendered
sprites) nor the transform gizmo (which edits `TransformComponent`) can grab them directly. The
Wave-8b mechanism (generalized in island Slice 2): when the selected entity carries collider
components in Edit, `ProxySyncSystem` materializes **standalone proxy entities keyed
`(kind, index)`** — one whole-shape proxy per collider (index 0) plus, while the convex family's
own proxy is selected, one per-vertex proxy per `ModelVertices` entry (see the vertex-editing
premise below) — `GizmoProxyComponent` is the pure-data
binding descriptor `(target entity, ProxyBindingKind, index)`; the proxy carries a
`TransformComponent` kept at the shape's **world** centre (the gizmo pivot / selection anchor)
and a cyan outline VISUAL emitted separately by `ProxySyncSystem.EmitOverlays` (the
`editor.overlayPrep` draw entry): screen-baked on the native-resolution Editor target at depth
0.02, projected through `OverlayProjection`, fit-scaled (never zoom-scaled) stroke, clipped to
the game viewport, **no** `VisibleComponent` (the chrome rule — `MeshPrepSystem` would overwrite
the identity `WorldMatrix` the screen-baked vertices require); transform placement and visual
both re-derive from the bound component **every frame** (cheap: selected entity only), so they
cannot diverge, and the proxies despawn on deselect / mode exit / target death. Proxies join the
**same** pick (`SelectionSystem` folds them in through the same rank+depth+id ordering, at the
constant `ProxyBorderPickDepth` — decoupled from the visual's Editor-band depth — hit-testing
only the shape's **border** within `8px/Zoom` so a sprite-covering collider never shadows its
entity) and
the **same** gizmo drag (move handle at the proxy pivot; the tool is forced to Move for proxies).
A selected **box** proxy additionally exposes eight **resize handles** — the box's corners and
edge midpoints (pure `BoxResize` math), hit-tested BEFORE the centre move handle — each moving
exactly the grabbed edge(s) of `Bounds` with the opposite edge anchored and sides clamped at
`BoxResize.MinSize`, through the same claim + coalescing-drag path.
The write-back never touches the proxy's own transform: each drag frame pushes a
`ColliderEditCommand` (before/after snapshot of `Bounds` or `ModelVertices`) against the **bound
game entity**, inside the coalescing transaction — one drag = one undo step — and the convex
write-back refreshes `WorldVertices` + `BroadPhaseAABB` in the same command (physics is frozen in
Edit; nothing else would). `ProxySyncSystem` also refreshes the selected entity's convex
`WorldVertices` per frame so the `ColliderDebugSystem` outline (which coexists as the global,
selection-unaware diagnostic) tracks edits instead of drifting. The binding kind is the
generalization seam, now proven three times (whole shapes, convex per-vertex handles, and — Slice 3
— `ProxyBindingKind.BoundaryVertex` per `BoundaryComponent.Points` entry): a new editable spatial
field is another `ProxyBindingKind` + a `ProxyGeometry` derivation case + a `GizmoSystem` write-back
case (a future spline-control-point binding for the road tool, Waves D/F, is the same recipe) —
never a second proxy mechanism. Boundary vertices differ only in that they materialise on PLAIN
selection (a boundary IS its points — no shape proxy to click through) and carry no convexity
constraint (an open polyline), writing back through `BoundaryEditCommand` (which re-fires the bake).
**Not everything border-picked is a proxy.** The UX2-E camera rig reuses the SAME border-pick ordering
(`SelectionSystem` folds its frustum rect in at `ProxyBorderPickDepth`, exactly like a collider/boundary
border) but is a **first-class entity** — its `TransformComponent` IS the edited data, so the ordinary
gizmo moves it via a `TransformEditCommand` and it needs NO `ProxyBindingKind`. The proxy seam exists
precisely because colliders are NOT entities; the rig is, so it stays off the seam (see "The editor
splits the free VIEW from the authored camera rig").

**Why:** the user clicked the red collider debug outlines and couldn't drag them (the outlines are
unselectable per-frame visualization entities with no back-link); restructuring colliders as child
entities was explicitly deferred to an engine RFC (`docs/level-editor/roadmap.md`) because
collision is a per-frame hot path — proxies deliver the editing affordance without touching the
collision data model. Commands must target the game entity because proxies are transient: an undo
entry recorded against a despawned proxy would dangle.
**Breaks:** a `TransformEditCommand` against the proxy makes undo a no-op after deselect (dangling
entity) and never moves the collider; fill-based proxy hit-testing makes a collider-covered sprite
unselectable while its proxy exists; skipping the `UpdateWorldVertices` refresh leaves a stale
`BroadPhaseAABB` (contacts silently missed back in Play — the collision premise) and a debug
outline frozen at the pre-edit shape; a `ChildOf`-parented proxy is cascade-disposed by the live
`DisposeOrphans`.
**Tests:** `MonoDreams.Tests/LevelEditor/ProxyTests.cs` (lifecycle: one proxy per collider on
select, despawn on deselect / mode exit / target death, standalone + survives a HierarchySystem
frame, selecting a proxy keeps the family; sync: owner transform move re-derives the proxy and
refreshes convex world data; write-back: box drag shifts `Bounds` by the delta and convex drag
translates all `ModelVertices` + refreshes `WorldVertices`/`BroadPhaseAABB`, owner transform
untouched, one drag = one undo step, undo restores the exact prior shape, redo re-applies;
selection: border click picks the proxy through the same pick path, inside click picks the owner,
and `ProxyClickOwnershipTest_MoveHandlePressAtShapeCentre_KeepsProxySelectedAndDrags` — pressing
the selected proxy's centre move-handle is claimed by the gizmo, so the same frame's selection
pass neither deselects the proxy nor despawns the family, and the drag completes;
pure inverse-transform delta math); `MonoDreams.Tests/LevelEditor/ProxyVertexTests.cs` (the
`(kind, index)` family lifecycle);
`MonoDreams.Tests/LevelEditor/BoxResizeTests.cs` (pure edge math per handle + MinSize clamp;
system-level: each handle adjusts exactly the expected `Bounds` edge(s) through one undo step,
undo restores the exact rect, the centre press still moves the whole box, and a resize-handle
press is claimed so the same-frame selection pass keeps the proxy).
**Depends on:** collision — "`ConvexColliderComponent.BroadPhaseAABB` must be refreshed when
vertices change"; this file — "Editor-overlay entities are standalone; delete snapshots the
disposed sub-graph" (the standalone rule), "Bounded undo with drag-coalescing" (the transaction),
"Selection picks MAX final `LayerDepth` with a selection-owned tiebreak" (the ordering proxies
join).

## The transport's Restart rebuilds the scene from the original load request and discards unsaved edits

Under the editor run configuration the transport (`EditorTransport`, held by the `EditorOverlay`)
is the ONE owner of `GameState.RunMode` — **Paused** = `RunMode.Edit`, **Playing** = `RunMode.Play`
(the shell stays composed in both — the transport buttons and the systems panel remain interactive
while the game runs in the inset viewport; the editing tools are Edit-guarded and therefore inert
while Playing) — **and it DRIVES the `ViewportContextStack`** (PF-B — the tab lifecycle; see "The
viewport context stack …"). **Play from the Scene tab spawns the Game tab**: `Play` in the Scene
context calls `EnterGameMode` (snapshot + rig-view adopt) BEFORE flipping RunMode to Play, so the
snapshot-before-flip guarantee holds. `RunMode` stays with the transport (`Transport.ActiveContextKind`
is the active-tab kind, superseding the retired `ViewMode`) — ONE owner, no parallel snapshot path.
**Restart** returns the world to the state of the ORIGINAL load, in this exact
order: set Paused (nothing simulates over the teardown), `EditorHistory.Clear()` (the recorded
commands reference entities about to die — replaying them in either direction would dangle),
`stack.ResetToScene()` (drop any Game tab, land on the Scene tab, forget the in-memory snapshot),
`world.Remove<CurrentLevelComponent>()` + `Remove<CurrentBackgroundColorComponent>()` (the LDtk
parsers subscribe to the component **added** event — a re-publish over a still-set component fires
*Changed* and never re-parses), dispose every scene entity, then invoke the screen-recorded
`Reload` (each screen registers "re-publish my original load request" in `Load`: the game screen
re-publishes `LoadLevelRequest(levelId)`, the menu re-runs its UI builder, the runner re-runs its
create methods). Restart while Playing also lands **Paused**; Restart **while the Game tab is active
also lands the Scene tab with the in-memory snapshot dropped** — the snapshot IS an unsaved edit, so
Restart's discard contract covers it with no special case (the disk reload is the source of truth;
see "The viewport context stack …"). **Unsaved live edits since the load
are DISCARDED** — the standard play-mode trade-off; Save first to keep them. The survival boundary
is exclusion by editor markers (the engine has no entity↔level association): an entity survives
when it carries `EditorInfrastructureComponent` (every editor-owned entity — chrome, panel rows,
gizmo overlays/proxies, the gizmo-state entity, and the UX2-E **camera rig** — is tagged at creation; the
rig's IDENTITY thus survives while its STATE re-syncs from `scene.camera` on the reload, like every other
scene rebuild — see "The editor splits the free VIEW from the authored camera rig"), when it is the cursor
pipeline (`CursorControllerComponent`/`CursorInputComponent` — screen input infrastructure, not
scene content), or when the screen's `KeepAlive` predicate names it (system-constructed screen
infrastructure held by reference, e.g. the dialogue UI root via `DialogueStateComponent`) — keeps
propagate DOWN the `ChildOf` chain. **A KeepAlive entity is also delete-guarded (PF-F, the crash fix):**
`Transport.IsScreenInfrastructure` (the `KeepAlive` predicate plus the `ChildOf` ancestor walk, now
public) is consulted by `EditorCommandSystem.DeleteSelection`, which REFUSES deleting such an entity
everywhere delete routes (command / menu / Delete key) with the status hint "screen infrastructure -
cannot be deleted" — disposing the dialogue-UI root a live `DialogueSystem` still references NRE'd the
game. The Entities tree also **hides** KeepAlive infrastructure in a PREFAB context (it survives the
sweep by design but is not prefab content); in a scene context it stays visible. A Restart with no
recorded `Reload` is a **loud no-op**
(warning, nothing disposed): tearing the world down with no way to rebuild it would strand the
designer on a blank screen.

**Source-first reload (UX-D, pre-mortem #5).** The screen's `Reload` re-publishes its original
`LoadLevelRequest`, which resolves through `NativeLevelLoader.CreateProbe`. That probe now resolves
**source-first when the editor project is resolved** — so a Restart reflects the last **Save** to the
source tree, not the stale bundled copy from the last build (see level-loading — "`LevelLoadRequestSystem`
resolves `LoadLevelRequest` native-only"). A bound menu/runner screen (no `LoadLevelRequest`) instead
re-runs its optional `NativeLevelLoader.TryPublishSceneLoad` **inside** `Reload`, so a Restart on those
screens restores the bound scene's placed content too, not just the code-built UI. **Save Backup As…
(UX-D) composes exactly this:** it writes the dangling backup file, then calls `Restart` to return the
working scene to its on-disk (source) truth — the backup captured the edits; the working scene reloads
clean.

**Why:** direct user directive — the F1 toggle is retired; "play/pause and restart buttons to play
the game, pause it or reset it" are the way the designer moves between editing and playing, and
restart must be trustworthy: it either fully rebuilds the loaded scene or refuses loudly.
**Breaks:** an uncleared history dangles undo entries against disposed entities (undo after
restart crashes or silently no-ops against the wrong world); a restart that skips the
`CurrentLevelComponent` removal never re-parses (the documented broken-hot-reload path); a sweep
without the editor-marker exclusion disposes the chrome/panel/gizmo state (the editor UI vanishes
on restart); disposing the cursor pipeline kills all mouse input for the session; a silent no-op
restart (or a teardown without reload) strands a blank world; a reload that read the **bundled** copy
would silently revert to the last build, not the last save (the stale-bundle bug — pre-mortem #5).
**Tests:** `MonoDreams.Tests/LevelEditor/EditorTransportTests.cs` (restart disposes scene entities
and re-runs the recorded load; editor infrastructure + cursor + `KeepAlive`-named sub-graphs
survive; unsaved-edit discard demonstrated — edit a transform through the history, restart, the
value is back at the loaded state and undo is a no-op; the world-level components are removed;
restart while Playing lands Paused; a reloadless restart is a loud no-op; the headless
`Play`/`Pause`/`Restart` ops drive the same paths);
`MonoDreams.Tests/LevelEditor/CameraRigTests.cs::RigSurvivesRestart_AndReSyncsFromTheFile` (the camera
rig's identity survives the sweep and its state re-syncs from the file — unsaved rig moves discarded);
`MonoDreams.Tests/LevelEditor/NativeFirstLoadTests.cs::StaleBundleRegression_ResolvedContextLoadsSource_UnresolvedLoadsBundled`
(the reload reads the SOURCE bytes under a resolved context — the pre-mortem #5 regression);
`MonoDreams.Tests/LevelEditor/SceneSourceWriteTests.cs::SaveBackupAs_WritesDanglingFile_NoSavePoint_NoBundle_ThenRestartReloadsBoundScene`
(Save Backup As… composes a write + Restart that reloads the bound scene).
**Depends on:** foundation — "Default `RunMode = Play` preserves all existing pipelines" (the
transport is the only mode owner); level-loading — the `LoadLevelRequest` →
`CurrentLevelComponent`-added parse trigger this premise routes around, and "`LevelLoadRequestSystem`
resolves `LoadLevelRequest` native-only" (the source-first probe the reload goes through); this file —
"The editor run flag composes the always-on editor and the transport owns RunMode", "Bounded undo with
drag-coalescing" (the history the restart clears), "The editor's Save dialog is a modal three-action
chooser …" (Save Backup As… composes write + Restart).

## The viewport context stack is the ONE tab-switching mechanism; the Game tab is its discard consumer (PF-B)

The center region is a **tab strip** (`[ Scene ] [ ▶ Game × ]`), not a Scene/Game mode toggle. All
switching goes through ONE mechanism — `ViewportContextStack` (pre-mortem #4: exactly one snapshot /
sweep / restore implementation; the Game tab is its FIRST consumer, never a parallel path). Each open
tab is a `ViewportContext` `{ Kind (`Scene` / `Game` / `Prefab`), Id, Label, Closable, IsDiscard, and —
captured when the tab is backgrounded — a `SceneData` snapshot + a `CameraViewSnapshot` view + the
history dirty flag }`. `EditorTransport` **owns `GameState.RunMode` and DRIVES the stack** (the retired
`EditorViewMode` is superseded by `Transport.ActiveContextKind` = the active tab's kind — the ONE mode
signal; the transport is still the ONE RunMode owner). The context carries **no `RunMode`** — the
transport is the sole authority for the live run state (leaving the Game tab always lands Paused; the
Scene/Prefab tabs are edited Paused). The mechanism:

- **The Scene tab** is always index 0, never closable, never a discard context.
- **Spawn the Game tab** (`Play` from the Scene tab → `Transport.EnterGameMode` → `stack.EnterGame`):
  snapshot the ACTIVE (Scene) context **FIRST** — `SceneWriter.BuildScene(world, rig.AsCamera(), layers)`
  → a held `SceneData` (no file I/O) + the `EditorHistory` dirty flag + the Scene VIEW — **before**
  `RunMode` flips to Play (pre-mortem #7: `Play` calls `EnterGameMode` *before* `state.RunMode = Play`,
  so no simulation frame mutates the scene before capture), adopt the game-camera view
  (`EditorCameraRig.SnapViewToRig`), then push a **discard** Game tab **KEEPING the live world as the
  sandbox** (NO sweep on enter — the world already IS the scene). Play spawns + activates + auto-plays in
  ONE action; pressing Play while the Game tab is already active does NOT re-snapshot (**one snapshot per
  session**). The Game tab's `▶` marker reuses the play glyph.
- **`SwitchTo(tab)`** (a general tab switch): snapshot the active context (via `CaptureSnapshot` + the
  rig + the VIEW + the history dirty flag) **UNLESS it is a discard context** — a Game tab is NEVER
  re-snapshotted on leave (discard semantics verbatim) → **sweep** (the transport's survivor-sparing
  `DisposeSceneEntities` — `EditorInfrastructureComponent` / cursor / `KeepAlive` survive) → reader-restore
  the target's snapshot via an **in-memory `LoadSceneRequest(SceneData)`** (so re-tag, texture rehydration
  incl. `file:` keys, `DrawComponent` restore, and camera-rig re-sync are ALL shared with the file load —
  pre-mortem #2: the reader is the ONLY restore implementation) → `EditorHistory.Clear()` (undo after a
  switch is a no-op — pre-mortem #3) + `MarkDirty()` reproducing the target's captured dirtiness → restore
  the target VIEW over the reader's auto-frame, **only when `CameraViewSnapshot.IsValid`** (a positive
  zoom; UX3-A pre-mortem #2 — a zeroed/unwired capture would let `Camera.Zoom` clamp 0 → `0.1f` and blank
  the view at the origin, so it is NOT applied — the reader's on-content auto-frame is kept). A discard
  context being left is **dropped from the strip** afterward — the Game tab never persists in the
  background. **Leaving the Game tab** (`Transport.ExitToSceneMode` = `SwitchTo(Scene)` landing Paused;
  its `×`; a scene switch) discards the sandbox and restores the Scene: **the Scene tab always shows
  exactly what Save would write.**
- **Save is blocked while the Game tab is active** — `SaveBlockReason.GameMode` (checked after `Playing`,
  before `NoProjectRoot`) through the SAME `EditorOverlay.SaveBlock(state, projectContext, activeKind)`
  guard the toolbar / dialog / headless dispatch all consult. The Scenes-panel `●` + the status bar
  reflect the Scene context's CAPTURED dirty flag while the Game tab is active (`Transport.SnapshotWasDirty`
  = `stack.SceneContext.WasDirty`), never sandbox churn.
- **Restart** (`Transport.Restart` → `stack.ResetToScene`): drop every non-Scene tab, forget the Scene
  context's in-memory snapshot (a Restart reloads from disk — the snapshot IS an unsaved edit its discard
  contract covers), land on the Scene tab, Paused (see "The transport's Restart …"). A scene switch while
  the Game tab is active leaves it FIRST, then runs the normal dirty gate on the RESTORED state — one gate
  flavor, no bypass.
- **The dirty-close gate (pre-mortem #9).** `stack.DecideClose(index)` is the pure gate the transport's
  `CloseTab` routes: the Scene tab is **`Refused`** (never closable — **a dirty Scene context can never be
  silently discarded by any stack operation**: `SwitchTo` away from a persistent context always snapshots
  it, and `DecideClose` refuses the Scene); the Game tab is **`DiscardImmediately`** (its `×` is discard by
  nature — no dialog, the existing exit semantics); a **dirty** persistent closable context (a future
  prefab tab, PF-D) is **`ConfirmDirty`** (route the Save & Close / Discard / Cancel confirm via
  `Transport.ConfirmDirtyClose`, wired in PF-D — never reached in PF-B); a **clean** one is **`CloseClean`**
  (close + return to the Scene tab). Restart is the ONLY path that discards the Scene, and it is the
  explicit "discard unsaved edits, reload from disk" contract, not a silent tab operation.
- **Prefab contexts (PF-D, wired).** `ViewportContextStack.OpenPrefab` pushes a closable, non-discard
  `Prefab` context: it snapshots the active context, sweeps, and reader-restores the prefab's content with
  the camera rig **suppressed** (the transient `RestoringPrefabContext` flag drives
  `LoadSceneRequest.SuppressCameraRig` — a prefab has no rig, pre-mortem #8), clearing the history so the
  fresh context is clean. One tab per prefab; `CaptureSnapshot` builds a **camera-less** scene for a
  prefab context. `DecideClose` returns `ConfirmDirty` for a dirty prefab tab (the `Transport.CloseTab`
  activates it first, then routes `ConfirmDirtyClose`); `CloseCleanContext` closes it (returning to the
  Scene when it was active). Play is disabled in a prefab tab. See "The prefab UX … (PF-D)".

**Why:** the user's ask — the Scene/Game toggle becomes tabs (Play spawns a Game tab), a real workflow
toward prefab tabs — with the Unity sandbox model preserved (poke the running scene "just to test",
edits never leak into the saved level). ONE mechanism keeps the Game tab and future prefab tabs from
drifting into two snapshot paths (pre-mortem #4); reusing the reader keeps "what you edit is what ships"
true after any switch; snapshotting before the Play flip keeps the restore point uncorrupted.
**Breaks:** a second snapshot path for the Game tab drifts from the stack (pre-mortem #4); a snapshot
taken after `RunMode = Play` bakes a simulated frame into the restore point (pre-mortem #7); a forked
restore forgets re-tag / rehydration / `DrawComponent` and reloads blank or saves empty (pre-mortem #2);
applying a zeroed/`default` view snapshot blanks the view at the origin (UX3-A pre-mortem #2); not clearing
history dangles undo against restored entities (pre-mortem #3); a saveable sandbox writes throwaway pokes
into the versioned level; a switch/close that silently discards a dirty Scene loses authored work
(pre-mortem #9); reflecting sandbox dirtiness on the Scenes panel lies about unsaved work.
**Tests:** `MonoDreams.Tests/LevelEditor/ViewportContextStackTests.cs` (the mechanism: `EnterGame`
snapshots the Scene + adopts the rig + no sweep + keeps the live world; the Game round-trip sweeps +
restores + drops the tab + the Scene state survives; `EnterGame` twice is one snapshot; `ExitToScene`
restores the captured dirty + clears history; `ResetToScene` drops the Game tab + forgets the snapshot;
the `DecideClose` truth table — Scene `Refused` even when dirty, Game `DiscardImmediately`; a dirty Scene
survives a round-trip, never silently discarded);
`MonoDreams.Tests/LevelEditor/ViewportTabStripTests.cs` (the strip renders the descriptors, active tab
`Bg1` + accent underline, the Game `▶` + closable `×`; descriptor-driven — one tab then two, extras
parked; body click → `SwitchToTab`, `×` click → `CloseTab` with close taking priority; suppressed during
a shell drag; DPR-2 tabs within the scaled header; **Play spawns + activates the Game tab and the strip
renders it**);
`MonoDreams.Tests/LevelEditor/EditorGameModeTests.cs`
(`EnterGameMove_Exit_RestoresPositionExactly_UndoNoOp_DirtyAndViewRestored`;
`Exit_RestoresTheCapturedDirtyState_NotSandboxChurn`;
`PlayInSceneMode_SnapshotsBeforeRunModeFlipsToPlay_AndAutoEntersGame`;
`PlayInGameMode_DoesNotReSnapshot_OneSnapshotPerSession`;
`SaveBlock_GameMode_IsDistinguishable_PlayingWins_SceneModeSavesAgain` +
`ToolbarSaveButton_IsInertInGameMode_ViaTheSharedGuard`;
`RestartInGameMode_LandsSceneMode_ReloadsDiskState_DropsSnapshot`;
`Camera_Enter_AdoptsRig_Exit_RestoresSceneView_RigUntouched`;
`GameModeRoundTrip_SharesTheReader_FileKeySpriteKeepsTextureRehydrationAndDrawComponent`;
`GameModeEntry_LandsPlaying_ExitLandsPaused`;
`Exit_WithZeroedOrUnwiredCaptureView_KeepsTheAutoFramedView_NeverBlanks` +
`CameraViewSnapshot_Default_IsInvalid_RealCapture_IsValid`);
`MonoDreams.Tests/LevelEditor/EditorTransportTests.cs::Transport_OwnsViewMode_DefaultScene_ToggleEntersAndExits_ExitLandsPaused`;
`MonoDreams.Tests/LevelEditor/GameModeBlankSceneReproTests.cs`
(`FreshBoot_NullCamera_EnterGameMode_ContentStaysVisible` +
`FreshBoot_NullCamera_EnterExitReEnter_WorldIntact_AndGameModeStaysVisible` — a fresh boot of a
`camera: null` off-origin scene stays visible spawning the Game tab AND across a round-trip, proven
through the REAL `CullingSystem`).
**Depends on:** this file — "The transport's Restart rebuilds the scene …" (the sweep reused + the
reset-to-Scene), "Scene round-trip reconstructs from registered components …" (the reader restore the
in-memory overload shares), "A loaded sprite entity carries a `DrawComponent` …" (the `DrawComponent`
restore + rig re-sync + view framing), "The editor splits the free VIEW from the authored camera rig"
(`AsCamera`/`SnapViewToRig`/`SyncFromScene`), "The editor history tracks a dirty save-point signal"
(`MarkDirty` restoring the captured dirty state), "Save is blocked while Playing, while the Game tab is
active, or when no project root is resolved" (the `GameMode` reason), "Game screens declare their bound
scene …" (a scene switch leaves the Game tab first), "The editor toolbar's buttons drive the same shared
editor instances …" (the tab strip's home + the transport buttons), "The editor shell's region sizes,
tabs, and drag ownership live in one shell-state component …" (the `ViewportTabs` descriptors the stack
writes).

## Viewport presses belong to exactly one tool family: `EditorToolMode` gates selection, gizmo, and placement

The shared `GizmoStateComponent` carries a coarse `EditorToolMode` (`SelectTransform` default;
`Place`; `Boundary` — island-authoring Slice 3; the brush modes Scatter/GroundPaint/Road are
reserved names). `SelectionSystem` and `GizmoSystem` process viewport presses **only** in
`SelectTransform` (they early-out otherwise — the gizmo also cancels any in-flight drag, hides its
overlays, and claims nothing); the palette's placement acts only in `Place`; the `BoundaryToolSystem`
lays polyline vertices only in `Boundary` (a viewport click lays a vertex, Enter/double-click
commits, Escape/right-click cancels). This composes with the finer `PressClaimed` click-ownership
rule, which keeps resolving handle-vs-scene *within* `SelectTransform`. The toolbar's transform-tool
buttons AND the boundary-tool button are a radio over the modes (each disarms the others — the
`ToolBoundary` button disarms the palette then enters `Boundary`; a transform-tool button disarms
the palette back to `SelectTransform`), as are Escape and right-click.

**Right-click double-duty (UX2-D).** A right-click keeps meaning *disarm* while a tool is armed
(`Place`/`Boundary` — the palette/boundary read `RightButtonPressed` and cancel), but in
`SelectTransform` with nothing armed it now **opens the entity context menu**: `SelectionSystem`
(dormant in the other modes) picks the entity under the cursor with the SAME `TryPick` used by the
left-click selection, and on a HIT selects it (keeping an existing selection if it was the
already-selected one) and raises `ViewportContextMenuRequested` for the overlay to open the menu. A
right-click over **empty** viewport opens no menu and clears no selection (click-empty stays a
left-click behavior); the gizmo's `PressClaimed` and the middle-drag camera nav are unaffected (right
button only). So a stray menu can never eat an armed tool's cancel gesture (pre-mortem #5).

**Why:** with placement live, a single viewport press would otherwise be claimed by three systems
at once — a placement click would simultaneously re-pick (or click-empty-clear) the selection and
grab a gizmo handle. One mode field on the existing shared state entity (extend, don't
new-component) is the unambiguous owner declaration, and mirrors the Unity/Godot convention that
activating a brush visibly deactivates the transform gizmo.
**Breaks:** placing a prop deselects the previous selection or drags it under the cursor;
conversely a selection click while armed stamps an unwanted prop; a stale `Place` mode with
nothing armed mutes every tool family (the palette self-heals it back to `SelectTransform`).
**Tests:** `MonoDreams.Tests/LevelEditor/PalettePlacementTests.cs`
(`ToolModalityTest_PlaceModePressNeitherSelectsNorDrags`,
`ToolModalityTest_EscapeRestoresSelectTransform`, `ToolModalityTest_RightClickDisarms`);
`MonoDreams.Tests/LevelEditor/SelectionTests.cs` (`ViewportRightClick_OnEntity_SelectsAndRequestsMenu`,
`ViewportRightClick_OnEmpty_NoMenuNoClear`, `ViewportRightClick_OnAlreadySelected_KeepsItAndRequestsMenu`,
`ViewportRightClick_InPlaceMode_NoMenu`, `ViewportRightClick_WhenGizmoClaimed_NoMenu`,
`ViewportRightClick_InPlayMode_Inert` — the UX2-D right-click double-duty).
**Depends on:** this file — "Selection picks MAX final `LayerDepth` with a selection-owned
tiebreak, target-aware" (the claim rule this composes with), "Editor context menus are a
data-driven popup: one model, two anchors …" (the menu the right-click opens).

## Editor context menus are a data-driven popup: one model, two anchors, modal like the dialog

The `EditorContextMenu` primitive (UX2-D) is a popup list on the native-resolution `Editor` target,
driven by a **pure menu MODEL** — a flat item list (`EditorMenuItem`: label, action-id `Path`,
`Enabled`, `Danger`; `Separator`; a ONE-level `Submenu`) assembled by `EditorContextMenuModel`
(`EntityMenu` / `EntitiesPanelMenu` / `ScenesPanelMenu`) and laid out by the pure
`EditorContextMenuLayout` (fixed-width box clamped to the window; a submenu opens beside its parent,
flipping left when there is no room). Because the content is DATA, the SAME model renders **two
ways** — as a right-click **context menu** (`EditorContextMenuSystem.OpenAt`, at the cursor) or the
Scene-header **`Entity ▾` dropdown** (`OpenBelow`, anchored under the button) — the discoverable twin
of the right-click. A clicked/`menu:pick`ed leaf fires its action-id `Path` through a `dispatch`
callback the overlay maps to the SAME shared editor instances (Order → the within-band nudges, Delete
→ the snapshotting `DeleteEntityCommand`, Add Empty Entity → the undoable `CreateEntityCommand`, Create
Empty Scene → the dialog), so the menu system stays game-agnostic.

**Modality — the menu owns the pointer while open, like the dialog.** Each open frame it hit-tests its
own items FIRST, then **consumes** the cursor's pointer edges (the `EditorDialogSystem` recipe), so no
mouse-driven editor system downstream acts on the same click. It is woven **immediately after
`editor.dialog`** (entry `editor.contextMenu`, `RunNormally`) so, in the rare case both could open, the
dialog consumes first and wins; and `OpenAt` **refuses to open while blocked** (`isBlocked` = the dialog
is open OR a shell splitter/scrollbar drag owns the pointer) — "if the dialog is open, menus never open".
The keyboard half is the screen ORing `Menu.IsOpen` into the host keyboard system's `ShouldSuppressInput`
(with `Dialog.IsOpen`), so **Escape closes the menu** instead of quitting the game. Closed by an item
click, a click-away (closes without acting), or Escape. Items use the UX-A state model (instant hover
fill — pooled rows never fade, pre-mortem #6; `Danger` label for destructive items; `TextDisabled` when
disabled). Chrome rules hold: `SimpleButtonComponent` box/fills + `DynamicTextComponent` labels + a
screen-baked ▸ triangle mesh, all on the `Editor` target, `EditorInfrastructureComponent`, **no
`VisibleComponent`**, parked when closed, in a dedicated `EditorTheme.Depths.Menu*` band ABOVE the
tooltip so a menu is never occluded.

**Four surfaces, one primitive.** The viewport right-click opens the entity menu (Order ▸ Bring
Forward / Send Backward, separator, Delete `Danger`) — see the tool-modality premise for the
right-click composition; the Entities-panel right-click opens Add Empty Entity plus, when a tree row is
under the cursor, that row's entity items above a separator (the panel raises `ContextMenuRequested` +
exposes `EntityAtPoint`; the overlay selects the row entity and builds the menu); the Scenes-panel
right-click opens Create Empty Scene…; the header `Entity ▾` button opens the entity menu below it,
acting on the current selection (its items disabled when nothing is selected). Ops:
`menu:open <viewport|entities|scenes|entity>` (viewport/entity use the current cursor position),
`menu:pick <path>` (e.g. `order/forward`, `delete`, `add-empty`, `create-scene`), `menu:close` — all
headless-testable; the Create-Empty-Scene modal is driven by the existing `dialog:name|confirm|cancel`
grammar (its confirm routes to `ConfirmCreateScene`).

**Checkable items — the Overlays dropdown (UX3-D).** The model gains a `Kind = Toggle` item with a
`Checked` state, rendered with a small check box in the row's left gutter (theme roles: `Text1` outline,
`Success` fill when on) BEFORE the label, so labels stay aligned across checkable and plain rows. A
Toggle click dispatches its `Path` but does **not** close the menu — Blender's flip-several-overlays-in
-one-open — then the system re-derives the model through an optional `rebuild` hook passed to
`OpenAt`/`OpenBelow`, so the check flips in place; an `Action` item still closes on click. This is the
SAME one-model/two-anchors/modality primitive, now with toggles, and it hosts the fifth surface: the
Scene-header **Overlays** dropdown (`EditorToolbarAction.Overlays`, the two-overlapping-circles icon),
whose model is `EditorContextMenuModel.OverlaysMenu` (a Grid toggle, a Grid Spacing ▸ submenu of preset
Action items whose current value is `Checked`, an Outline Selected toggle, a Camera toggle). Its item
paths route through `ViewportOverlayOps` (see the viewport-overlays premise). `FindByPath` matches Toggle
leaves too, so `menu:pick overlay/grid` toggles headlessly.

**Why:** the design's §4 — a single data-driven popup that serves the viewport right-click, both panel
right-clicks, and the discoverable header dropdowns (Entity + Overlays), without bespoke widgets (the
no-duplicate-ways tenet); modal capture is required or a stray viewport click/keystroke leaks to the
tools behind it (most dangerously Escape quitting the game). One model + anchors keeps the context menu
and the header dropdowns provably identical. Toggle-without-close matches Blender (a designer flips grid
+ outline + camera in one open); the rebuild hook keeps the rendered check honest.
**Breaks:** a menu that does not consume the pointer lets its item-click also select/place behind it; a
menu that opens while the dialog is open (or during a splitter drag) fights it for the pointer; not
ORing `Menu.IsOpen` into `ShouldSuppressInput` makes Escape quit the game instead of closing the menu;
a `VisibleComponent` on the menu chrome pulls it into `MeshPrepSystem`, which overwrites the identity
`WorldMatrix` its absolute-pixel vertices require; a menu below the dialog band is occluded by a modal.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorContextMenuTests.cs` (the pure model + layout — items /
submenu / disabled / danger / `FindByPath`, height, window-clamp, submenu right/left flip; the system —
open/close, `Pick` dispatches a submenu leaf + closes, disabled-pick no-op, `isBlocked` refuses to open,
item-click dispatches + consumes the cursor, click-away closes, Escape closes, hover opens a submenu then
item-click dispatches; the menu→command wiring — Order nudges the selected sprite, Delete is the
snapshotting command + undo restores; `AddEmptyEntity`; the Create-Empty-Scene dialog collision refusal +
accept + empty-name + the canonical empty-world write; the UX3-D toggle menu —
`OverlaysMenu_HasGridToggle_SpacingSubmenu_OutlineToggle_CameraToggle`,
`ToggleItem_Click_DispatchesWithoutClosing_AndRefreshesTheCheckInPlace`,
`Pick_SpacingPresetAction_SetsTheSharedStep_AndCloses`, `CheckRect_SitsInTheLeftGutter_BeforeTheLabel`);
`MonoDreams.Tests/LevelEditor/SelectionTests.cs`
(the viewport right-click); `MonoDreams.Tests/LevelEditor/EditorPanelTests.cs`
(`RightClickInThePanel_RaisesTheContextMenuRequest_AndMapsTheRowEntity`).
**Depends on:** this file — "Viewport presses belong to exactly one tool family" (the right-click
composition), "Selection picks MAX final `LayerDepth` …" (the reused `TryPick`), "Editor-overlay entities
are standalone; delete snapshots the disposed sub-graph" (the Delete item), "The editor's Save dialog is a
modal three-action chooser …" (the modal machinery the Create-Empty-Scene mode extends + the cursor-consume
recipe), "Game screens declare their bound scene … switching IS selecting" (the dirty-gated switch Create
Empty Scene reuses), "Scene serialization is canonical and byte-stable …" (the empty scene's bytes); cursor
— "Button press/release edges derive from CursorInputSystem's own previous-state" (why an item's release-edge
action survives the menu's own consume).

## The editor's keyboard shortcuts are ONE chord table, gated by a single viewport context

The editor's GLOBAL keyboard shortcuts live in ONE data table — `EditorShortcuts` (chord →
`EditorShortcutAction`) — read by `EditorShortcutSystem` through the foundation chord layer
(`KeyChordTracker` over the injectable `Func<KeyboardState>` seam), NOT as scattered per-action keyboard
predicates. Bindings this wave (Blender parity — bare letter keys are reserved for tools): `PlatformCommand+Z`
→ Undo, `PlatformCommand+Shift+Z` → Redo, `Shift+A` → open the **Add** menu at the cursor (the Entities-panel
add section), `Delete` → delete the selection, `Home` → frame the scene. The pre-existing **bare `Z`/`Y`
undo/redo were removed** (bare keys are tools), and the delete/frame keyboard predicates that used to live on
`EditorInputBindings` / `EditorCommandSystem` / `CameraNavSystem` were consolidated here — there is now ONE
place to read the editor bindings. `EditorInputBindings` keeps only the **tool-contextual** keys (Escape
cancel, boundary commit, palette ghost-rotate Q/E, the optional order nudges), whose context is a tool being
armed/laying, not the shortcut gate.

**One context gate.** Every binding checks the SAME `ViewportShortcutContext` predicate: cursor over the game
viewport (`!CursorInputComponent.OutsideViewport`), no dialog open, no context menu open, Paused
(`RunMode.Edit`). This mirrors the modal suppression game keys already get (the screen ORs `Dialog.IsOpen ||
Menu.IsOpen` into the host keyboard's `ShouldSuppressInput`), so a chord never fires while the designer types
in a dialog field, hovers a panel, or is Playing. `EditorShortcutSystem` is woven with the input-owner block,
immediately AFTER `editor.dialog` + `editor.contextMenu` (registrar entry `editor.shortcuts`, `RunNormally`)
so modality wins; the gate's `Editing` flag makes it inert while Playing, so it needs no extra Edit wrapper.
Its `KeyChordTracker` advances every frame (edges never leak across a context change), but dispatch happens
only when the gate allows. `commandIsMeta` (⌘ vs Ctrl) is injected by the overlay (`OperatingSystem.IsMacOS()`,
a runtime query in the Composition layer, mirroring `EditorHiDpi` — the foundation chord layer stays blind).

**Same shared instances, and ops are the headless channel.** The overlay's dispatch maps each action to the
SAME instance the toolbar/menu use — Undo/Redo → the shared `EditorHistory`, Delete → the snapshotting
`EditorCommandSystem.DeleteSelection`, FrameScene → the shared `CameraNavSystem.FrameScene`, AddMenu → the ONE
`OpenContextMenu` coordinator — never a second path. Because input replay carries `AInputState` actions, not
raw chords (the foundation chord replay caveat), each binding's ACTION is exercised headlessly through an op:
Undo/Redo via the toolbar `Undo`/`Redo` actions, Delete via `menu:pick delete`, FrameScene via the new
`view:frame` op, AddMenu via the new `menu:open add` op; the chord→action MATCHING is unit-tested at the
tracker/table level. **UX3-F: the bare `G`/`S`/`R` modal transforms join the table** (Blender parity — bare
letter keys ARE the tools) as `ModalGrab`/`ModalScale`/`ModalRotate`; the dispatch enters the shared
`ModalTransformSystem`, and the gate gains a fourth term — `ModalActive` (`Modal.IsActive`) — so while a
modal transform owns the keyboard NO shortcut fires (a re-pressed G/S/R cannot re-enter mid-session). See
"The modal transform (G/S/R) owns the pointer + keyboard …" below.

**Why:** design §4 — combo input is an ENGINE feature (foundation) and the editor's chords are one table so
there is one place to read/extend them; the context gate stops panel typing or a dialog from triggering tools
(pre-mortem: a stray keystroke leaking to the tools behind a modal); consolidating the old predicates removes
the "second path" a per-action wiring would leave. Bare `Z`/`Y` had to go for Blender parity (bare keys arm
tools). Ops are the headless channel because replay does not carry chords.
**Breaks:** two keyboard-reading paths for undo (the shortcut AND a leftover predicate) double-fire or drift;
a shortcut that skips the context gate fires while a dialog field has focus (typing "a" arms Add) or while
Playing (a viewport keystroke belongs to the game); weaving `editor.shortcuts` before the dialog/menu lets a
chord act on the frame a modal opened; a bare-`Z` undo re-introduces the non-Blender binding and collides with
tool letters; a second history/command/menu instance means the shortcut's undo can't reverse the gizmo's edits.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorShortcutTests.cs` (`Table_BindsEachChordToItsAction_WindowsResolution`,
`Table_CmdShiftZ_ResolvesToRedo_NeverUndo_PreMortem3`, `Table_MacResolution_MetaFiresCommand_CtrlDoesNot`,
`Table_BareZ_AndBareY_HaveNoBinding_TheRemovedUndoRedo`, `Table_BindsBareGSR_ToTheModalTransforms_UX3F`;
`Context_AllowsEditing_OnlyOverViewport_NoModal_WhilePaused` (incl. the `ModalActive` term);
`System_Fires_OverViewport_Paused`, `System_DoesNotFire_OverAPanel`, `System_DoesNotFire_WhileDialogOrMenuOpen`,
`System_DoesNotFire_WhilePlaying`, `System_DoesNotFire_WhileAModalTransformOwnsInput`, `System_BareZ_DoesNotUndo`;
`Shortcut_CmdZ_DrivesTheSharedHistory_UndoActuallyUndoes`,
`Shortcut_Delete_DrivesTheSharedCommandSystem_RemovesTheSelection`, `Shortcut_ShiftA_OpensTheAddMenu_AtTheCursor`);
the tracker matrix + PlatformCommand resolution are `MonoDreams.Tests/Foundation/KeyChordTests.cs`.
**Depends on:** foundation — "Key chords fire on an exact-modifier press edge; `PlatformCommand` resolution is
injected" (the chord layer this reads); this file — "Editor context menus are a data-driven popup …" (the Add
menu Shift+A opens + the `editor.contextMenu` weave this follows), "The editor's Save dialog is a modal …" +
"Editor context menus … modal like the dialog" (the `Dialog.IsOpen`/`Menu.IsOpen` suppression the gate mirrors),
"Editor camera navigation pans/zooms/frames the scene directly" (the `FrameScene` Home triggers),
"Bounded undo with drag-coalescing" (the shared `EditorHistory` Undo/Redo drive), "Editor-overlay entities are
standalone; delete snapshots the disposed sub-graph" (the `DeleteSelection` Delete drives).

## The modal transform (G/S/R) owns the pointer + keyboard for one coalesced, undoable session

Blender's `G`/`S`/`R` enter a **modal transform** over the current selection (`ModalTransformSystem`,
entered from the `EditorShortcuts` table). While active the mouse drives the edit WITHOUT a button held —
**grab** = the world delta from the entry cursor, **scale** = the distance-ratio factor to the pivot,
**rotate** = the swept angle about the pivot — applied EVERY frame through the SAME
`EditorHistory.BeginTransaction`-coalesced `TransformEditCommand` path a gizmo drag uses, so **one modal
session = exactly one undo step** that a single undo fully reverts. `X`/`Y` toggle an axis lock (the same
key clears it, the other replaces; grab zeroes the other component, scale edits one axis, rotate ignores
axis keys). Digits / `-` / `.` / backspace build a numeric buffer that **OVERRIDES the mouse** — grab
units along the constrained axis (**a typed grab REQUIRES an axis constraint**, SIMPLIFY v1; the readout
prompts "press X or Y" otherwise), scale a factor (uniform, or per-axis when constrained), rotate degrees
— and a typed value is **exact**: snap quantizes ONLY the mouse-driven result (typing IS the exact
affordance, the readout literally says "type = exact"). **LMB or Enter commits** (`CommitTransaction`);
**RMB or Escape cancels** (`CancelTransaction`, reverting the live edit). The pure geometry + axis /
buffer / typed / snap rules live in the world-free `ModalTransform` (the `GizmoTransform`/`CameraNav`
pattern), so they unit-test without a world, a cursor, or a GraphicsDevice.

**The modal owns the pointer AND keyboard while active.** Every active frame it applies the live edit
then **consumes the cursor's pointer edges** (the dialog/menu consume idiom), so selection, the gizmo,
the palette, and camera-nav all stand down — in particular the confirm-LMB never re-picks or clears the
selection, even when it lands over another entity (pre-mortem #4). It reads its keys through its own
`Func<KeyboardState>` seam; the composing screen ORs `Modal.IsActive` into BOTH the host keyboard's
`ShouldSuppressInput` and the `ViewportShortcutContext.ModalActive` shortcut gate, so no game key or
editor chord fires mid-modal (a re-pressed G/S/R cannot re-enter) and **Escape cancels the modal, never
the game (exit) nor a tool (disarm)** — the Escape priority. Mode-switch mid-modal (Blender's G→S) is
deferred.

**The camera rig composes (the UX2-G mapping).** `G` moves the rig via a `TransformEditCommand` on its
own transform; `S` edits its authored `CameraRigComponent.Zoom` via `CameraZoomEditCommand` (a bigger
frustum ⇒ a LOWER zoom: `newZoom = beforeZoom / factor`, clamped to the camera-nav range); `R` is
**refused** for the rig with a status note (rig rotation editing is a future wave). `ModalTransformSystem`
is woven `editor.modal` with the input-owner block, immediately AFTER `editor.shortcuts` (which enters it)
and BEFORE the tools (`editor.gizmo`) + the draw pipeline's `editor.selection`, so its consume reaches
them; `RunNormally`, self-guarded to `RunMode.Edit` (a modal cannot survive into Play). The
`modal:grab|scale|rotate` / `modal:axis x|y` / `modal:digits <text>` / `modal:cursor <dx> <dy>` /
`modal:confirm|cancel` ops drive the full flow headlessly (chords aren't in the replay channel).

**Why:** the design §5 modal transforms — a keyboard-first alternative to the gizmo drag on the same
coalesced-undo machinery. The pointer + keyboard ownership is the same modal-capture the dialog/menu use;
without it a confirm-click also re-picks (pre-mortem #4) and a mid-modal keystroke leaks to the game or a
tool (most dangerously Escape quitting the game). The rig mapping matches the gizmo's Scale→zoom (UX2-G),
so a designer frames the shot with S the same way whether dragging or in a modal.
**Breaks:** a modal that doesn't consume the pointer lets its confirm-click re-pick / clear the selection
(pre-mortem #4); no `ShouldSuppressInput`/`ModalActive` OR lets Escape quit the game or a re-pressed G
re-enter mid-session; pushing outside a transaction (or committing on cancel) makes a session many undo
steps or an un-revertable edit; snapping a typed value defeats the exact affordance; editing
`Transform.Scale` on the rig (instead of its `Zoom`) mis-authors the camera; allowing Rotate on the rig
edits a field UX2-G left for later.
**Tests:** `MonoDreams.Tests/LevelEditor/ModalTransformTests.cs` (the pure math: grab/scale/rotate live
results, axis constraints, the typed override incl. the grab-requires-axis rule, snap on the mouse-driven
result vs exact typed, buffer + axis editing) and
`MonoDreams.Tests/LevelEditor/ModalTransformSystemTests.cs`
(`Enter_WithSelection_Activates_WithoutSelection_NoOps`, `Enter_InPlay_IsRefused`,
`Grab_MouseMotion_EditsLive_LmbCommitsOneUndoStep`, `Grab_Rmb_CancelsAndRestoresTheStart`,
`Escape_CancelsTheModal`, `AxisLock_ConstrainsTheGrab`, `TypedValue_AppliesExactly_AlongTheLockedAxis`,
`OpCursor_DrivesTheLiveEdit_Headlessly`, `ConfirmClickOverAnotherEntity_DoesNotRepick_NorClear`
(pre-mortem #4, with the real `SelectionSystem`), `Rig_Grab_MovesTheRigTransform_OneUndoStep`,
`Rig_Scale_EditsZoom_NotTransformScale_OneUndoStep_UndoRestores`, `Rig_Rotate_IsRefused`); the shortcut
entry + the `ModalActive` gate are `MonoDreams.Tests/LevelEditor/EditorShortcutTests.cs`
(`Table_BindsBareGSR_ToTheModalTransforms_UX3F`, `System_DoesNotFire_WhileAModalTransformOwnsInput`).
**Depends on:** this file — "The gizmo applies a quantized (snap-on) or raw (snap-off) transform edit,
honoring Origin" (the `GizmoTransform` helpers + the one-drag-one-undo pattern this reuses), "Bounded undo
with drag-coalescing" (the `BeginTransaction`/`CommitTransaction`/`CancelTransaction` machinery), "Selection
picks MAX final `LayerDepth` …" (the click-ownership the consume protects — pre-mortem #4), "Viewport
presses belong to exactly one tool family" (`EditorToolMode` — the modal is a keyboard-entered peer of the
tools), "The editor's keyboard shortcuts are ONE chord table …" (the table + gate that enter it), "The
editor splits the free VIEW from the authored camera rig …" (the rig's `Zoom` + `CameraZoomEditCommand`
the Scale path drives), "The editor's Save dialog is a modal three-action chooser …" (the cursor-consume
recipe); foundation — `AKeyboardInputHandlingSystem.ShouldSuppressInput` (the keyboard-half seam).

## The window status bar is one thin strip in the ONE inset, formatted by a pure model

The editor's **status bar** (UX3-F, "like Blender and IntelliJ") is a thin (`EditorChromeLayout.StatusBarHeight`
= 22pt) strip flush with the window bottom, BELOW the assets shelf. It joins the **ONE viewport inset**:
`EditorChromeLayout.ViewportInset`'s bottom margin is the assets shelf PLUS the status bar, and the left /
right strips + the shelf all stop above it — there is never a second bottom rect source (pre-mortem #6), so
compositing, mouse mapping, and `OutsideViewport` stay in lockstep at every DPR. `EditorChromeBuilder`
paints the `Bg0` band + a top `Border` rule (so the panels-cover-the-margins test sees it), and
`EditorStatusBarSystem` places the dynamic content each frame on the native `Editor` target (no
`VisibleComponent` — the chrome rule; live in BOTH transport states). **LEFT:** while a modal transform is
active, its live readout (`StatusBarModel.LeftModal` — mode word + ΔX/ΔY / factor / degrees + axis tag +
the typed buffer + the confirm/cancel hint), else (PF-F) a transient **notification** while one is
showing, else the contextual status (`LeftStatus` — the selected
entity's name or "No selection", and the non-infra entity count). **The notification seam (PF-F):** the
pure `EditorNotifications` model (newest-wins, a bounded ~4.5s display clock the status bar ticks),
severity-colored via the `EditorTheme` intent roles (Info / Success / Warning / Danger), ASCII-only like
the rest of the bar — what EVERY user-action refusal / confirmation raises AS WELL AS logging (a save
guard, a prefab capture confirmation, a guardrail hint, the multi-root message); a live modal readout
always wins over it. **RIGHT (PF-B):** `StatusBarModel.Right` —
the ACTIVE viewport tab's id, plus its transport state on the Game tab (`island` on the Scene tab,
`island | Playing` / `island | Paused` on the Game tab — the run state replaces the retired "Scene mode" /
"Game mode" words, since the tab strip now names the active context), with a `Warning` dirty dot drawn as a
MESH (gated on the SAME dirty source the Scenes panel reads — `Transport.SnapshotWasDirty` while the Game
tab is active, else the history). The strings are all pure `StatusBarModel`, unit-testable with no
world/font, and **ASCII only** — the chrome bitmap font (PPMondwest, max glyph U+25CC) has no
`Δ`/`×`/`°`/`·`/`●`/`…`, so the readout uses `dX`/`dY`, `x` for the factor, `deg`, `|` separators, and the
dirty dot is a mesh, never a glyph.

**Why:** the design §5 ask; the ONE-inset rule is the same single-source-of-truth the viewport-inset premise
protects (a second bottom rect desyncs every world pick by the strip height). A pure model keeps the content
testable and the rendering a thin placement pass. ASCII-only avoids rendering missing-glyph boxes for the
Greek/symbol characters the design sketch used.
**Breaks:** a second bottom rect source (not the ONE inset) desyncs mouse mapping by 22pt (pre-mortem #6); a
`Δ`/`●`/`·` in a rendered string draws a blank box (the font lacks them); a dirty dot keyed to the history
while the Game tab is active would flicker on sandbox churn (it must read the snapshot dirty state); a
`VisibleComponent` on the labels/dot pulls them into `MeshPrepSystem`, double-offsetting the absolute-pixel content.
**Tests:** `MonoDreams.Tests/LevelEditor/StatusBarModelTests.cs` (the pure formatting — the modal readout for
grab/scale/rotate incl. axis + buffer + the press-X-or-Y prompt + the rig "Zoom" word, the contextual status
with count pluralization, `Right_ShowsActiveTabId_AndRunStateOnTheGameTab` — the Scene tab shows just the id,
the Game tab the id + Playing/Paused) and `MonoDreams.Tests/LevelEditor/EditorStatusBarSystemTests.cs`
(the Scene tab shows the id + the dirty dot mesh only when dirty, `Right_ReflectsGameTab_ShowsRunState` — the
Game tab shows `id | Paused`, the left flip from status to the modal readout, "No selection"); PF-F
notify seam: `MonoDreams.Tests/LevelEditor/EditorNotificationsTests.cs` (newest-wins, the bounded display
clock, current-read). The strip joining the ONE inset (the panels-cover-the-margins + DPR-2 doubling
+ the `StatusBar` rect) is `MonoDreams.Tests/LevelEditor/EditorShellTests.cs`
(`ChromeLayout_PanelsCoverExactlyTheInsetMargins`, `ChromeLayout_AtDpr2_DoublesEveryPointMetric`,
`ChromeBuilder_PanelsAreOpaqueAndCoverTheMargins`) + `MonoDreams.Tests/LevelEditor/EditorShellStateTests.cs`
(`ViewportInset_UsesTheRuntimeRegionSizes`, `LeftRightAndBottom_UseTheRuntimeSizes`).
**Depends on:** this file — "The editor shell insets the game viewport and renders its chrome at native
resolution" (the ONE inset + the Editor target + the chrome rules this strip joins), "The editor shell's
region sizes, tabs, and drag ownership live in one shell-state component" (the inset derivation), "The modal
transform (G/S/R) owns the pointer + keyboard …" (the readout source), "Every level-editor color and depth is
an `EditorTheme` role" (the `Bg0` band / `Border` rule / `Warning` dot / `Text1` labels); rendering — the mesh
`DrawComponent` draw path (the dirty dot mesh).

## Viewport overlays are session settings; the grid IS the snap grid, bounded, and hidden while the Game tab is active

The editor's viewport overlays — the world reference grid, the selection outline, and the camera-rig
frustum glyph — are toggled by a per-session `ViewportOverlaySettingsComponent` on a standalone
editor-state entity (`EditorInfrastructureComponent`-tagged, so it survives a transport Restart):
`ShowGrid` (default **off** — preserves the current look), `OutlineSelected` (default **on**),
`ShowCameraGlyph` (default **on**). It is deliberately **not** registered on the component-serializer
registry — **session-scoped v1**; per-project persistence (writing these into the project manifest) is
named terrain. The Scene-header **Overlays** dropdown (`EditorToolbarAction.Overlays`) and the ops
`overlay:grid|outline|camera on|off` + `overlay:spacing <n>` drive the SAME settings through the pure
`ViewportOverlayOps` (op channel + menu-path channel, one application each).

**One grid quantum — spacing IS the snap step.** There is **no separate grid-spacing field**: the grid
spacing is `GizmoStateComponent.GridStep` (the gizmo snap step). `EditorGrid` reads it, and BOTH the
`overlay:spacing` op AND the menu's 8/16/32/64 presets WRITE it — so the displayed grid is always the
grid things snap to, in both directions (change spacing via the menu → the gizmo snaps at the new step;
change the snap step elsewhere → the grid follows). A second copy would let the overlay lie about where
things snap.

**The grid is bounded and gated.** `EditorGrid` bakes world-space lines at `GridStep` across the visible
world AABB through the SAME overlay path as the gizmo/glyph (screen-baked on the native `Editor` target,
viewport-clipped by `OverlayMeshClip`, identity `WorldMatrix`, **no** `VisibleComponent`), at the LOWEST
overlay depth (`EditorTheme.Depths.Grid` = 0.01, beneath `ProxyOverlay`), every 5th line the stronger
`GridMajor` role (else `GridMinor` — subtle warm darks, `Bg2..Border` on the ramp). The line count is
**bounded** by the pure `GridGeometry` (pre-mortem #5): above ~200 minor lines/axis it degrades to
major-only, above ~200 major lines/axis it draws nothing — a zoomed-out view over a small spacing can
never allocate an unbounded mesh. The grid, the selection outline (`OutlineSelected` off → the outline
emit is suppressed; the selection itself is unaffected), and the camera glyph (`ShowCameraGlyph` off →
hidden; the view/rig divergence rule then applies only while on) are all **Edit-only**, and **all three
are hidden while the Game tab is active (`ActiveContextKind == Game`)** — the sandbox looks like the game (Blender hides overlays in camera
view). The gizmo/glyph gates are injected `Func<bool>` seams from the overlay (the systems stay
game-agnostic); the grid emits from the draw-phase `EditorOverlayPrepSystem`, beneath the others.

**Why:** the design §3 — Blender's per-viewport Overlays, adapted. The one-quantum rule is the load-bearing
invariant: an overlay that draws a grid at a spacing DIFFERENT from the snap step misleads the designer
about where a snapped drag lands. The bound keeps a pathological zoom from freezing the editor. The
Game-mode hide keeps the sandbox faithful to the shipped view.
**Breaks:** a second spacing value drifts from the snap step (the grid lies); an unbounded grid mesh at a
zoomed-out view freezes/OOMs the editor; a `VisibleComponent` on the grid mesh pulls it into
`MeshPrepSystem`, which overwrites the identity `WorldMatrix` its screen-baked vertices require; a grid
(or outline, or glyph) left visible while the Game tab is active breaks "the sandbox looks like the game"; a raw
`Color`/XNA token in `EditorGrid` trips the palette lint (it takes the `GridMinor`/`GridMajor` roles).
**Tests:** `MonoDreams.Tests/LevelEditor/GridGeometryTests.cs` (`Plan_EmitsLinesAtSpacing_OverTheVisibleRange`,
`Plan_MajorLines_AreEveryFifth_AnchoredToTheOrigin`, `Plan_DegradesToMajorOnly_ThenToNothing_AsTheRangeGrows`,
`Plan_LineCount_IsAlwaysBounded_RegardlessOfZoom`); `MonoDreams.Tests/LevelEditor/ViewportOverlayTests.cs`
(`Settings_Defaults_GridOff_OutlineOn_CameraOn`; the `overlay:*` + menu-path ops; `Op_Spacing_WritesTheSharedGridStep_NoSecondCopy`
+ `Spacing_IsTheSnapStep_OneValue_BothDirections`; `Grid_BakesAnEditorTargetMesh_NoVisibleComponent_WhenOnAndInEdit`,
`Grid_FollowsTheSharedGridStep_ChangingItViaTheOpReSpacesTheGrid`, `Grid_ClipsToTheGameViewport_UnderAnInset_DevicePixelDestination`,
`Grid_HiddenInPlay_AndWhenTheGateIsFalse`, `Grid_PathologicalZoomOut_DegradesToNothing_NoUnboundedMesh`,
`Grid_ModerateZoomOut_StaysBounded_MajorOnly`; `OutlineSelectedOff_SuppressesOnlyTheOutline_HandleStays_SelectionUnaffected`,
`GameMode_HidesAllGizmoOverlays_TheSandboxLooksLikeTheGame`, `CameraGlyphGate_Off_HidesTheFrustum_EvenWhenTheViewDiffersFromTheRig`).
**Depends on:** this file — "Editor context menus are a data-driven popup …" (the Toggle menu that drives
these settings), "The gizmo applies a quantized (snap-on) or raw (snap-off) transform edit" (the
`GridStep` this shares as the ONE grid quantum), "The editor splits the free VIEW from the authored camera
rig" (the glyph this gates), "Selection picks MAX final `LayerDepth` …" (the gizmo whose outline this
gates), "The editor shell insets the game viewport …" (the `OverlayProjection` + `OverlayMeshClip` +
`Editor` target + the depth stack), "Every level-editor color and depth is an `EditorTheme` role" (the
`GridMinor`/`GridMajor` roles + `Depths.Grid` + the lint), "The viewport context stack …"
(the `ActiveContextKind == Game` hide); rendering — `LineMeshGenerator` + the mesh `DrawComponent` draw path.

## The palette hold-drag multi-stamps at arc-length spacing, coalesced into one undo step

While a palette sprite item is armed in `Place` mode, a **hold-drag** stamps the prop repeatedly:
the press stamps one and opens a coalescing transaction (`EditorHistory.BeginTransaction`), holding
+ dragging stamps more — one per `GizmoStateComponent.StampSpacing` of **arc-length** travelled,
sampled by the pure `Brush/StrokeSampler` (which carries the leftover fractional distance between
frames, so spacing is exact regardless of frame rate or cursor speed) — and the release commits the
whole stroke as **exactly one** `CommitTransaction` history entry (one undo removes every stamp of
the drag). A **single click** (press then release with no drag) is the degenerate case: one stamp,
one undo step. This is the plain embryo of the future scatter brush — no jitter, no seed; each stamp
is an ordinary `CreateEntityCommand` (auto-tagged `SceneObjectComponent`, sub-graph snapshot). A
non-positive `StampSpacing` disables multi-stamp (a click still places one). Stamps that
snap-collapse onto the previous position (snap on + spacing < grid) are skipped so identical props
never stack in one cell; the last stamp is auto-selected on release. Any interruption of an open
stroke (disarm, Escape/right-click, entering Play, dispose) **commits** it — the placed stamps are
real edits, and an abandoned open transaction on the shared history would break the next
`BeginTransaction`. Triggers (island §5.3) stay single-click, not multi-stamped.

**Why:** dressing large areas (grass tufts, stones) one click at a time is the bottleneck the
scatter brush will eventually solve; arc-length spacing is the minimal, jitter-free first step, and
coalescing keeps one drag = one undo step (the gizmo's drag-coalescing contract, reused).
**Breaks:** un-coalesced stamps make one drag N undo steps; frame-rate-dependent spacing clumps
stamps where the cursor moved slowly; an abandoned open transaction throws on the next drag; stacking
identical props in one grid cell when snap collapses the spacing.
**Tests:** `MonoDreams.Tests/LevelEditor/StrokeSamplerTests.cs` (even spacing, diagonal, leftover
carry-over, disable/short-segment no-op) and `MonoDreams.Tests/LevelEditor/PalettePlacementTests.cs`
(`MultiStampTest_HoldDragStampsAtSpacingAndCoalescesToOneUndoStep` — a scripted hold-drag stamps at
the expected spacing, nothing is committed mid-drag, the release is one undo step that one undo
reverses whole; `PlacementTest_SingleClickIsOneUndoStepAutoSelectAndRepeat` — a single click still
places one, one undo step, auto-selected).
**Depends on:** this file — "Bounded undo with drag-coalescing" (the transaction pattern) and
"Viewport presses belong to exactly one tool family" (multi-stamp acts only in `Place`).

## `file:` AssetKeys load drop-folder art at runtime and graduate to content keys at ship

A placed prop's `SpriteInfoComponent.AssetKey` may use the `file:` scheme
(`"file:Island/props/tree01.png"`, optional `#region` suffix for a sliced-sheet entry): the
texture is loaded at runtime — `Texture2D.FromStream` over `TitleContainer.OpenStream`, lazy and
memoized per PNG (`FileAssetTextureLoader`) — from the gitignored asset drop folder
(`Content/Island/`, copied raw to the output content dir; its committed `MANIFEST.md` names the
packs). The catalog scan (`AssetCatalog.Scan`) reads **only** the directory listing + the
`*.slices.json` sidecars, never a PNG (`TitleContainer` cannot enumerate, so the scan is
host-filesystem — desktop-editor-first). A **missing file at load is a loud `Logger.Warning` plus
the shared visible magenta placeholder texture, never an invisible entity**. The region suffix
identifies the palette entry only — loading always opens the base PNG and the region's `Source`
rect is serialized on the sprite itself, so scenes survive sidecar changes. When art finalizes,
assets graduate into MGCB content and `file:` keys flip to content keys — a mechanical, greppable
migration; `file:` is the fast-iteration authoring loop, content keys are the shipping (and
web-ready) form.

**Why:** the island phase is "experiment with placeholder packs" — an MGCB round-trip per art
experiment kills the loop, and itch licenses forbid committing the packs (so every checkout may
be missing files, which must fail visibly, not silently).
**Breaks:** silent-missing assets produce invisible entities that look like data loss; eager
texture loads at scan turn startup O(catalog); shipping `file:` keys to web breaks (no directory
scan there) — the graduation step is the exit.
**Tests:** `MonoDreams.Tests/LevelEditor/AssetCatalogTests.cs` (scan/sidecars/lazy/missing),
`SceneRoundTripTests.cs` (`FileAssetKeyRoundTripTest`, `MissingFileAssetOnReloadTest`).
**Depends on:** this file — "`SpriteInfoComponent` serializes an `AssetKey`, never the live
`Texture2D`" (this premise extends the key's grammar); level-blender — the `TitleContainer`
content-stream premise (the same portable read seam).

## Y-sorted props use the feet-origin convention, factory-applied

`SpritePropFactory` (the generic palette placement factory) builds the standard renderable stack
from a catalog entry + a **screen-supplied** `PaletteBand`; on a Y-sorted band it sets
`SpriteInfoComponent.Origin` to the sprite's **bottom-center in source pixels** and
`YSortOffset = 0`. The entity's `Position` is therefore where the prop *stands*: the sprite
renders with its feet at the transform position, and `YSortSystem` (which keys on
`WorldPosition.Y + YSortOffset`) sorts by that same feet line — the player walks behind a tree
when above it with zero per-prop tuning. Non-Y-sorted (ground) bands keep the default top-left
origin; their within-band order is authored (bring-forward/send-back, Slice 2). The band→depth
mapping itself is supplied by the screen from ITS `DrawLayerMap` — the module never references a
game's layer enum.

**Placement centres the visual, not the origin, under the cursor.** The factory places the prop at
the `Position` it is *given*; the palette computes that position so the sprite's **visual centre**
(source `(W/2, H/2)`) lands under the cursor — `PalettePlacementSystem.SpritePlacementPosition`
subtracts the source-space centre↔origin delta (rotated by the armed ghost rotation; placement scale
is 1) from the cursor world point. It is the ONE function the ghost preview and every committed stamp
share, so they can never disagree about "under the cursor". `Origin` is untouched (feet on a Y-sorted
band, top-left otherwise) — only the position shifts, so the feet-origin / Y-sort behaviour above is
preserved. **Grid-snap still quantizes the transform position** (the feet/origin point — the SAME
field it quantized before centring), so with snap on the feet land on grid lines, not the
free-floating centre. Triggers place at the raw (snapped) cursor — no sprite centre to offset.

**Why:** top-down walk-behind depends on the sort key and the visual base being the same point;
without the convention every prop needs a hand-tuned `YSortOffset` and a mis-set one reads as the
player clipping through the prop. Centring on the visual (not the feet/origin) is the user-reported
fix — a prop placed with its feet under the cursor reads as landing above where you clicked.
**Breaks:** props sort by their top-left corner — the player pops in front of a building while
visually behind it; ghost preview and placed prop disagree about where "under the cursor" is (the
shared position function prevents this); placing the origin rather than the centre under the cursor
reads as off-centre; snapping the centre rather than the transform position would drift the feet off
the grid the round-trip and Y-sort expect.
**Tests:** `MonoDreams.Tests/LevelEditor/SpritePropFactoryTests.cs`
(`FeetOriginOnYSortedBandTest`, `SpritePropStandardStackTest`, `SlicedEntrySourceRectTest`);
`MonoDreams.Tests/LevelEditor/PalettePlacementTests.cs`
(`GhostLifecycleTest_FollowsCursorSnapsParksAndDespawns` — the ghost follows the cursor centred and
snap quantizes the centred transform position; `PlacementTest_SingleClickIsOneUndoStepAutoSelectAndRepeat`
— a stamp lands centred; `MultiStampTest_HoldDragStampsAtSpacingAndCoalescesToOneUndoStep` — arc-length
spacing preserved, every stamp centred).
**Depends on:** rendering — `YSortSystem`'s `WorldPosition.Y + YSortOffset` key; this file — "The
serializer persists SOURCE sort fields…" (Origin/YSortOffset are SOURCE fields that round-trip).

## A placed asset's band is its permanent per-asset mark if set, else the global selector

The palette's layer band is normally the **global** band selector (the header row — you pick
Ground/Detail/Props/Overhead before placing). FW3 adds a **permanent per-asset mark**: a catalog
entry can be marked to ALWAYS place on a specific band regardless of the global selector (e.g. a
ground tile is always Ground). The **resolution rule** (`PalettePlacementSystem.ResolveBand`, used
by both the ghost preview and every stamp/placement): the placement band = the asset's **marked
band if set, else `SelectedBand`** (the global selector). A mark that names a band this screen does
not offer is ignored (falls back to the global selector) so a stale config can never point at a
non-existent band. Marks are set on a card's **band-chip badge** (`CycleAssetBand` — unmarked → each
band → unmarked) or headlessly (`SetAssetBand`, the `asset-band:<entryId>:<band>` op; `auto`/`none`
clears). They persist in an **`asset-bands.json`** (`AssetBandConfig`) written next to the assets at
the catalog's scan root through the **canonical byte-stable** JSON policy (`CanonicalJson`) — so the
mark **survives an editor restart** (a fresh scan + fresh config load still resolves it). This is
**dev-authoring metadata** (it lives with the gitignored placeholder packs, is desktop-editor-only,
uses `System.IO` directly like the catalog's own directory scan) and it **never touches a scene
file**: a placed entity still serializes the actual band it landed on (unchanged) — the mark only
changes the DEFAULT band used when arming/placing. A directly-constructed (rootless) config keeps
marks in memory with a loud no-op `Save`, so a screen with no drop folder (or a unit test) still
resolves marks for the session.

**Why:** the user's report "there is no way to permanently mark an asset as ground or not" — a global
band selector alone means re-picking the band for every asset, and forgetting to leaves a ground tile
Y-sorting as a prop. A per-asset default that persists removes the friction; keeping it out of the
scene file preserves the "a scene serializes the actual band on the placed entity" round-trip.
**Breaks:** without the mark, every placement needs the global selector set correctly first (easy to
forget → wrong band); baking the mark into the scene would conflate authoring defaults with placed
truth; a non-canonical writer would churn the config diff; a mark naming a removed band would place
onto nothing.
**Tests:** `MonoDreams.Tests/LevelEditor/PalettePlacementTests.cs`
(`BandResolution_MarkedAssetUsesItsBand_UnmarkedUsesGlobalSelector`,
`SetAssetBand_SetsClearsAndIsLoudOnUnknown`,
`CycleAssetBand_WalksUnmarkedThroughEveryBandBackToUnmarked`,
`MarkedBand_SurvivesCatalogRescanAndEditorRestart`,
`HeadlessPaletteOpTest_AssetBandStringReachesNamedDispatch`);
`MonoDreams.Tests/LevelEditor/AssetBandConfigTests.cs` (`SetBand_PersistsAndSurvivesReload`,
`ClearBand_RemovesTheMark_Persisting`, `Config_RoundTripsCanonicalBytes`,
`MalformedConfig_FallsBackToEmpty_NoThrow`, `InMemoryConfig_KeepsMarks_ButCannotPersist`).
**Depends on:** this file — "Y-sorted props use the feet-origin convention, factory-applied" (the band
carries the Y-sort/feet behavior the mark selects); "Scene serialization is canonical and
byte-stable" (`CanonicalJson`, reused to write the config).

## The palette lists assets as cards (icon on top, label on the bottom) in a scrollable grid

The palette's bottom strip renders each catalog entry as a fixed-size **card** — a lazily-loaded art
**thumbnail** filling the top icon box, the **text label** on the bottom row (truncated with an
ellipsis to the card width so it never bleeds into the neighbour; the label-only fallback when the
texture is missing/magenta), and a small **band-chip badge** in the icon's top-right corner showing
the per-asset band mark (a band initial when marked, `-` when unmarked; click cycles it — see the
resolution premise above). Cards flow left-to-right into a fixed-width grid inside
`EditorChromeLayout.BottomBar` and scroll by **whole card rows** on the mouse wheel (scrolled-out rows
are parked off-screen — no clipping). The band-selector header row stays at the top of the strip. The
pure grid math (card rects, icon/label/chip sub-rects, column wrap, whole-row scroll, DPR scaling)
lives in `PaletteLayout` so it is unit-testable without a GraphicsDevice. `EditorChromeLayout.BottomBarHeight`
was raised (104 → 168 logical points) to give the cards real screen real estate — the game viewport
inset shrinks by the same amount, automatically, because the shell + mouse mapping both derive from
`ViewportInset`. Hit-testing is card-body-then-chip: a click on the chip cycles the band (never
arms), a click anywhere else on the card arms placement. **UX-B:** the shelf height is now the
runtime `EditorShellStateComponent.BottomHeightPt` (the bottom splitter resizes it) and the palette
renders in the shelf **body below its "Assets" tab strip** (`EditorChromeLayout.RegionBody`); when
the cards overflow, the palette draws the same slim `Border`/`BorderStrong` scrollbar as the right
strip (draggable, sharing the shell drag token — the shell-state premise), and it stands down while
any foreign splitter/scrollbar drag owns the pointer. **PF-D:** the shelf's tab strip is now
interactive (`Assets | Prefabs`, OWNED by this system — the retired static tab left
`EditorChromeBuilder`); the **Prefabs** tab lists `.mdprefab` cards (a prefab glyph + id) and reuses the
SAME `Place`-mode arm/ghost/click machinery (mutually exclusive with an armed asset/trigger; the prefab
ghost v1 is none — click-on-viewport). Each tab's chrome parks when the other is active. See "The prefab
UX … (PF-D)". **PF-F — the palette is now UNIVERSAL:** when a screen supplies NO catalog/bands of its own
but the project context is resolved, the OVERLAY builds them itself — a default catalog scanned from the
`Content/Island` drop-folder convention (`EditorOverlay.DefaultAssetDropFolder`) + one band per layer of
the SCREEN's own `DrawLayerMap` (`EditorOverlay.DefaultBandsFromLayers`) — so a menu / demo gets the
Assets + Prefabs tabs too, not just the game screen (the user's "the editor should be oblivious to what
kind of scene is loaded"). An unresolved project keeps the no-palette behavior; a screen that supplies
its own catalog/bands overrides it; the screen still weaves `overlay.Palette` into its pipeline.

**Why:** the user's report that the palette assets "should be bigger, take a little more height … and
be actual cards with the icon/preview on top and text on the bottom" — flat text rows are hard to
scan and give the art no room. Keeping the layout math pure keeps it testable and keeps chrome
hit-tests aligned at any DPR.
**Breaks:** an inset that grows only compositing (not mouse mapping) desyncs every world pick by the
extra bottom margin; a card grid computed in the system (not `PaletteLayout`) can't be unit-tested;
an un-truncated label bleeds across cards; a chip hit-test after the card body would arm instead of
marking.
**Tests:** `MonoDreams.Tests/LevelEditor/PaletteLayoutTests.cs` (`CardGridWrapsAtContentWidth`,
`CardGridAlwaysFitsAtLeastOneColumn`, `ScrollClampsToWholeCardRows`, `CardRectVisibleAndScrolledOut`,
`CardSubRects_IconTopLabelBottomChipCorner`, `RaisedStripFitsHeaderPlusACardRow`,
`CardMetrics_AtDpr2_Double`); `MonoDreams.Tests/LevelEditor/EditorShellTests.cs`
(`ChromeLayout_DefaultScale_IsThePreDprLayout`, `ChromeLayout_AtDpr2_DoublesEveryPointMetric` — the
raised inset at DPR 1 and 2).
**Depends on:** this file — "The editor shell insets the game viewport and renders its chrome at
native resolution" (the `ViewportInset` the strip height feeds, and the device-pixel space the cards
render in); rendering — the `SimpleButtonComponent`/`DynamicTextComponent` chrome primitives.

## Save is blocked while Playing, while the Game tab is active, or when no project root is resolved

`EditorOverlay.DispatchToolbarAction`'s Save case no-ops with a loud `Logger.Warning` in THREE
distinguishable cases (`EditorOverlay.SaveBlock(state, projectContext, activeKind)` →
`SaveBlockReason.Playing` / `SaveBlockReason.GameMode` / `SaveBlockReason.NoProjectRoot`, checked in
that precedence): (1) while `RunMode == Play` — saving
is an authoring act over the **paused** scene, and a mid-Play save would bake transient run state (a
mid-air player, in-flight velocities, half-resolved collisions) into the scene file as if it were
authored truth; (2) while the **Game tab is active** (PF-B, `ActiveContextKind == Game`) — sandbox edits
are expressly not-to-be-saved (they discard on leave), so Save reflects the real Scene tab, not the
sandbox (see "The viewport context stack …"); (3) while the editor's `EditorProjectContext` is unresolved
(no `game.mdproj` found — a shipped/relocated build, a console, or an unset `MONODREAMS_PROJECT_ROOT`
with nothing to walk up to) — there is nowhere versioned to write. The toolbar renders the Save button
dimmed for ANY cause (the transport rule dims all editing buttons while Playing; a small per-button
gate additionally dims Save while Paused-but-blocked — `NoProjectRoot` OR `GameMode`), and this guard
closes the remaining dispatch paths (the
headless `ToolbarAction` op, any programmatic dispatch). The two reasons are reported separately so
the log/toolbar can tell the user WHY. Undo/Redo keep their existing Paused-only toolbar gating;
the transport buttons stay live in both states (there is no Load button — a scene is opened via the
Scenes panel, UX-C/UX-D). (PS3 repoints the actual write from the ephemeral build-output path to the
resolved source tree — see "The editor Save writes versioned `.mdscene` into the project source tree" —
so the `NoProjectRoot` gate is now also what keeps the write target valid.) When Save is **not** blocked
it no longer writes immediately — it OPENS the three-action Save dialog, and each write runs in the
dialog's action callback: **Save Scene** and **Save Project** both route through `SaveCurrentScene`
(Save Project is v1 single-scene — it saves the ONE in-memory scene through the same path and never
blanket-writes the on-disk set), and **Save Backup As…** routes through `SaveBackupAs`. **All three
re-apply this exact guard** (Playing / Game-tab / no-project-root) as defense-in-depth, and the
empty-save guard below covers a backup too (see "The editor's Save dialog is a modal three-action
chooser …").

**The empty-save guard (UX-C §3.5, pre-mortem #4).** Beyond the two `SaveBlock` causes above,
`SaveCurrentSceneTo` applies one more world-state guard the pure `SaveBlock` cannot express: it
**refuses** (loud `Logger.Warning`, distinguishable reason) when the world has **zero
`SceneObjectComponent` roots AND no scene was loaded into this world this session** — the pure
predicate `EditorOverlay.EmptySaveRefused(sceneRootCount, sceneWasLoaded)`. There is genuinely
*nothing to save*, so a mis-bound code-built screen (a menu that never placed anything) can never
blank a real level with an empty file — **regardless of whether the target file already exists** (file
existence is deliberately NOT a factor). The escape hatch is the reader's `SceneReaderSystem.SceneWasLoaded`
one-way session flag (set true on the first successful `LoadSceneRequest`): a designer who deliberately
**emptied a loaded scene** may still save it empty. On a successful write `SaveCurrentSceneTo` also calls
`EditorHistory.MarkSavePoint()` (see "The editor history tracks a dirty save-point signal").
**Save Backup As… (UX-D) obeys the SAME guards** — the two `SaveBlock` causes and the empty-save guard —
but on success it deliberately does **not** `MarkSavePoint` (the working scene is still dirty vs disk)
and does not append the MGCB copy line (a backup is dangling); it then reloads the bound scene via
Restart.

**Prefab reconciliation (PF-D).** The empty-save guard is a SCENE guard — it does **not** apply to a
Save Prefab. An empty prefab (its one root, nothing else — e.g. a just-created Create-Empty-Prefab being
assembled) is **legal**: `EditorOverlay.SavePrefabCurrent` skips `EmptySaveRefused` entirely, and the
only gate is `PrefabWriter.BuildPrefab`'s **one-root validation** (a zero-root world has no root to
normalize and is refused; a multi-root world is refused loud). So "nothing to save" is a scene concept
(a mis-bound screen blanking a level); a prefab always has its root, and assembling from an empty root is
the intended flow. See "The prefab UX … (PF-D)".

**Why:** the authoring-vs-runtime trap (island-authoring plan §9, pulled into Slice 1 because it
is nearly free) AND the project-persistence trap (project-persistence plan §4): a mid-Play save
corrupts a scene, and a save with no resolved project root has nowhere versioned to land — both
fail silently in a way that only shows later; and the empty-save trap (UX-C §3.5): now that a bound
screen's overlay carries an explicit scene id, a Save on a screen where nothing was placed OR loaded
would overwrite that scene id's real file with an empty one.
**Breaks:** a scene reloads with the player embedded mid-jump or props mid-physics — "the level I
saved is not the level I authored"; or Save silently writes to (or crashes over) an ephemeral
build-output path instead of the versioned source tree; or a mis-bound screen's blank Save wipes a
committed level (the empty-save footgun).
**Tests:** `MonoDreams.Tests/LevelEditor/ToolbarTests.cs`
(`SaveGuardTest_BlockedWhilePlayingOrWithoutAProjectRoot`,
`SaveGuardTest_DispatchNoOpsWhileBlockedAndSavesWhenAllowed`);
`MonoDreams.Tests/LevelEditor/EditorGameModeTests.cs`
(`SaveBlock_GameMode_IsDistinguishable_PlayingWins_SceneModeSavesAgain`,
`ToolbarSaveButton_IsInertInGameMode_ViaTheSharedGuard` — the third `GameMode` cause + precedence);
`MonoDreams.Tests/LevelEditor/EmptySaveGuardTests.cs` (`EmptySaveRefused_TruthTable` — the
(rootCount, wasLoaded) truth table incl. the never-loaded-but-file-exists case;
`SceneReader_SceneWasLoaded_StartsFalse_FlipsTrueAfterALoad`);
`MonoDreams.Tests/LevelEditor/SceneSourceWriteTests.cs`
(`SaveProject_WritesTheCurrentSceneThroughTheSamePath_MarksTheSavePoint_SingleSceneV1` — Save Project is
single-scene v1, writes one file, never blanket-writes;
`SaveBackupAs_WritesDanglingFile_NoSavePoint_NoBundle_ThenRestartReloadsBoundScene` — backup obeys the
guards but marks nothing and bundles nothing).
**Depends on:** this file — "The editor run flag composes the always-on editor and the transport
owns RunMode" (Playing = `RunMode.Play` with the shell composed); "The project manifest anchors the
editor's project root; unresolved is fail-safe" (the `NoProjectRoot` cause).

## Game screens declare their bound scene; the Scenes panel lists screens + scene files and switching IS selecting

A game screen declares which configuration file it loads from at **registration** — foundation's
`ScreenInfo(DisplayName, BoundSceneId, HostsSceneFiles)`, code being the source of truth. The editor's
**Scenes panel** (the **Scenes tab** of the left strip — renamed from "Project" in UX2-B) renders the pure `SceneCatalog.Build`, which merges: every registered
screen with a `BoundSceneId` → one entry (label = `DisplayName`, in registration order); every
`.mdscene` under the resolved project's levels dir **not claimed by a binding** → one entry hosted by the
first `HostsSceneFiles` screen (label = the scene id — dangling backups appear here by design); and an
**unresolved project degrades to screens only** (matching the Save guard's fail-safe). The pure catalog
never reads the filesystem — the overlay injects the scene-id list (its existing desktop directory IO,
`ListSceneIds` over `LevelsPath`). Each overlay is handed its scene id **explicitly** (the Game host sets
it in `Load` from the requested level via `SetSceneId`; a bound screen from its declared id at
construction), killing the pre-UX-C hazard where every overlay fell back to `manifest.startScene` and all
three screens would Save to the same `island.mdscene`.

A bound screen also publishes an **optional scene load** in `Load` (`NativeLevelLoader.TryPublishSceneLoad`
— source-first when the project is resolved and the source file exists, else the bundled `TitleContainer`
probe, else a silent no-op) so its saved scene comes up UNDER its code-built UI on boot. Code-spawned UI
stays **untagged / never serialized**: the existing `SceneObjectComponent` membership policy is now the
ownership policy — screen UI is code-owned, only loaded/placed/editor-created content is scene-owned.

**There is no Load action — switching IS selecting.** Clicking a Scenes-panel row (or the `scenes:select
<key>` op) routes through the ONE initiator `EditorOverlay.SelectScene`. When the **Game tab is active**
(PF-B) it `ExitToSceneMode` **first** (a full restore per "The viewport context stack …"), so the dirty
gate below then runs on the RESTORED real scene, never on sandbox churn — one
gate flavor, no bypass. The decision is the pure
`SceneCatalog.DecideSwitch(entry, isDirty)`: the current entry → no-op; a **clean** world → the
host-supplied `SwitchScene(entry)` callback fires immediately; a **dirty** world → a modal confirm-on-switch
(the `EditorDialogSystem` `ConfirmSwitch` mode — a new mode on the same modal machinery, parked chrome +
cursor consume + same `editor.dialog` weave — with **[Save & Switch] [Discard & Switch (`Danger`)]
[Cancel]**), whose Save & Switch runs the SAME guarded `SaveCurrentScene` then switches, Discard switches
without saving, and Cancel stays. The dirty gate lives in this one initiator (pre-mortem #7), so the panel
click and `scenes:select` are gated identically. `SwitchScene` is the game-agnostic seam (like
`EditorTransport.Reload`): Examples wires it to the existing hand-off (`EditorSceneSwitch.Switch` — set
`RequestedLevelComponent` for the level host only, then `ScreenController.LoadScreen(entry.ScreenName)`),
Demos would wire plain `LoadScreen`. The world tears down wholesale and the shared `GameState.RunMode =
Edit` survives (foundation), so the new screen composes a fresh overlay bound to the right scene id. The
editor module gains **no** dependency on a game screen type.

**Create Empty Scene (UX2-D).** A right-click in the Scenes panel offers **Create Empty Scene…**, which
opens a small modal on the SAME dialog machinery (name field prefilled `untitled`, `Sanitize`d,
Create/Cancel). Confirm **refuses an existing name loudly and keeps the dialog open** (the injected
name-collision predicate), then writes a **minimal canonical `.mdscene`** — empty `entities[]` + the
current camera/layers the writer emits for an empty world, built through `SceneWriter`/`CanonicalJson`
(never hand-written JSON) — into `LevelsPath`, applies the SAME zero-touch `EnsureLevelBundled` treatment
a Save gets, and then **switches to it through this same `SelectScene` flow** (the catalog re-scan
surfaces the new file immediately, and a dirty working scene runs the confirm-on-switch gate first). It
is blocked when no project root is resolved (nowhere versioned to write) — the Save-guard fail-safe.

**Why:** the user's rule — "we create game screens in code and need a clear way to indicate which
configuration files they load from" — plus the removal of the Load action: a screen declares its scene,
the panel lists them, and selecting one loads it. Explicit per-screen scene ids are the fix for the
all-screens-save-to-one-file hazard; the dirty gate in the single initiator is what stops a switch from
silently discarding unsaved edits.
**Breaks:** a screen whose overlay has no explicit scene id Saves to `manifest.startScene` (three screens
clobbering one file); a switch path that skips the dirty gate silently loses edits (pre-mortem #7); the
editor referencing a game screen type couples the module to a game; reading the filesystem in the pure
catalog makes it un-unit-testable; tagging screen UI as scene-owned would serialize the menu's buttons.
**Tests:** `MonoDreams.Tests/LevelEditor/SceneCatalogTests.cs` (merging + registration order, claiming,
dangling backups, unresolved → screens only, no-host → no files, all-plain → empty, current detection,
and the `DecideSwitch` truth table); `MonoDreams.Tests/LevelEditor/OptionalSceneLoadTests.cs` (source-first
/ bundled / absent no-op / unresolved-skips-source); `MonoDreams.Tests/LevelEditor/EditorPanelModelTests.cs`
(`ScenesTab_ShowsProjectInfo_AndTheScenesList`, `ScenesTab_CurrentEntry_ShowsDirtyMarker_WhenDirty`,
`ScenesTab_NoCatalog_ShowsNoScenes`); `MonoDreams.Tests/LevelEditor/EditorPanelTests.cs`
(`ScenesTab_SceneCatalogRowClick_ForwardsTheEntryToTheSelectCallback`);
`MonoDreams.Tests/LevelEditor/EditorDialogTests.cs` (`ConfirmSwitch_Confirm_RunsSaveAndSwitch_NotDiscard_AndCloses`,
`ConfirmSwitch_Discard_SwitchesWithoutSaving_AndCloses`, `ConfirmSwitch_Cancel_DoesNeither_AndCloses`,
`ConfirmSwitch_EnterConfirms_EscapeCancels`, `ConfirmSwitch_ClickDiscardThroughRealCursorPipeline_DiscardsOnRelease`);
`MonoDreams.Tests/LevelEditor/EditorContextMenuTests.cs` (Create Empty Scene — the dialog's collision
refusal + accept + empty-name, and the canonical empty-world write on a fake FS — UX2-D);
`MonoDreams.Tests/Foundation/ScreenRegistrationTests.cs` (the `ScreenInfo` binding + enumeration).
**Depends on:** foundation — "Screens declare editor-facing `ScreenInfo`; the shared `GameState` (and its
`RunMode`) is the only survivor of a screen switch"; this file — "The editor's panels: a LEFT tabbed
panel (Entities/Systems/Scenes), a dedicated RIGHT Inspector, …" (the Scenes tab this renders in), "The editor history tracks a
dirty save-point signal" (the dirty input to the gate), "Save is blocked while Playing or when no project
root is resolved" (the guarded `SaveCurrentScene` that Save & Switch reuses), "The game boots native
scenes native-first via `LoadLevelRequest`" (`NativeLevelLoader`, whose optional-load helper this uses);
level-loading — "`LevelLoadRequestSystem` resolves `LoadLevelRequest` native-only (fails loud otherwise)".

## The editor history tracks a dirty save-point signal

`EditorHistory` carries a monotonic `EditVersion` (bumped on every recorded push — including a
transaction commit — plus every undo, redo, and `Clear`) and a `MarkSavePoint()`; `IsDirty` is
`EditVersion != savePointVersion`. A fresh history is clean (both start at 0). `EditorOverlay.SaveCurrentScene`
marks the save point on a successful write; the transport's `Restart` `Clear()` advances `EditVersion`
(keeping it monotonic) **then re-marks clean** — a reload from disk has no unsaved edits. Leaving the Game
tab (PF-B) `Clear()`s and then, if the pre-entry state was dirty, calls `MarkDirty()` — which advances
`EditVersion` past the save point WITHOUT recording an undo entry — so the RESTORED Scene dirtiness
is reproduced while undo/redo stay empty (undo after leaving is a no-op — see "The viewport context stack …").
Mid-transaction
pushes accumulate without bumping `EditVersion` (the world is live-edited but not yet recorded), so an
aborted drag (`CancelTransaction`) leaves the dirtiness unchanged and a committed drag is exactly one
dirty step. **Known conservative edge:** because `EditVersion` only advances, undoing back to the exact
save-point world still reads dirty — the dirty gate errs toward prompting rather than silently discarding.

**Why:** the Scenes-panel switch gate (and the dirty marker) needs a reliable "there are unsaved edits"
signal, and every world mutation in Edit already flows through the history — so the history is the natural
home for the bit. Monotonic-plus-save-point is the minimal correct model; the conservative undo edge is
acceptable (a spurious confirm dialog is safe; a missed one loses work).
**Breaks:** a save point that rewinds `EditVersion` on undo could read clean while edits remain unsaved
(the switch would silently discard them); marking dirty mid-transaction would make an aborted drag read
dirty; not re-marking clean after `Clear` would make a freshly-reloaded scene read dirty and prompt on the
very next switch.
**Tests:** `MonoDreams.Tests/LevelEditor/DirtyTrackingTests.cs` (`FreshHistory_IsClean`,
`Push_MakesDirty_SavePoint_MakesClean_NextEditDirtyAgain`,
`UndoRedo_AdvanceEditVersion_SoBackToSavePointStillReadsDirty`,
`Transaction_CommitIsOneDirtyStep_MidTransactionIsNotYetDirty_CancelStaysClean`,
`Clear_ResetsClean_ButEditVersionIsMonotonic`);
`MonoDreams.Tests/LevelEditor/EditorGameModeTests.cs::Exit_RestoresTheCapturedDirtyState_NotSandboxChurn`
(the `MarkDirty()` restore of a captured dirty state after `Clear`, undo/redo empty).
**Depends on:** this file — "Bounded undo with drag-coalescing" (the history whose mutations this counts),
"The transport's Restart rebuilds the scene from the original load request …" (the `Clear` that resets
clean), "Game screens declare their bound scene …" (the switch gate that reads `IsDirty`).

## The editor's Save dialog is a modal three-action chooser (Save Scene / Save Project / Save Backup As…) that owns input while open

The toolbar's Save button (and `dialog:save-open`) opens a modal **three-action chooser**
(`EditorDialogSystem`, weave entry `editor.dialog`) rather than acting immediately — replacing the
retired file-system navigator (UX-D §4). The file-picking rationale is gone: **there is no Load
action and no filename typing to pick an existing scene** — a scene is opened by selecting it in the
**Scenes panel** (see "Game screens declare their bound scene …"), so the dialog only writes. Three
stacked full-width actions (a title + a `Text1` subtitle each, the Claude-Code-style "clear actions"
list), laid out by the pure `EditorDialogLayout`:

1. **Save Scene** (primary, `Accent` outline + `Accent` title) — subtitle `<sceneId>.mdscene` — the
   existing guarded `EditorOverlay.SaveCurrentScene` (source-tree write + zero-touch bundling +
   ship-lint warning + `MarkSavePoint`). Enter picks it.
2. **Save Project** — subtitle "every unsaved scene + project files (currently: `<sceneId>`)" —
   `EditorOverlay.SaveProject`, which v1 saves the current scene (the only one in memory) through the
   SAME guarded path; it must **never** blanket-write scenes not in memory (it is the terrain for
   multi-scene sessions).
3. **Save Backup As…** — clicking it **arms** a name field (prefilled `<sceneId>-backup`, `Sanitize`d)
   + a Confirm; confirm runs `EditorOverlay.SaveBackupAs`, which writes `<name>.mdscene` into
   `LevelsPath` **without** rebinding the scene id, **without** `MarkSavePoint`, and **without**
   bundling (a backup is dangling by design — logged "not bundled"), then **reloads the bound scene
   from disk** via the transport's `Restart` (teardown + screen-recorded reload + history clear ⇒
   clean). Its subtitle carries the `Warning`-colored "then reloads `<sceneId>` from disk (discards
   unsaved edits)".

Escape/Cancel closes. Enter = Save Scene, or confirm-backup while the name field is armed. Each action
fires a callback the overlay supplies (running the SAME shared `SceneWriter` / `History` / `Transport`
instances), so the dialog stays **game-agnostic** — it knows nothing of `SceneWriter` / project paths.

The dialog is built the way the systems panel is — native-resolution chrome on `RenderTargetID.Editor`,
`SimpleButtonComponent` + `DynamicTextComponent`, `ScreenPosition` hit-test, **no `VisibleComponent`**
(shown/hidden by parking off-screen) — deliberately NOT the `ui` `DialogComponent`/`DialogSystem` (which
toggle `VisibleComponent` and trap focus via `UIFocusSystem` — both Main/HUD mechanisms the
Editor-target chrome must not use). While the dialog is open it **owns input**, in two halves: (1) mouse
— after hit-testing its own controls (on the release edge) it clears the cursor's pointer edges AND the
button level fields on the single cursor entity, so no mouse-driven editor system (toolbar, selection,
gizmo, camera-nav, palette, boundary, systems-panel) downstream that frame acts. The dialog itself
acting on the release edge survives its own consume ONLY because `CursorInputSystem` derives the edges
from a previous-state it owns rather than from the (now-cleared) level fields — see cursor's "Button
press/release edges derive from CursorInputSystem's own previous-state"; without that the dialog's clear
of `LeftButton` would make its own release edge unobservable the next frame (the confirmed "dialog
clicks do nothing" bug). (2) keyboard — the composing screen wires the host keyboard system's
`ShouldSuppressInput` to `Dialog.IsOpen`, so every editor/game keyboard action (delete, undo/redo,
frame, boundary-commit, and the game's Escape-to-exit) stands down while the dialog reads the keyboard
for the backup name field (Backspace edits, Enter confirms, Escape cancels; typing is ignored until the
backup field is armed). Every action also has a public method so the headless
`dialog:save-open|scene|project|name <text>|backup <name>|confirm|discard|cancel` op grammar drives the
whole flow with no real keyboard/mouse (`dialog:confirm` = the focused/default action; `dialog:backup
<name>` is a one-shot arm+set+confirm). **The `ConfirmSwitch` mode (UX-C) stays live on this same
machinery** (opened by `OpenConfirmSwitch`; `dialog:confirm` = Save &amp; Switch, `dialog:discard` =
Discard &amp; Switch, `dialog:cancel`; Enter = Confirm, Escape = Cancel): a plain "Unsaved changes in
&lt;scene&gt;" confirm with no field, reusing the parked chrome + cursor consume + `editor.dialog` weave,
so the switch-confirm modality can never leak a click to the tools behind it (pre-mortem #3). After the
navigator's removal the system has these live modes: `Save`, `ConfirmSwitch`, `CreateScene`, and the
**PF-D** additions — `SavePrefab` (a single primary [Save Prefab] + Cancel — the prefab-context Save,
`dialog:prefab`, routing to `EditorOverlay.SavePrefabCurrent`), `CreatePrefab` (the name modal shared by
Create-Prefab-from-Selection + Create-Empty-Prefab, collision-refused), and `ConfirmDelete` (the
destructive prefab-card Delete confirm). `ConfirmSwitch` gained a **verb** so `OpenConfirmClose` renders
"Save & Close / Discard & Close" for a dirty prefab tab's × (pre-mortem #9); the three name-ish modals
share the keyboard + two-button mouse, differing only in layout + the mode-routed `Confirm`.

**Why:** the project model re-founding (UX-C/UX-D) makes a file picker redundant — screens declare their
scene, the Scenes panel lists them, and selecting one loads it — so Save need only *write*, and a
short "clear actions" chooser is a better fit than a directory browser (the Claude-Code visual identity).
A modal must capture input or a stray viewport click/keystroke leaks to the tools behind it — most
dangerously typing a backup name with `z`/`y` (undo/redo) or hitting Escape (quit the game). Reusing the
`ui` dialog machinery would force `VisibleComponent` onto Editor chrome and break the chrome-render
invariant, so the dialog is editor-native but still built from the shared UI primitives (no parallel draw
components — the no-duplicate-ways tenet).
**Breaks:** a Save Project that blanket-wrote the on-disk set (not the in-memory scene) would clobber
scenes the designer never touched; a backup that marked the save point (or bundled, or rebound the scene
id) would masquerade as the working scene's save; without the mouse edge-consumption a click meant for a
dialog button also selects/places behind it; without the keyboard suppression, typing a backup name fires
editor hotkeys (undo/redo/delete) and Escape quits the game mid-edit; using `ui.DialogSystem` would add
`VisibleComponent` to Editor chrome and double-offset the pre-baked meshes.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorDialogTests.cs`
(`EditorTextField_AppendBackspaceSetClear`, `EditorTextField_Sanitize`,
`SaveDialog_Opens_WithThreeActions_BackupDisarmed_AndPrefilledBackupName`,
`SaveDialog_SaveScene_InvokesTheSaveSceneCallback_AndCloses`,
`SaveDialog_SaveProject_InvokesTheSaveProjectCallback_AndCloses`,
`SaveDialog_Backup_ArmRevealsField_ThenConfirmPassesSanitizedName_AndCloses`,
`SaveDialog_Backup_EmptyNameAfterSanitize_KeepsDialogOpen_AndDoesNotWrite`,
`SaveDialog_BackupOneShot_ArmsSetsAndConfirms_InOneCall`, `SaveDialog_Cancel_InvokesNothing_AndCloses`,
`SaveDialog_Escape_Closes`, `SaveDialog_Enter_PicksSaveScene_WhenBackupDisarmed`,
`SaveDialog_Enter_ConfirmsBackup_WhenBackupArmed`, `SaveDialog_BackupField_KeyboardTypingBackspace`,
`SaveDialog_TypingIsIgnored_WhenBackupDisarmed`, `OpenDialog_ConsumesTheCursor_SoAViewportClickDoesNotSelect`,
`SaveDialog_ClickSaveSceneThroughRealCursorPipeline_InvokesOnRelease` — the EF1 press→release regression
through the REAL `CursorInputSystem → editor.dialog` order — plus the `ConfirmSwitch_*` mode tests) and
`MonoDreams.Tests/LevelEditor/SceneSourceWriteTests.cs`
(`SaveProject_WritesTheCurrentSceneThroughTheSamePath_MarksTheSavePoint_SingleSceneV1`,
`SaveBackupAs_WritesDanglingFile_NoSavePoint_NoBundle_ThenRestartReloadsBoundScene` — the backup
semantics: dangling file written, scene id unchanged, save point not marked, no MGCB copy line, then
Restart reloads the bound scene from disk).
**Depends on:** this file — "Save is blocked while Playing or when no project root is resolved" (the
actions re-apply the guard; backup obeys it too); "The editor Save writes versioned `.mdscene` into the
project source tree" (the write target + `Content/Levels` bundling home); "The transport's Restart
rebuilds the scene from the original load request …" (the backup's reload); "Game screens declare their
bound scene …" (the Scenes panel that replaced the Load affordance); cursor — "Button press/release edges
derive from CursorInputSystem's own previous-state, immune to consumers clearing the level fields" (why the
dialog's release-edge action survives its own pointer-edge consume); foundation —
`AKeyboardInputHandlingSystem.ShouldSuppressInput` (the keyboard-half seam); rendering — "Editor-target
chrome carries no `VisibleComponent`" (the chrome rule).

## The project manifest anchors the editor's project root; unresolved is fail-safe

A MonoDreams game is a versionable unit rooted at a `game.mdproj` manifest (`GameProject`:
`formatVersion`, `startScene`, `levelsDir` default `Levels`, `assetRoots`), read/written through the
SAME `CanonicalJson` policy scenes use so it is byte-stable and diffable. The editor resolves its
project root once at init (desktop-only) via `EditorProjectContext.Resolve`, in this order (corrected
in FW1):

1. **PRIMARY — env var `MONODREAMS_PROJECT_ROOT`** (probing `<root>/Content/game.mdproj` then
   `<root>/game.mdproj`). This is the explicit override — trusted, NOT bin/obj-filtered. For the
   reference game its value is the absolute path to the content project, e.g.
   `.../MonoDreams.Examples.Core`.
2. **FALLBACK — walk up from `BaseDirectory`, rejecting build-output copies.** At each ancestor probe
   `<dir>/Content/game.mdproj` then `<dir>/game.mdproj`, but **reject any candidate whose path contains
   a `bin`/`obj` segment** — that is the MGCB-copied OUTPUT manifest beside the executable
   (`bin/Debug/net8.0/Content/game.mdproj`), never the versioned source. (FW1 fix: the walk-up used to
   match that output copy first, so Save landed in `bin/…/Content/Levels`.)
3. **FALLBACK — repo-root search for the SOURCE manifest.** The source manifest usually lives in a
   **sibling** project (`.../<Game>.Core/Content/game.mdproj`), off the build-output ancestor chain, so
   walk-up alone can't reach it. While ascending, detect the repository/solution root (an ancestor
   holding a `.git` entry — **file OR directory**, so git worktrees work — or a `*.sln`), then
   recursively search under it for `game.mdproj`, **excluding every `bin`/`obj` path**. When several
   source manifests exist (e.g. a web head's `wwwroot/Content` copy) the choice is deterministic:
   shallowest path first, then ordinal — so a normal `dotnet run`/Rider run from inside the repo
   resolves the SOURCE with **no env var**.
4. **UNRESOLVED — only an output copy (or nothing) found.** `Resolved = false`; Save is disabled with
   an actionable reason (never a silent write to `bin`).

**The resolved `ProjectRoot` is the directory that CONTAINS the manifest** (so
`ProjectRoot/game.mdproj == ManifestPath` and `ProjectRoot/LevelsDir == LevelsPath`), uniform across
every resolution path. Resolution NEVER throws — a missing or malformed manifest logs a warning and
yields `Resolved = false`. The head (where the editor flag is parsed) resolves it and hands it to the
overlay (an optional ctor param, default null) so the `level-editor` module stays game-agnostic; the
module reads the environment/filesystem only through injected delegates. The pure `Resolve` overload
takes the env/file/dir/enumerate probes as delegates (fully unit-testable with a simulated layout); the
no-arg convenience wires the env/file probes to `PlatformServices.Current` and the two directory probes
(`.git`/`*.sln` detection + recursive manifest search) to `System.IO` directly — the module's only
direct filesystem access, justified because project resolution is a desktop-only, editor-init host
concern that never runs on web. The example manifest ships committed at
`MonoDreams.Examples.Core/Content/game.mdproj` (under `Content/` + an MGCB `/copy:` so the shipped game
can read it via `TitleContainer` later, like `blender_level.json`).

**Rider / IDE setup:** an in-repo `dotnet run`/Rider run now resolves the SOURCE tree with **no
configuration** (step 3). To target a specific content project (or when running from a relocated
output), set `MONODREAMS_PROJECT_ROOT` in the run configuration to the content project directory —
for the reference game the absolute path to `MonoDreams.Examples.Core` (which contains
`Content/game.mdproj`); Save then lands in `MonoDreams.Examples.Core/Content/Levels/<id>.mdscene`.

**Why:** the editor runs from a build-output directory but must locate the versioned project source to
save into it (PS3) and to gate Save when there is no project (PS2); a resolution that threw, picked the
build-output copy, or picked the wrong root would crash the editor or silently write to the wrong place
(the confirmed "Save lands in `bin/…/Content/Levels`" bug — the walk-up resolved the output manifest).
**Breaks:** Save writes to (or crashes over) an ephemeral build-output path (the FW1 bug); the game
cannot resolve its `startScene` at boot (PS4); a locale/insertion-order-dependent manifest churns the
git diff.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorProjectContextTests.cs` (env / walk-up / unresolved /
malformed / env-miss-falls-back; **FW1**: `WalkUp_FromBinBaseDir_ResolvesTheSourceManifest_NeverTheBinOutputCopy`
— a bin base dir + a source + an output + a web copy + a `.git` file resolves the SOURCE, never the bin
copy; `EnvVar_Wins_EvenWhenABinCopyAndSourceExist`; `OnlyABinOutputCopy_AndNoSource_IsUnresolved_NeverTheBinCopy`),
`MonoDreams.Tests/LevelEditor/GameProjectTests.cs` (canonical round-trip, byte-stable, `assetRoots`
order preserved, canonical shape locked).
**Depends on:** this file — "Scene serialization is canonical and byte-stable…" (the shared
`CanonicalJson`); foundation — `IPlatformServices` (`BaseDirectory`, env/file lookups the resolver
routes through).

## The editor Save writes versioned `.mdscene` into the project source tree

The editor's Save writes the scene to `EditorProjectContext.LevelsPath/<sceneId>.mdscene` — i.e.
`ProjectRoot/LevelsDir/<sceneId>.mdscene`, in the **versioned SOURCE tree** — through
`IPlatformServices.WriteAllText` (a desktop file write git sees immediately), creating the levels
directory if it is missing (PS3). The path is derived by the pure `EditorOverlay.SceneFilePath(ctx,
sceneId)`; the `sceneId` is `EditorOverlay.ResolveSceneId(explicit, ctx)` — an explicit id wins,
else the manifest's `GameProject.StartScene`, else `EditorOverlay.DefaultSceneId` (`"untitled"`) — so
Save writes a named `<id>.mdscene`, not a fixed `editor_scene.json`. The in-editor **Load** reads that
SAME source path back directly (`LoadSceneRequest(path, fromContent: false)` → `ReadAllText`) for an
instant reload of what was just written — no build round-trip. This **retires the pre-PS3
`ExportScene`→`BaseDirectory` write**: the editor no longer writes into `bin/…`, and
`IPlatformServices.ExportScene` is reserved for the deferred web browser-download (web has no source
tree; on web the project context is null so Save is disabled). Two guards keep the write safe: the
overlay's save-guard (`SaveBlock` → `NoProjectRoot`, which blocks the dispatch and dims the button
when the project is unresolved) and, as defense-in-depth, `SceneWriter.Save` itself refusing a
null/empty path (loud, no write). The shipped game still reads bundled `.mdscene` read-only via
`TitleContainer` (console-portable — PS4); only the desktop editor writes.

**Why:** the "there is no save mechanism" gap — Save must land the file where a designer versions it
(the source `Content/Levels/`), not in the ephemeral, gitignored, clobbered-on-rebuild build output;
and the PS1 byte-stable fixed point (`load → edit → save` == source bytes) must survive the repoint so
git diffs stay meaningful at the real location.
**Breaks:** Save writes to `bin/…` (lost on the next rebuild, invisible to git) — the designer's work
silently evaporates; or Save writes to nowhere / crashes when the project is unresolved; or a fixed
`editor_scene.json` name makes every level overwrite the same file.
**Tests:** `MonoDreams.Tests/LevelEditor/SceneSourceWriteTests.cs`
(`Save_WritesIntoTheProjectSourceTree_NotBaseDirectory`, `Save_Refused_WhenProjectUnresolved`,
`SceneId_DefaultsFromManifestStartScene_ElseUntitled`, `Load_ReadsTheJustWrittenSourceFile_RoundTrips`,
`SaveReloadSave_AtTheSourcePath_IsAByteStableFixedPoint`).
**Depends on:** this file — "The project manifest anchors the editor's project root; unresolved is
fail-safe" (`LevelsPath` + `StartScene`); "Scene serialization is canonical and byte-stable…" (the
bytes written); "Save is blocked while Playing or when no project root is resolved" (the upstream
gate); foundation — `IPlatformServices` (`WriteAllText` / `CreateDirectory` / `ReadAllText`).

## The game boots native scenes native-first via `LoadLevelRequest`

A saved `.mdscene` is loadable as the game's **real level** (PS4): `LoadLevelRequest(id)` resolves
**native-first**. `NativeLevelLoader.CreateProbe` builds the `Func<string,bool>` handed to
`LevelLoadRequestSystem`; per request it resolves **source-first when an editor `EditorProjectContext`
is resolved** (UX-D, pre-mortem #5): a resolved context + an existing source
`<LevelsPath>/<id>.mdscene` publishes `LoadSceneRequest(sourcePath, fromContent:false)`; otherwise it
probes the bundled `Content/Levels/<id>.mdscene` via `TitleContainer` and publishes
`LoadSceneRequest(rel, fromContent:true)`. **A null context (a shipped / console / web build) skips the
source branch entirely — byte-identical to the pre-UX-D bundled path.** On a hit (returning `true`) the
**same** `SceneReaderSystem` that serves the editor's Save-then-reload also serves the game boot — the
reader is thus generalized off the editor-only `LoadSceneRequest`. Source-first is what makes a
Restart-after-Save honest: the source tree is authoritative the moment the editor Saves, while the
bundled copy is stale until the next build; the probe shares its source-first resolution with the
bound-screen optional load (`NativeLevelLoader.TryPublishSourceFirst`). It runs in **both run modes** and
**with no editor composed**: `LoadLevelExampleGameScreen` reuses the overlay's reader when the editor is
present, else builds a standalone one (engine **and game** serializers — PS5), so a shipped game boots
native scenes too. When no `.mdscene` exists for the id the boot dispatcher **fails loud** — the
LDtk/Blender loaders are import-only (PS5) and not wired to boot, so there is no silent legacy attempt.
The bundled
`game.mdproj` (read at boot via
`ManifestBoot.TryReadManifest` over `TitleContainer`) drives the entry: `ManifestBoot.ResolveStartScene`
returns the manifest's `startScene` **only** when a native scene exists for it, else `null` so the host
keeps its default boot (a not-yet-migrated `startScene` — the Examples `island` placeholder — stays
back-compat until PS5 lands its `.mdscene`).

**Why:** native `.mdscene` is the game's real level format; the shipped game must boot it read-only on
every platform (`TitleContainer`, console-portable), and the load entry must be unified so a native load
is not clobbered by the LDtk remove-on-miss — this is what closes the CORE_TENETS §6 parser-asymmetry
(fully in PS5). The manifest-boot guard (native-exists) keeps a placeholder `startScene` from breaking
the default boot before its level is committed.
**Breaks:** if the native reader were only composed behind the editor, a shipped game could never boot a
`.mdscene`; if `ResolveStartScene` returned `startScene` unconditionally, a manifest naming a
not-yet-committed level would send the game into a failing LDtk load instead of its menu.
**Tests:** `MonoDreams.Tests/LevelEditor/NativeFirstLoadTests.cs`
(`NativeFirst_LoadsScene_ViaTheNativeReader_WithNoEditorComposed`,
`NoNativeScene_ProbeReturnsFalse_AndPublishesNothing`, `CommittedSampleScene_MatchesTheCanonicalShape`,
`CommittedSampleScene_LoadsBackViaTheNativeReader`,
`Probe_WithResolvedContext_ResolvesSourceFirst_PublishingTheSourcePath`,
`Probe_WithNullContext_SkipsSourceFirst_AndUsesTheBundledPathUnchanged`,
`StaleBundleRegression_ResolvedContextLoadsSource_UnresolvedLoadsBundled` — the UX-D source-first probe +
the pre-mortem #5 regression), `MonoDreams.Tests/LevelEditor/ManifestBootTests.cs`,
`MonoDreams.Tests/IntegrationTests/NativeSceneBootTests.cs` (the real headless game boots the committed
sample).
**Depends on:** level-loading — "`LevelLoadRequestSystem` resolves `LoadLevelRequest` native-only (fails
loud otherwise)"; "Native `.mdscene` levels are bundled by an MGCB `/copy:` entry and read via
`TitleContainer`"; this file — "Scene round-trip reconstructs from registered components, not factories";
"The project manifest anchors the editor's project root; unresolved is fail-safe".

## Within-band ordering nudges SOURCE sort fields and never breaks the band

The Bring forward / Send back actions (toolbar buttons `Fwd`/`Back`, headless
`order:forward`/`order:back`, optional PageUp/PageDown via `DefaultEditorKeys`) adjust the
selection's overlap order INSIDE its layer band by `EditorCommandSystem.OrderStep`, resolved and
clamped against the band the screen's `DrawLayerMap` reports (`TryGetBandRange` — a containment
lookup that, unlike the exact-match `TryGetYSortRange`, also answers for already-nudged depths).
On a **plain band** the nudge moves SOURCE `SpriteInfoComponent.LayerDepth`, clamped at the
band's inset edges so a nudge can never cross into the neighboring band. On a **Y-sorted band**
the nudge moves `SpriteInfoComponent.YSortDepthBias` (clamped to ± the band width) and **never**
`LayerDepth`: Y-sort participation is an exact-match lookup on the registered band value
(`TryGetYSortRange` in `YSortSystem`), so a nudged depth would silently drop the sprite out of
Y-sorting — the class of bug where a prop stops walking-behind after one harmless-looking click.
Each click is one `SpriteSortEditCommand` (data + apply/revert) = one undo step; a click already
at the edge pushes nothing (no empty undo entries). Because only SOURCE fields move — never the
per-frame-derived `DrawComponent.LayerDepth` — the ordering survives save/load through the
existing serializer unchanged.

**Why:** overlapping ground patches (grass over sand, dirt over grass — plan §4.2) need
authorable order, and the two sort regimes have different safe knobs; picking the wrong one is
silent until the next Y-sort or reload.
**Breaks:** nudging `LayerDepth` on a Y-sorted band drops the sprite out of Y-sorting entirely
(walks-behind stops working); an unclamped nudge crosses band boundaries (a ground patch drifting
above the props band); writing the derived depth instead of the source bakes one frame's sort.
**Tests:** `MonoDreams.Tests/LevelEditor/OrderingTests.cs`
(`BringForward_NudgesSourceLayerDepth_OneUndoStepPerClick`, `Ordering_ClampsAtBandEdges`,
`Ordering_OnYSortedBand_AdjustsBiasNeverLayerDepth`, `Ordering_TargetsTheOwner_WhenAProxyIsSelected`,
`TryGetBandRange_ResolvesNudgedDepths_AndRejectsOutOfBand`) and
`SceneRoundTripTests.OrderingPersistsThroughSaveLoadTest`.
**Depends on:** rendering — "Layer-depth ownership pipeline" (the SOURCE→derived flow this rides)
and `YSortSystem`'s exact-match band lookup; this file — "The serializer persists SOURCE sort
fields, never the per-frame-derived `DrawComponent.LayerDepth`", "Bounded undo with drag-coalescing".

## Prop footprints default to full width × bottom quarter, feet-anchored

The **Add box collider** action creates the top-down footprint default (plan §5.1), computed
purely by `ColliderDefaults.FootprintBounds` from the sprite's RENDERED geometry: the local quad
relative to `Position` spans `(-Origin·s) .. (-Origin·s + Size)` (`s = Size/Source`, the
source→render scale; `Bounds` is Transform-relative and never transform-scaled), and the
footprint keeps its full width and bottom 25% — under the feet-origin convention
(`Origin = (srcW/2, srcH)`) exactly `(-w/2, -h/4) .. (w/2, 0)`: the box hangs off the feet point,
which IS the entity's `Position`, so the character collides with the base of a tree/building and
walks behind its canopy. **Add polygon collider** starts from a hexagon inscribed in that same
footprint; **Remove collider** (and Delete on a whole-shape proxy) removes through
`ColliderComponentCommand`, whose construction-time snapshot restores the removed component
field-for-field on undo (`Bounds`/`ModelVertices`, `ActiveLayers`, `Passive`, `Enabled`,
`IgnoreTransformRotation`, with derived world data refreshed against the live transform). A
sprite-less entity gets `ColliderDefaults.FallbackFootprint`. An editor-added footprint is
**`Passive = true`** (`ColliderDefaults.FootprintPassive`) — a static world blocker in the
`WallEntityFactory` idiom: `Passive = true` means "does not initiate a collision", so the footprint
is never moved by resolution and a static prop/building **blocks the player without drifting**. (A
`Passive = false` footprint initiates and is displaced by resolution — the building would slide away
when walked into. Blocker-vs-trigger is the game's `EntityInfoComponent` classification, not the
`Passive` flag — footprints and trigger zones are both passive; see the trigger-zone premise.)

**Why:** the reference games' convention — only the base blocks movement; a full-sprite default
box would block the player on a tree's canopy, and an un-snapshotted remove would make undo
resurrect a default collider instead of the designer's tuned one. Passive keeps static props static
(the same static-blocker idiom the coastline bake and trigger zones use).
**Breaks:** a full-height default footprint blocks walk-behind everywhere; footprint math ignoring
`Origin`/render scale plants the box off the visible base; a remove that loses `Passive`/layers on
undo silently turns a trigger zone into a wall; **a `Passive = false` footprint drifts — the prop
slides away from the player instead of blocking**.
**Tests:** `MonoDreams.Tests/LevelEditor/ColliderActionTests.cs`
(`FootprintBounds_FeetOrigin_FullWidthBottomQuarter_FeetAnchored`,
`FootprintHexagon_InscribedInTheFootprint_AndConvex`, `AddBoxCollider_AppliesFootprintDefault_Undoable`
— asserts the footprint is `Passive`, `AddConvexCollider_AppliesFootprintHexagon_Undoable` — same,
`AddBoxFootprint_IsPassiveStaticBlocker_BlocksActiveBodyWithoutDrifting` — the real collision +
physics pipeline: an active body is blocked and the footprint owner does not move,
`RemoveCollider_Both_OneUndoEntry_RestoresFieldForField`,
`RemoveCollider_ViaProxy_RemovesOnlyThatKind_ReselectsOwner`).
**Depends on:** this file — "Y-sorted props use the feet-origin convention, factory-applied" (the
Origin the math inverts); collision — `BoxColliderComponent.Bounds` is Transform-relative.

## Convex colliders are vertex-edited through (kind, index) proxies; invalid shapes are rejected loudly

Per-vertex editing rides the generalized proxy family: `ProxySyncSystem` keys its proxies
`(ProxyBindingKind, index)` and materializes one `ConvexVertex` proxy per `ModelVertices` entry
**while the convex family's own proxy (shape or vertex) is selected** — one click deeper than
entity selection, so selecting a prop shows clean collider outlines and clicking the convex
outline opens the vertex session (the Godot/Unity collision-shape convention; it also keeps the
pre-Slice-2 "N proxies per selected entity" expectations intact). Dragging a vertex proxy writes
back exactly ONE model vertex (inverse-transformed world delta) through `ColliderEditCommand` —
one drag = one undo step, world data refreshed. **Convexity strategy: reject, loudly.** A drag
frame whose result fails `ProxyGeometry.IsConvex` (all non-zero consecutive-edge cross products
share a sign; collinear points allowed; zero-area loops rejected) is NOT applied — the vertex
sticks at its last valid position and a warning logs once per drag. Auto-hulling was rejected
because it can reorder or drop vertices mid-drag, invalidating the very `(kind, index)` binding
being dragged and dangling the family. **Add vertex** inserts an edge midpoint (after the
selected vertex proxy, else into the longest edge) — collinear by construction, hence always
legal, and given shape by the next drag. **Delete on a vertex proxy deletes that vertex** —
routed through `EditorCommandSystem`'s delete intent, never disposing the transient proxy entity
— guarded so a convex collider keeps ≥ 3 vertices (a loud refusal at 3; removing a vertex from a
convex polygon is itself always convex-safe), with the selection handed to the shape proxy so the
session continues.

**Why:** irregular building bases (plan §5.1) need per-vertex footprints, the collision module's
SAT is convex-only (an invalid shape silently mis-collides), and this is the exact machinery the
Slice-3 boundary tool and the Wave-F spline control points reuse — the binding indices must stay
stable under editing.
**Breaks:** applying a non-convex drag frame hands SAT a shape it cannot resolve (silent tunnel /
phantom contacts); auto-hull mid-drag despawns or retargets the dragged handle; a 2-vertex
"polygon" after an unguarded delete throws in the collider's own constructor paths; disposing the
proxy entity on Delete makes Delete a non-undoable visual no-op.
**Tests:** `MonoDreams.Tests/LevelEditor/ProxyVertexTests.cs`
(`VertexProxies_MaterializeWhenTheConvexFamilyProxyIsSelected`, `VertexCountChange_ResizesTheFamilyLive`,
`VertexDrag_WritesTheRightModelVertex_OneUndoStep`, `VertexDrag_RejectsNonConvexResult_KeepsLastValidShape`,
`IsConvex_AcceptsConvexAndCollinear_RejectsConcaveAndDegenerate`,
`VertexHandle_WinsThePick_WhereItRidesTheShapeBorder`) and
`MonoDreams.Tests/LevelEditor/ColliderActionTests.cs`
(`Delete_OnVertexProxy_DeletesTheVertex_WithMinThreeGuard`,
`Delete_OnShapeProxy_RemovesTheCollider_NotTheProxyEntity`,
`AddVertex_InsertsMidpoint_AfterSelectedVertex_OrIntoLongestEdge`).
**Depends on:** this file — "Collider shapes are edited through standalone gizmo proxies…" (the
family + write-back rules this extends); collision — the convex-only SAT contract and
"`BroadPhaseAABB` must be refreshed when vertices change".

## A boundary bakes into one convex quad segment per polyline edge; bake products never serialize

A freeform world boundary (island-authoring §5.2 — coastline / cliff) is a `BoundaryComponent
{ Points[] (local to the entity's Position), Thickness }` authoring entity: **pure, serialized
scene data** (registered `core.Boundary`, round-trips in `entities[]`), the durable truth. Its
COLLISION is **baked, never per-frame**: `BoundaryBakeSystem` subscribes to the component being
**added** (the boundary tool's commit, a scene load re-setting it) and **changed** (a vertex drag /
add / delete + undo/redo, all through `entity.Set(new BoundaryComponent(...))`), enqueues the
entity, and — only when draining that queue in `Update` — generates **one thin convex quad segment
collider per polyline edge** (N points → N−1 quads, `BoundaryGeometry.EdgeQuads`, each of the
component's `Thickness`, wound to the collision module's positive-shoelace convention). An empty
queue is a no-op, so nothing evaluates a boundary in a normal frame (the §S2 bake-is-message-driven
rule). Segments are **`ChildOf` children** of the boundary (lifecycle + grouping) and carry
`BakedProductComponent` + a `ConvexColliderComponent` that is **`Passive = true`** — static world
geometry (the `WallEntityFactory` idiom): a passive collider never initiates a collision so
resolution never moves it, yet the active player is resolved out of it, so it BLOCKS while staying
put. Root-level collision (`UpdateWorldVertices` uses the entity's LOCAL `Position`) is honoured by
copying the boundary's world position onto each segment child and keeping the quad in the local
frame. **Bake products NEVER scene-serialize**: `SceneWriter` excludes any `BakedProductComponent`
entity from the membership closure **even inside the boundary root's `ChildOf` descendant set` — the
polyline is the durable truth, the segments regenerate on load (bake-on-load runs in both run modes,
`RunNormally`). Boundaries are edited via per-vertex proxies (`ProxyBindingKind.BoundaryVertex`, one
per point) that materialise on PLAIN selection of the boundary — a boundary IS its points, so
`SelectionSystem` also border-picks the boundary's open polyline to select it in the first place —
plus a single **thickness handle** (`ProxyBindingKind.BoundaryThickness`, Slice 4) riding the band
edge (first-edge midpoint + normal × Thickness/2): dragging it along the normal changes `Thickness`
by 2× the perpendicular move (the band spans ±Thickness/2) through one `BoundaryEditCommand`
(one drag = one undo step, floored at `BoundaryComponent.MinThickness`), which re-fires the bake.

**Whole-boundary MOVE re-bakes (Slice 4).** The gizmo moves a boundary by mutating its
`TransformComponent` fields directly, which fires **no** component-changed event — so
`BoundaryBakeSystem.Update` also **polls each boundary's world position every frame** and enqueues a
re-bake when it drifts from the position it was last baked at. Without this, moving a boundary shifts
its outline + proxies (which read `WorldPosition`) but leaves the already-baked, root-level segment
colliders at the old spot, so a moved coastline would stop blocking where it now appears. The poll is
`O(#boundaries)` (few, long-lived) and re-bakes only on an actual move; a static scene (including
Play) costs a position compare and nothing else.

**Why:** a coastline is deeply concave and the engine's SAT is convex-only, so a segment chain is
the standard robust answer; §S2 (bake-never-evaluate) keeps it off the per-frame path; the
"bake products never scene-serialize" invariant keeps the file honest (the durable truth is the
source) and prevents double-counting / stale run state on reload. Passive (not the task's literal
`passive=false`) is the engine's static-blocker idiom — a non-passive segment initiates and is moved
by the resolution (it drifts when the player pushes it), which is a bug for static world geometry.
The move-poll is required because a Transform edit is a field mutation, not an `entity.Set`, so the
changed subscription never sees it.
**Breaks:** evaluating the polyline per frame (the perf rule); serializing the baked children
(double segments on the next load, baked stale state in the file); non-passive segments that drift
when hit; a concave single collider that SAT cannot resolve; forgetting the local→world copy so a
non-origin boundary's colliders land at the wrong place; a moved boundary whose segments stay at the
old position (blocks where the coastline no longer is).
**Tests:** `MonoDreams.Tests/LevelEditor/BoundaryGeometryTests.cs` (edge-quads count / thickness /
winding / degenerate inputs; world-projection; open-polyline border test),
`BoundaryBakeTests.cs` (added/changed → N−1 passive convex `ChildOf` + `BakedProduct` segments;
re-bake disposes the old; bakes in Play; empty queue is a no-op),
`BoundaryToolTests.cs` (lay/commit/cancel lifecycle, one undo step, centroid pivot + local points,
≥2 guard), `BoundaryVertexProxyTests.cs` (per-vertex proxies + the thickness handle on plain
selection; a drag writes one point; delete keeps ≥2; add inserts a midpoint),
`BoundaryThicknessTests.cs` (the thickness handle materialises at the band edge; a drag changes
`Thickness` in one undo step and re-fires the bake; undo reverts),
`BoundaryBakeTests.BoundaryMove_ReBakesSegmentsAtTheNewWorldPosition` (a whole-boundary move
re-bakes the segments at the new world position with correct collider world vertices; no drift = no
spurious re-bake), and
`SceneRoundTripTests.BoundaryBakeChildrenNeverSerialize_RegenerateOnLoadTest` (the boundary root
serializes, no convex child does, children regenerate on load, polyline round-trips).
**Depends on:** collision — SAT is convex-only, and `Passive` = "does not initiate" static geometry
(the `WallEntityFactory` idiom); foundation — `ChildOfComponent` + `HierarchySystem.DisposeOrphans`
(the bake children's lifecycle); this file — "Collider shapes are edited through standalone gizmo
proxies…" (the `(kind, index)` machinery the boundary vertices reuse), "Viewport presses belong to
exactly one tool family" (the `Boundary` mode).

## Trigger zones are Passive colliders identified by an auto-numbered EntityInfo string

A trigger zone (island-authoring §5.3 — evidence spot / talk radius / exit) is a **`Passive`
box collider** whose identity rides **`EntityInfoComponent`** — `Type` = a game-defined category
prefix (`"evidence"`, `"talkzone"`, `"exit"`, screen-supplied as `TriggerType`s) and `Name` = an
auto-numbered scene-unique instance id (`"evidence_01"`, `TriggerFactory.NextName` = one past the
highest existing suffix, so numbering survives deletes). **No new component**: the trigger IS a
passive collider + a serialized identity string, exactly what a game reaction system pattern-matches
on (the in-repo `ZoneDialogueTriggerSystem` precedent — a collision message + a zone identity → a
game reaction). It round-trips through the existing `EntityInfo` + `BoxCollider` serializers
unchanged (the `Passive` flag already serialises); rename is editing the saved JSON (banked decision
3 — no free-text widget). Placement rides the palette's Place mode (a "Triggers section" of the
strip); the placed zone is centred on the click, auto-selected, and one `CreateEntityCommand` undo
step (auto-tagged `SceneObject`). A trigger is **Passive like all static geometry**, so the flag
alone cannot tell a blocker from a sensor — the game's collision classifier decides that by identity
(a known trigger prefix → a non-Physics collision type the physical resolver ignores → fires without
blocking; everything else → Physics → blocks). Because a passive collider has no sprite and would be
invisible in Edit, `TriggerOverlaySystem` draws an Edit-only tinted outline for every trigger (a
Passive box `SceneObject` with no sprite) plus the armed-trigger placement ghost, on the
native-resolution Editor target (the chrome rule: no `VisibleComponent`).

**Why:** the plan's decision to reuse the engine's existing string identity + passive-collider
mechanism instead of inventing a trigger component keeps the editor game-agnostic and matches the
one working precedent; auto-numbering gives unique, stable ids without a text-input widget; the
Edit-only outline is what makes an otherwise-invisible sensor visible and selectable.
**Breaks:** a trigger that blocks the player (classified Physics, or the classifier keyed on the
`Passive` flag — which every static blocker also sets); an invisible zone a designer can neither see
nor select; a duplicate identity string two game systems can't disambiguate; a new component that
the game's reaction system would have to learn instead of reading the string it already reads.
**Tests:** `MonoDreams.Tests/LevelEditor/TriggerPlacementTests.cs` (`TriggerFactory` makes a passive
centred box with the prefix identity; `NextName` auto-numbers uniquely per prefix; the trigger
round-trips through the serializers; a moving player entering the zone emits a `CollisionMessage`
carrying the trigger's `EntityInfoComponent` identity) and the milestone test.
**Depends on:** collision — `Passive` = "does not initiate" (a passive target still resolves an
active body); foundation — `EntityInfoComponent` (string identity, serialized); this file — "The
component-serializer registry is opt-in per type" (the trigger uses only pre-registered serializers).

## The walkable island milestone: build → save → reload → play → restart

The island-authoring phase's acceptance milestone (open decision 6): a scene dressed with ≥2
buildings (footprint colliders), ground + road patches, a coastline boundary, and ≥1 evidence + ≥1
talk-zone trigger **saves**, **reloads** (via `LoadSceneRequest`), is **walkable in Play** (the
coastline and a building footprint BLOCK the player; entering a trigger fires a collision carrying
the right `EntityInfoComponent` identity), and **survives Restart** (a re-load rebuilds the whole
scene, the coastline segments re-baking). This is the integration contract binding the slice's parts
together: the boundary bake + never-serialize, the passive static-blocker idiom, the trigger
identity, and the scene round-trip.

**Why:** the slice's parts are individually unit-tested, but "the island is walkable" is an
emergent property of their interaction (bake-on-load feeding collision, the classifier telling a
blocker from a sensor by identity, the round-trip preserving the polyline while dropping the baked
products) — only an end-to-end test proves it holds.
**Breaks:** any of: the coastline not re-baking on load (walk through the coast); a non-passive
blocker drifting when hit; a trigger blocking instead of sensing (or not firing); the polyline not
round-tripping; baked segments serialized then double-counted on reload.
**Tests:** `MonoDreams.Tests/LevelEditor/IslandMilestoneTests.cs` (`WalkableIslandMilestone` — the
full build → save → reload → play → restart-equivalent flow, in-process over the REAL editor +
engine systems; the environment cannot present a window, so this is the honest headless form, per
the Wave-5 in-process-integration precedent).
**Depends on:** this file — "A boundary bakes into one convex quad segment per polyline edge…",
"Trigger zones are Passive colliders identified by an auto-numbered EntityInfo string", "Scene
round-trip reconstructs from registered components, not factories"; collision — SAT + the `Passive`
static-blocker semantics.

## LDtk/Blender are import-only; the importer round-trips a parsed world to native

The LDtk and Blender parsers are no longer wired to live game boot (PS5). They are **import
machinery**: run once, via the import op (`Game1`'s headless `--export-scene <id>` /
`MONODREAMS_EXPORT_SCENE`, or a future editor toolbar action), to re-parse a legacy level into a native
`.mdscene` the game then owns. `LevelImporter` is the testable core: given a world a parser populated, it
tags every scene-content root with `SceneObjectComponent` (a top-level entity that is not
`EditorInfrastructureComponent` and not `BakedProductComponent`) so the canonical `SceneWriter`'s
membership closure captures it + its `ChildOf` descendants, then serializes through the registry.
Reconstruction on load is by components, never by re-running the parser — so every component a
factory/parser sets needs a registered serializer, and the reference factories set
`SpriteInfoComponent.AssetKey` (the tileset/texture content key) so the native reader re-loads the
texture. The parsers are composed only in the reference screen's `importMode`, never at boot; the import
op boots in `RunMode.Edit` so the frozen logic group cannot perturb the pristine parsed positions before
capture, and disposes system-built screen infrastructure (the `DialogueStateComponent` UI sub-graph)
before importing so only level content is captured.

**Why:** native `.mdscene` is the game's real level format; keeping the parsers as one-way importers
(not live loaders) is what makes the boot path native-only and closes the parser-asymmetry, while still
letting a gamedev migrate an LDtk/Blender level they already authored.
**Breaks:** if the importer did not tag the parsed roots, a straight `SceneWriter.Save` would write an
empty scene (the parsers never set the transient save-root tag); if a parsed component had no registered
serializer, its data drops silently (a loud write-time warning surfaces it).
**Tests:** `MonoDreams.Tests/LevelEditor/LevelImporterTests.cs` (LDtk-like + Blender-like worlds →
import → reload via the native reader → equivalent world: counts, game components, transforms, parent
graph; `TagContentRoots` excludes infra/bake and is idempotent).
**Depends on:** this file — "Scene round-trip reconstructs from registered components, not factories",
"Game components round-trip through registered serializers"; level-loading — "`LevelLoadRequestSystem`
resolves `LoadLevelRequest` native-only (fails loud otherwise)".

## The Examples levels are migrated to native `.mdscene`

The reference game's levels are committed native scenes under `Content/Levels/`. `Blender_Level.mdscene`
(the migrated Blender level: player Pete, NPCs Boldo/elephant-kid + their zones, the store collider) was
produced once by the import op and is byte-canonical (a load→save is a fixed point). It is bundled via an
MGCB `/copy:` entry (mirroring `sample.mdscene`), boots through the shipped native reader, and is what
the level-selection menu's "Level 1" resolves to. The LDtk `Level_0` is **not** migrated: its ~21k
per-tile entities would make a per-entity native scene a multi-MB artifact — it needs a native tile-layer
batching primitive first (a follow-up), so it stays import-only and is not offered by the menu.

**Why:** migrating the levels to native is what lets the game boot native-only (the fallback removal);
the migrated scene must round-trip through the shipped reader, which is what forced full component
serialization (engine + game) and the `AssetKey` fixes.
**Breaks:** a non-canonical hand-edit of the committed scene would break the byte-stable fixed point (the
byte-lock test catches it); a missing game-component serializer would throw when the scene boots.
**Tests:** `MonoDreams.Tests/LevelEditor/MigratedLevelTests.cs`
(`CommittedBlenderLevel_IsByteCanonical_LoadSaveIsAFixedPoint`,
`CommittedBlenderLevel_BootsThroughTheShippedReader_YieldingPlayerAndNpcs`),
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelBootsNative`.
**Depends on:** this file — "LDtk/Blender are import-only; the importer round-trips a parsed world to
native", "The game boots native scenes native-first via `LoadLevelRequest`".

## Game components round-trip through registered serializers

Full component serialization spans the game's own components, not just the engine's. The reference game
registers serializers for `PlayerState`, `OrbitalMotion`, `StopMotionEffect`, and `DialogueZoneComponent`
via `GameComponentSerializers.RegisterGameComponents`, and the engine ships one for
`CameraFollowTargetComponent` — so a native scene migrated from an LDtk/Blender level reconstructs the
same world through the registry. `RegisterGameComponents` is called on **both** the editor overlay's live
registry (in-editor Load/Save) **and** the shipped game's standalone native-reader registry (a booted
native scene reconstructs game components with no editor composed). Runtime-derived affordances that hold
unserializable handles — `NPCInteractionIcon` (a live `Entity` reference) and the interaction icon's
`DynamicTextComponent` (a live font) — are deliberately NOT registered; they are excluded from the scene
(as the export op does with the dialogue UI) and are a follow-up (entity-reference serialization + font
asset keys).

**Why:** the migrated Examples levels carry game components; if the shipped reader's registry knew only
engine serializers, a native boot would throw on the first game-component key.
**Breaks:** registering game serializers on the editor path but not the shipped path (or vice versa)
makes a scene load in one composition and throw in the other.
**Tests:** `MonoDreams.Tests/LevelEditor/GameComponentSerializerTests.cs` (each game component +
`CameraFollowTargetComponent` round-trips; the registry registers every game type),
`MonoDreams.Tests/LevelEditor/MigratedLevelTests.cs` (the committed scene boots through the shipped
registry).
**Depends on:** this file — "Scene round-trip reconstructs from registered components, not factories".

## A scene is ship-ready iff it has zero `file:` AssetKeys

A native scene is **"ship-ready / fully portable"** exactly when it carries **zero `file:`
AssetKeys** — every asset reference has graduated from the editor's drop-folder `file:` scheme
(loaded at runtime from the gitignored asset folder, desktop-editor-first) to an MGCB **content
key** (processed, shipped, web-ready). A `file:` key resolves to a magenta placeholder on a fresh
checkout or on web (no directory scan there), so "zero `file:` keys" is the checkable invariant for
"this committed level is portable". `SceneLint` (`Serialization/SceneLint.cs`) is the pure analyzer:
`FindFileAssetKeys(scene)` walks every entity's serialized component bodies for any JSON string using
the `FileAssetKey.Prefix` scheme (so it catches today's `SpriteInfoComponent.AssetKey` and any future
file-scheme reference without enumerating component types), and `IsShipReady` is the zero-findings
predicate. The editor logs a **loud warning on Save** when the scene being written still has `file:`
keys (never blocking — a `file:` scene is valid to author + iterate on); the committed
`Content/Levels/**` scenes are asserted ship-clean by a test (`Blender_Level` / `sample` use only
content-key AssetKeys — PS5).

**Why:** a versioned scene that references unversioned/gitignored drop-folder art breaks on a fresh
checkout and on web — the graduation to content keys is the exit, and "zero `file:` keys" is the
one-line, greppable, testable definition of "done" for shipping a level (project-persistence plan §7).
**Breaks:** a scene shipped (or committed as a reference level) with `file:` keys loads magenta
placeholders for anyone without the exact drop folder — silent-looking data loss on someone else's
machine; without a checkable predicate the graduation step is easy to forget.
**Tests:** `MonoDreams.Tests/LevelEditor/SceneLintTests.cs`
(`FindFileAssetKeys_FlagsTheFileScheme_WithEntityAndComponentContext`,
`IsShipReady_TrueForContentKeyOnly_FalseForAnyFileKey`, `FindFileAssetKeys_EmptyOrNullScene_IsShipReady`,
`FindFileAssetKeys_ScansNestedArraysAndObjects`, and
`AllCommittedExamplesLevels_AreShipClean_ZeroFileKeys` — the committed levels carry no `file:` keys).
**Depends on:** this file — "`file:` AssetKeys load drop-folder art at runtime and graduate to content
keys at ship" (the scheme this lint counts); "`SpriteInfoComponent` serializes an `AssetKey`, never the
live `Texture2D`".

## New levels bundle zero-touch: the editor appends the MGCB `/copy:` entry on first save

A brand-new saved `Content/Levels/<id>.mdscene` must be bootable after a normal build with **no
manual `.mgcb` editing** (project-persistence plan §3, banked decision 2). The shipped game reads
bundled scenes read-only via `TitleContainer` over MGCB-`/copy:`-bundled files (the one all-platform
read path — desktop `bin/…/Content/Levels/` AND web `wwwroot/Content/Levels/`), but `.mgcb` is an
explicit list with **no glob syntax**. So the editor — the sole creator of new levels, already
writing the `.mdscene` into the source tree (PS3) — on Save also **appends the `/copy:` entry** for
that level to the content project's `Content.mgcb` (`ProjectRoot/Content.mgcb`) if it is missing.
The append is a pure text transform (`MgcbLevelBundle.EnsureCopyEntry`, **idempotent** — a no-op for
a level whose entry already exists, so there is never a double-copy and no reshuffle), done through
`IPlatformServices` on the desktop-editor path only (Save is already gated on a resolved project root,
and the web project context is null → Save disabled → no append). **Exactly one mechanism** bundles
every level: the MGCB `/copy:` entry (committed levels' lines were hand-added once; new levels' lines
are editor-appended).

**Why not a build-time glob:** a full `Content.npl` Nopipeline regen of the hand-maintained
`Content.mgcb` sweeps the **gitignored Island placeholder-art pack** into the MGCB texture build (via
the recursive `*.png` group — empirically ~800 entries), breaking a fresh checkout where those files
are absent; and a raw-copy MSBuild `<None>`/`.targets` reaches the desktop output but **not** the web
`wwwroot/Content/` (only the KNI content builder stages there, via the `.mgcb`). The `/copy:` path is
the validated, console-portable, all-platform mechanism; the editor appends its line because the
editor is where the level is born and already edits dev files. The `.npl` `Levels/*.mdscene` copy
group is a **declarative record only** here (Nopipeline is not wired to regenerate this project's
hand-maintained `.mgcb`).
**Breaks:** a new level saved but not `/copy:`-listed is invisible to `TitleContainer` at runtime and
the game fails to boot it; a non-idempotent append double-copies (or churns) on every re-save; running
the append on web (no source tree) or when the project is unresolved would write to nowhere.
**Tests:** `MonoDreams.Tests/LevelEditor/MgcbLevelBundleTests.cs`
(`EnsureCopyEntry_AppendsBlock_WhenAbsent`, `EnsureCopyEntry_Idempotent_WhenAlreadyPresent`,
`EnsureCopyEntry_DistinguishesSimilarIds_WholeLineMatch`, `EnsureCopyEntry_HandlesMissingTrailingNewline`,
`CopyLine_MatchesTheContentRelativeFormat`, and
`CommittedMgcb_HasACopyEntry_ForEveryCommittedLevel` — every committed level is already `/copy:`-listed).
**Depends on:** level-loading — "Native `.mdscene` levels are bundled by an MGCB `/copy:` entry and read
via `TitleContainer`" (the read side this feeds); this file — "The editor Save writes versioned
`.mdscene` into the project source tree" (the same Save that appends the entry); foundation —
`IPlatformServices` (`FileExists`/`ReadAllText`/`WriteAllText`).

## Prefabs are LINKED instances: a compact `prefab` entry with diff-based whole-component overrides, expanded from one `.mdprefab` (PF-C)

A `.mdprefab` is the `SceneData` schema reused verbatim (same `CanonicalJson`, same
component-serializer registry, same stable ids) with the prefab rules enforced on WRITE
(`PrefabWriter` + `PrefabData.FindSingleRootIndex`): **exactly ONE root** (every other entity must
parent-chain to it — a multi-root world is refused loud), the **root Transform position normalized to
origin** (children keep their LOCAL offsets — `TransformComponent.Position` is parent-relative, so
normalization touches only the root), and **no `camera`** (a prefab is a class, not a scene —
pre-mortem #8: the rig is scene-context-only). A scene places a prefab as a **LINKED instance**: an
`entities[]` entry gains an **additive optional `prefab: "<id>"` field** and a `components{}` holding
ONLY `core.Transform` (always instance-owned) plus **whole-component overrides**. Override detection is
**diff-based, not bookkept** (pre-mortem #1): the writer serializes the instance root's live components
and OMITS any whose canonical bytes EQUAL the prefab root's same-key bytes (inherited), KEEPS the
byte-different (changed) and the prefab-absent (added) ones; `core.Transform` is always kept.
Canonical determinism (the byte-stable serializer) is what makes byte-equality a reliable "inherited"
test. The instance's children are **prefab-owned and NEVER serialized** — the membership closure adds
the instance root but stops the descent at it (pre-mortem #3), so a scene file contains ZERO
instance-child entries. `PrefabInstanceComponent { PrefabId }` is the runtime marker, **structurally
captured** like `SceneEntityIdComponent` (written as the `prefab` field, never a `components{}` body,
never tripping the unregistered-component warning, hidden by the editable Inspector).

Expansion is **ONE implementation** (`PrefabExpander`), shared by the scene reader (each `prefab`
entry, through a new optional `SceneSerializer.Deserialize` prefab-expander callback), the
`PrefabFactory` (the `"prefab:<id>"` `EntitySpawnRequest` channel — a prefix-dispatched
`IEntityFactory`), and live propagation: it reconstructs root + children through the normal
deserialize path (texture rehydration + transient `DrawComponent` restore included, via the shared
`SceneRehydration` — the reader's top-level loop never sees the prefab-owned children, so the expander
finishes them), applies the instance's `components{}` OVER the root (whole-component replace via the
registry), and stamps the marker; the reader's re-tag then stamps `SceneObjectComponent` + the scene
id on the ROOT only (children are ordinary `ChildOf` descendants — the closure covers them, so they
are never re-tagged and never mistaken for scene roots). It **fails loud** on a missing prefab (the
unknown-component stance: the reader aborts the load; the factory warns-and-drops per its convention)
and refuses **cycles** — an instance of X inside X, directly or transitively, is refused at save
(`PrefabWriter`, via the referenced prefabs' `prefab` entries) and capped at load (an expansion stack
+ a `MaxDepth` backstop, pre-mortem #7). Because both writer and reader route through the same diff +
the same expansion, `save → load → save` on a scene with instances is a **byte fixed point**. Prefabs
bundle zero-touch by the same MGCB `/copy:` mechanism as levels, under `Content/Prefabs/<id>.mdprefab`
(source-first in the editor via `PrefabFileSource`, shipped via `TitleContainer`). **v1 limitation:** a
component the prefab HAS but that was REMOVED on an instance does not persist — re-expansion restores
it (the tracked per-instance removal set is named terrain; v1 is whole-component overrides + additions).

**Propagation.** Editing + re-saving a prefab refreshes every open instance
(`PrefabPropagation.ReExpand`): each instance's current overrides are captured (diffed against the OLD
prefab — the definition the open instances currently reflect), its prefab-owned subtree despawned, and
it is rebuilt from the NEW prefab (resolved through the expander's source) with the overrides + scene
id re-applied. Because a live rebuild disposes and recreates entities, if ANY instance was rebuilt the
editor history is **cleared and the scene marked dirty** (the Restart rule — stale undo/redo commands
would dangle, pre-mortem #2); with no open instance of that prefab the history is left completely
untouched. Smarter command-merging is terrain.

**Why:** prefabs are the "build NPCs / dialogue zones / the Player as classes" surface; LINKED
instances (vs. baked copies) let a prefab edit propagate, and diff-based overrides mean no per-edit
flag bookkeeping to desync. Reusing `SceneData` keeps ONE serializer / ONE canonical policy / ONE
stable-id scheme; ONE expander keeps the reader, the code/factory path, and propagation from drifting.
Fail-loud + cycle refusal mirror the unknown-component stance (a corrupt reference is a visible error,
not a silent half-load or an infinite loop).
**Breaks:** a multi-root or camera-bearing prefab corrupts placement/expansion; bookkept overrides
desync from the live component; serializing instance children double-expands on load and bloats the
diff; a second expansion implementation drifts from the reader; a non-diff (bytes-nondeterministic)
override test turns inherited components into phantom overrides (churned diffs); an uncapped cycle
hangs the load; propagation under a live history dangles undo commands onto disposed entities.
**Tests:** `MonoDreams.Tests/LevelEditor/PrefabFormatTests.cs` (one-root validation + refusal, origin
normalization with children keeping local offsets, camera omission, additivity of the `prefab` field,
the `PrefabDiff` matrix, `CanonicalEquals`, membership exclusion, direct cycle refusal);
`MonoDreams.Tests/LevelEditor/PrefabExpansionTests.cs` (reader expansion — root/children/overrides/
marker/re-tag/rehydrate; the `save → load → save` byte fixed point; missing-prefab + no-expander
fail-loud; self-referencing cycle capped at load; transitive cycle refused at save; the factory channel
+ `EntitySpawnSystem` prefix dispatch + unknown-id warn-and-drop; propagation override-preservation +
the history-clear rule / untouched-when-none); `MonoDreams.Tests/LevelEditor/MgcbLevelBundleTests.cs`
(the prefab `/copy:` bundling helpers); the end-to-end acceptance walkthrough
`MonoDreams.Tests/LevelEditor/PrefabMilestoneTests.cs`
(`PrefabWalkthrough_NpcDialogueZonePlayer_BuildPlaceOverridePropagateBootPlay` — the compact instance
entries + zero serialized children + the diff-based override + the `save → load → save` byte fixed point +
boot-and-play, all with the game components; `UnpackedNpcInstance_SerializesExpanded…` and
`PrefabFactory_SpawnsInstanceAtRuntime…` — the unpack + `EntitySpawnRequest("prefab:<id>")` channel).
**Depends on:** this file — "Scene round-trip reconstructs from registered components, not factories"
(the deserialize/rehydrate/re-tag path expansion rides), "Scene serialization is canonical and
byte-stable…" (the determinism the diff needs + the additive `prefab` field), "The component-serializer
registry is opt-in per type…" (the structural-capture + fail-loud stance), "The editor Save writes
versioned `.mdscene` into the project source tree" + "New levels bundle zero-touch…" (the Prefabs dir
joins both), "The editor history tracks a dirty save-point signal" (the propagation Clear + dirty);
level-loading — "`EntitySpawnRequest` → `EntitySpawnSystem` → registered `IEntityFactory`" (the
`prefab:` prefix channel), "Native `.mdscene` levels are bundled by an MGCB `/copy:` entry…" (the same
mechanism prefabs join).

## The prefab UX: a Prefabs shelf, prefab-context tabs, Save Prefab + propagation, and instance-children guardrails (PF-D)

PF-D surfaces the PF-C prefab core (above) as the designer workflow, over the PF-A Inspector and PF-B
viewport-context stack. Six moving parts, one mechanism each:

- **The Prefabs shelf tab.** The bottom shelf's tab strip is now interactive (`Assets | Prefabs`,
  OWNED by `PalettePlacementSystem` — the retired static tab left `EditorChromeBuilder`). The Prefabs
  tab lists `Content/Prefabs/*.mdprefab` (source-first via the overlay's injected `ListPrefabIds`; an
  unresolved project ⇒ an empty shelf + a message) as cards — a real art **thumbnail** when the prefab
  has a sprite, else an `EditorIcons.Prefab` glyph (a box/package), + the id label (PF-G — see "The
  prefab shelf card + placement ghost show the prefab's dominant sprite"). A card click **arms placement**
  through the SAME Place-mode machinery assets use (so there is ONE `Place`-mode owner, ONE ghost, ONE
  placement click); asset/trigger/prefab arming is mutually exclusive. **The prefab placement ghost**
  shows the prefab's dominant sprite following the cursor (a spriteless prefab shows a crosshair) so
  placement is aim-able (PF-G). A viewport click stamps a **linked instance**
  at the snapped cursor through the overlay's undoable `PlacePrefabInstance` → a `CreateInstanceCommand`
  (delete-undo disposes the whole instance subtree, nothing dangles; redo re-instantiates through the
  ONE `PrefabExpander`). Card right-click → `PrefabCardMenu` (**Edit Prefab** / **Delete**,
  id-suffixed paths); double-click → Edit; empty-shelf right-click → `PrefabShelfMenu`
  (**Create Empty Prefab…**). **Delete** refuses loud when the open scene (the live world's
  `PrefabInstanceComponent` roots OR the backgrounded Scene snapshot's compact `prefab` entries) holds
  an instance of it, else a destructive confirm → a source-file delete (not undoable — a file delete).

- **Prefab-context tabs.** Opening a prefab pushes a `ViewportContextKind.Prefab` context onto the
  stack (`EditorTransport.OpenPrefab` → `ViewportContextStack.OpenPrefab`): an empty world loaded with
  the prefab's entities via the reader (source-first), auto-framed, its own scene-id (= the prefab id)
  and its own dirty/save-point (the history is cleared on open). One tab per prefab (a second open just
  activates it); multiple prefab tabs may coexist; switching between any tabs is the ONE stack
  mechanism, and per-context dirty is isolated (each context's `WasDirty` is captured on leave and
  reproduced on return). **Play is disabled in a prefab tab** (a prefab never plays, v1 — the transport
  lands it Paused). **The × is dirty-gated** (pre-mortem #9): `DecideClose` returns `ConfirmDirty` for a
  dirty prefab tab, and `Transport.CloseTab` activates the tab first, then routes
  `ConfirmDirtyClose` → the dialog's **Save & Close / Discard / Cancel** confirm (Save & Close writes the
  prefab then `CloseCleanContext`; Discard closes discarding edits; Cancel keeps it).

- **No camera rig in a prefab context (pre-mortem #8).** A prefab is a class, not a scene — it has no
  camera rig. The gate is FOUR-fold: (1) the rig **glyph** is `ActiveContextKind == Scene`-only (the
  camera-rig premise); (2) the **"Camera" tree row** is folded in by `EditorPanelSystem.MaterializeScene`
  only when the active context is not `Prefab`; (3) the reader's **rig sync is suppressed** — a
  prefab-context load/restore carries `LoadSceneRequest.SuppressCameraRig` (set by the stack's transient
  `RestoringPrefabContext` flag during the synchronous restore), so `SceneReaderSystem.ApplyCamera`
  frames the free VIEW on the prefab content but NEVER calls the rig seam (a rig sync would corrupt the
  SCENE's authored camera); (4) the prefab-context **snapshot captures a camera-less scene**
  (`SceneWriter.BuildScene(world)` with no camera/layers) and `PrefabWriter.BuildPrefab` nulls any
  camera — so **a prefab save emits no camera**.

- **Save Prefab.** In a prefab context the Save button/`dialog:prefab` opens the **Save-Prefab** dialog
  (a single primary [Save Prefab] + Cancel — a prefab is one file, no Save Project / Backup); the `Game`
  block cause cannot apply (a prefab tab is not the Game tab), and it is gated only by Playing (can't
  happen — Play is disabled) + `NoProjectRoot`. Confirm runs `EditorOverlay.SavePrefabCurrent`: the PF-C
  validated `PrefabWriter.BuildPrefab` (one-root + origin-normalize + no camera + cycle-refuse — a
  multi-root world / cycle is **refused loud**, keeping the dialog's intent), then bundle (zero-touch
  MGCB `/copy:./Prefabs/<id>.mdprefab`), `MarkSavePoint`, and propagate. **The empty-save guard does NOT
  apply to a prefab save** — an empty prefab (its one root, nothing else) is LEGAL while assembling; the
  one-root validation is the only gate (see the empty-save premise's prefab reconciliation).

- **Propagation on save (the chosen mechanism).** `SavePrefabCurrent` re-expands live-world instances
  via `PrefabPropagation.ReExpand` (the history-clear rule applies to the LIVE world only — but the live
  world in a prefab tab is the PREFAB world, which never self-instances, so 0 are rebuilt and the history
  stays clean). A **backgrounded SCENE context needs NO eager action**: its snapshot is COMPACT (holds
  `prefab` entries, not expanded children — the writer stopped the closure at each instance root), so the
  scene's **next restore re-expands through the reader reading the just-saved prefab source-first** — the
  overrides (diffed against the old prefab at snapshot time) apply over the NEW root. This is why "the
  scene picks up the new prefab on return" for free; a stale-marking mechanism would be redundant.

- **Creation flows.** **Create Prefab from Selection…** (entity menu + Entity header, scene context):
  a name modal (sanitized, collision-refused) → capture the single-root selection's subtree into a
  temp world → `PrefabWriter.BuildPrefab` (origin-normalized; a prefab-owned child or an
  already-an-instance selection is refused loud) → write + bundle → **replace the selection with a
  linked instance preserving world position** as ONE undoable composite (`DeleteEntityCommand` +
  `CreateInstanceCommand`); undo restores the original entities and removes the instance, and **the FILE
  stays** (written before the composite, never touched by undo). **Create Empty Prefab…** (Prefabs shelf
  menu): a name modal → a minimal one-root `.mdprefab` (one empty root at origin — satisfies one-root
  validation) → bundle → open its tab.

- **Unpack** (entity menu on an instance root, `Danger`): `UnpackPrefabCommand` drops the
  `PrefabInstanceComponent` marker, keeping every live entity as an ordinary scene entity (its children
  become closure-serialized again); undo re-links (restores the compact serialization). No dispose — the
  root handle is stable across the toggle.

- **Instance-children guardrails.** Children of an instance are selectable but **not editable** in a
  scene: every mutation path — gizmo drag, modal G/S/R, delete, collider add/remove, order nudge, and
  Inspector edit/add/remove — consults the ONE shared predicate `PrefabGuards.IsPrefabOwned` (a strict
  `ChildOf` descendant of a `PrefabInstanceComponent` root) and refuses with the shared loud hint
  (`PrefabGuards.Refusal` — a `Logger.Warning` IS the status hint; there is no toast channel). The
  instance **ROOT stays fully editable** (it carries the marker, so it is NOT "owned" — its Transform is
  always instance-owned, other component edits become overrides via the diff), and deleting the ROOT is
  legal (the whole instance goes, snapshot-undoable). Placing a prefab instance INSIDE a prefab tab is
  v1-refused with a hint (nested-prefab authoring is terrain — the core recursion exists but is not
  surfaced).

- **Capture + assembly hardening (PF-F).** Create-Prefab-from-Selection captures through the ONE
  `PrefabCapture` helper (shared by the overlay and its test double, so they never drift): it finds the
  parentless root ROBUSTLY (never `created[0]`) and **refuses an empty capture** — a bare-`core.Transform`
  root with no children and no other components is "selection appears empty - nothing captured" (the
  user's `elephant-kid` empty-shell symptom; Create-Empty is the path for a bare root), refused loud +
  on the status bar with nothing written. It names the captured root (its own `EntityInfoComponent`,
  else `EntityInfoComponent(prefabId)`). On success it confirms `Created prefab '<id>' (N entities)` on
  the status bar and **refreshes the shelf** (`PalettePlacementSystem.RefreshPrefabs` — also after
  Create-Empty / Save-Prefab / a card Delete / the RefreshCatalog action) so the card appears without a
  relaunch. Every root-creating action **inside a prefab tab** (palette place, trigger place, Add Empty
  Entity, boundary commit) **auto-parents** the new entity under the single prefab root
  (`CreateEntityCommand.parentTo` → `PrefabContextRoot.Resolve`, dropping its save-root tag), so assembly
  can never leave a second, un-savable root; undo/redo re-establish the parent. A genuine multi-root
  Save-Prefab refusal **names the offending roots** ("2 roots: 'A', 'B' - parent everything under one
  root"). A placed **instance** gets a UNIQUE `EntityInfoComponent` name — the prefab root's name (else
  the prefab id), uniquified against the live world (`House`, `House 2`, … via `EntityNaming`) — applied
  before the tree materializes (a `CreateInstanceCommand(autoName: true)`). Guardrail hints + prefab
  refusals + capture confirmations all surface via the status-bar notify seam, not just the log.

**Ops.** `prefabs:list`; `prefab:edit <id>` (open the tab), `prefab:place <id>` (one-shot place at the
cursor), `prefab:unpack`, `prefab:delete <id>`, `prefab:create-from-selection <name>`,
`prefab:create-empty <name>`; `dialog:prefab` (the Save-Prefab confirm rides the dialog grammar);
`panel:tab prefabs` switches the shelf. The status bar's right side reads `prefab: <id>` (+ the system's
dirty `●`) in a prefab context.

**Why:** PF-D is the user's actual goal — build NPCs / dialogue zones / the Player as prefabs, place
them, edit them, and see edits propagate. Reusing ONE Place-mode owner / ONE expander / ONE stack / ONE
dialog machinery / ONE guard predicate keeps the surface from forking (the no-duplicate-ways tenet). The
no-rig gate keeps a prefab a class (no camera). The compact-snapshot propagation mechanism is correct
for free — the reader IS propagation.
**Breaks:** a rig materializing in a prefab tab serializes a camera into the `.mdprefab` (format
violation) or corrupts the scene's authored camera; an empty-save guard on a prefab blocks assembling a
new one; an editable instance child desyncs from the prefab silently (Godot's editable-children trap);
a create-from-selection that did not preserve the file on undo loses the extraction; a second Place-mode
owner fights the palette; deleting a prefab with live instances strands them.
**Tests:** `MonoDreams.Tests/LevelEditor/PrefabUxTests.cs` (the `IsPrefabOwned` matrix + a system-level
Modal refusal; `CreateInstanceCommand` place/undo/redo; `UnpackPrefabCommand`; the create-from-selection
composite + file-stays; one-root empty-legal + multi-root-refused; the background-scene re-expansion
mechanism; the reader `SuppressCameraRig` gate; the status-bar text);
`MonoDreams.Tests/LevelEditor/ViewportContextStackTests.cs` (`OpenPrefab*`, rig-suppressed restore, one
tab per prefab, per-context dirty isolation, `DecideClose`→`ConfirmDirty`, `CloseCleanContext`);
`MonoDreams.Tests/LevelEditor/PalettePlacementTests.cs` (`PrefabShelf_ArmPrefab_ThenViewportClick…`);
`MonoDreams.Tests/LevelEditor/EditorContextMenuTests.cs` (the extended entity menu + `PrefabCardMenu` /
`PrefabShelfMenu`); the end-to-end acceptance walkthrough
`MonoDreams.Tests/LevelEditor/PrefabMilestoneTests.cs` (Create-Prefab-from-Selection → place instances →
per-instance override → Save-Prefab propagation on the scene's restore → Unpack, driven exactly as the
overlay's prefab ops drive them; PF-F: `MultiEntityCapture_DeepFamily…` — a deep family captured via the
root, placed, saved compact, reloaded, re-expanded, and a bare-root capture refused;
`AssembleFromEmpty_ViaPalettePlacement_AutoParented_SaveSucceeds_InstancesNamedUnique`).
PF-F unit coverage: `MonoDreams.Tests/LevelEditor/PrefabCaptureTests.cs` (robust root-finding, the empty
refusal, root naming, the uniquifier series), `MonoDreams.Tests/LevelEditor/PrefabContextHygieneTests.cs`
(auto-parent + undo/redo, the `PrefabContextRoot` resolver, the screen-infra delete guard,
`Transport.IsScreenInfrastructure`).
**Depends on:** this file — "Prefabs are LINKED instances … (PF-C)" (the core this exposes: the
expander, the diff-based compaction, `PrefabPropagation`, the bundling), "The viewport context stack is
the ONE tab-switching mechanism … (PF-B)" (prefab contexts are its new consumer), "The editor splits the
free VIEW from the authored camera rig" (the four-fold no-rig gate), "The editor's Save dialog is a modal
three-action chooser …" (the Save-Prefab + confirm modes on the same machinery), "The palette lists
assets as cards …" (the Prefabs tab mirrors it), "Save is blocked while Playing, while the Game tab is
active, or when no project root is resolved" (Save Prefab reuses the guard, minus the Game cause), "The
Inspector is editable, DevTools-style … (PF-A)" (works as-is in a prefab context, guardrail-gated on
children), "Editor-overlay entities are standalone; delete snapshots the disposed sub-graph" (the
instance-subtree dispose on unstamp/undo), "The editor history tracks a dirty save-point signal"
(per-context dirty + the save point).

## The prefab shelf card + placement ghost show the prefab's dominant sprite (PF-G)

A prefab's **dominant sprite** is resolved by a pure ROOT-FIRST walk of its `.mdprefab` entities
(`PrefabSpritePreview.TryResolve` — the root, then its `ChildOf` descendants breadth-first, in document
order) for the first `core.SpriteInfo` with a usable asset key. The user's `house` prefab carries its
sprite on the CHILD (`House2`), so the walk must descend — reading only the root would find nothing. The
resolved key feeds the palette's `FileAssetTextureLoader` — the SAME texture path the asset cards use —
so a **Prefabs shelf card** shows a real aspect-fit thumbnail when the prefab has a sprite and its art
loads (else it falls back to the `EditorIcons.Prefab` glyph); resolution is cached per shelf refresh (on
the card, rebuilt by `RefreshPrefabs`). While a prefab is **armed**, the placement **ghost** shows that
same sprite (the asset-ghost machinery, `GhostTint`) at `SnapPosition(cursor) + Offset`, where `Offset`
is the sprite's WORLD position WITHIN the prefab (root normalized to origin) — so the ghost sits exactly
where the placed instance's sprite will land (the instance root goes at the snapped cursor). A
**spriteless** prefab (a bare dialogue zone) shows a `GhostTint` crosshair instead. The crosshair is a
Main-target mesh with world-baked vertices and NO `TransformComponent` (so `MeshPrepSystem` skips it) and
NO `SpriteInfoComponent` (so `CullingSystem`/`SpritePrepSystem` ignore it) — the `ColliderDebugSystem`
debug-mesh idiom.

**Why:** the user's first prefab session had prefab cards as generic glyphs and placement blind (no
ghost), so a prefab was indistinguishable from another and impossible to aim. Resolving through the SAME
loader as asset cards keeps ONE texture path; positioning the ghost at `cursor + Offset` keeps the
preview and the actual placement in agreement (aim-able).
**Breaks:** reading only the prefab root finds no sprite for the common root-is-a-container shape (the
`house`); a ghost centred at the raw cursor (ignoring `Offset`) shows the sprite where it will NOT land;
a crosshair built with a `TransformComponent` is overwritten by `MeshPrepSystem`; gating the ghost on
texture availability (rather than sprite presence) would hide the aim indicator for a prefab whose art is
a missing-file placeholder (the asset ghost shows regardless).
**Tests:** `MonoDreams.Tests/LevelEditor/PrefabSpritePreviewTests.cs` (root-first walk descends to the
child sprite with its world placement; root sprite preferred; spriteless / null → false);
`MonoDreams.Tests/LevelEditor/PalettePlacementTests.cs`
(`PrefabGhost_ArmedPrefabWithSprite_ShowsSpriteGhost_FollowingCursor_AtPlacementSpot`,
`PrefabGhost_OutsideViewport_ParksTheGhost`, `PrefabGhost_SpritelessPrefab_ShowsCrosshair_NotSpriteGhost`,
and the `PrefabShelf_ArmPrefab…` crosshair fallback).
**Depends on:** this file — "The prefab UX … (PF-D)" (the shelf cards + Place-mode ghost this enriches),
"Prefabs are LINKED instances … (PF-C)" (the `PrefabData` the resolver reads, the `CreateInstanceCommand`
placement the ghost previews), "The palette lists assets as cards …" (the same thumbnail/texture path);
rendering — "Layer depth ownership" (the ghost/crosshair are Main-target draws the real pipeline renders).

## See also

- `docs/CORE_TENETS.md` — "The editor is part of the game" + the interaction matrix.
- `MonoDreams/foundation/docs/premises.md` — the run-state model premises.
- `docs/flows/level-editor.md` — the per-frame flow doc.
- `scene-format.md` — the native MonoDreams scene format spec (the registry fills its `components{}`).
- `docs/level-editor/roadmap.md` — the Wave A–F map + the foundation plug-in points.
