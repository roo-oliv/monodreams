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
> load and discards unsaved edits), and the **project-persistence PS1** invariant
> (canonical, byte-stable scene serialization + a persisted stable scene-local id
> ordering `entities[]`). No premise here ships `Tests: none yet`.

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
Transient / overlay entities (cursor, UI / HUD, the editor's gizmo / selection / toolbar) are
untagged → excluded; Blender-origin entities are untagged in this wave (their save is deferred) →
view-only. `SceneWriter` computes the closure, serializes it through the Wave-2 `SceneSerializer`
into a `SceneData` (attaching the active `Camera` state and the `DrawLayerMap` banding), and writes
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
and Blender-style entities excluded; `DerivedDepthReproductionTest` — after reload, a prep + `YSortSystem`
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

Additionally, when the reader is given the screen's live `Camera` **and** the loaded scene has **no
active** `CameraFollowTargetComponent` (a dressed prop-only scene has no player, so nothing else moves
the camera onto the content), it **auto-frames** the camera on the loaded content's world-space AABB
(centre + zoom-fit, via the pure `CameraNav` frame-scene math) — so an off-origin scene (e.g.
`Blender_Level` at ~(1275,−530)) is visible instead of the camera sitting at (0,0) while the content is
elsewhere. When an active follow target IS present the reader leaves the camera untouched
(`CameraFollowSystem` owns it in Play). No content ⇒ no-op. The `Camera` is an optional reader ctor
param (the overlay supplies the screen's camera; the pure round-trip tests pass null and skip framing).

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
**Depends on:** rendering — `SpritePrepSystem`'s `[With(DrawComponent, …)]` query and `CullingSystem`'s
`VisibleComponent` add (the draw-path gates); this file — "Editor camera navigation pans/zooms/frames
the scene directly" (the `CameraNav` frame-scene math reused); "Y-sorted props use the feet-origin
convention, factory-applied" (`SpritePropFactory`, the pairing this mirrors); camera —
`CameraFollowTargetComponent` (the follow-target signal that suppresses auto-framing).

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
round-trips and re-serializes identically, so the fixed point holds).

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
`NewRootAfterLoad_GetsNextFreeStableId`).
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
and a proxy's centre-handle press deselects the proxy and despawns the family.
**Tests:** `MonoDreams.Tests/LevelEditor/SelectionTests.cs` (`SelectionTopmostTest` — stacked sprites
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
HUD) — inside the shell's top bar, with buttons sized in physical pixels and each carrying a
`ToolbarButtonComponent` binding a click to an `EditorToolbarAction`. `ToolbarSystem` hit-tests
the cursor's `ScreenPosition` (backbuffer **device** pixels — the raw mouse × the viewport
manager's `DevicePixelRatio`; the chrome sits in the margins where the virtual mapping is null
and `VirtualPosition` is frozen) against the button `Bounds` and hands
the action plus the frame's `GameState` to a dispatch supplied by the overlay — which wires the
left-most TRANSPORT buttons (Play/Pause — one toggle whose label `ToolbarSystem` syncs with the
state — and Restart) through the shared `EditorTransport`, Save through
`SceneWriter.Save(world, file, camera, layers)` (the **same** `SceneSerializer`), Load by publishing a
`LoadSceneRequest` (handled by the registered `SceneReaderSystem`), Undo/Redo on the **same**
`EditorHistory`, snap-toggle flipping the shared `GizmoStateComponent.SnapEnabled`, and tool-select
setting the shared `GizmoStateComponent.Tool`. There is exactly one `EditorHistory` / one gizmo-state
entity / one `EditorTransport` — the toolbar never constructs a second. Under the transport model
the toolbar is live in BOTH transport states (the chrome pass always renders while the editor is
composed): the transport buttons dispatch always — they are how you leave either state — while the
EDITING buttons (tools / Save / Load / Undo / Redo / Snap) dispatch only while Paused (`Edit`) and
render with the disabled fill while Playing (an undo racing live physics would be surprising; a
viewport click belongs to the game).

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
tool, snap-toggle flips the flag, Save invokes `SceneWriter` through a fake `IPlatformServices`, Load
publishes a `LoadSceneRequest`, Undo/Redo drive the shared history, empty-stack undo is a no-op);
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (native `ScreenPosition` hit-test dispatches in
Edit, misses outside the bounds, inert in Play).
**Depends on:** ui — `SimpleButtonComponent` / `ButtonMeshPrepSystem`; cursor —
`CursorInputComponent.ScreenPosition`; level-editor — "The editor shell insets the game viewport
and renders its chrome at native resolution", "Bounded undo with drag-coalescing", "Scene
round-trip reconstructs from registered components, not factories".

## The editor shell insets the game viewport and renders its chrome at native resolution

While the editor is composed (the run flag — the shell is CONSTANT across transport states, it
never collapses while Playing) the game composite (Main/UI/HUD layers) renders into a **smaller
centered viewport** with chrome margins reserved around it (Blender-style: top toolbar bar, right panel
strip — the Wave-8 systems panel's home — and a thin bottom strip; `EditorChromeLayout` owns the
numbers), while the chrome itself renders on `RenderTargetID.Editor` — a render target at
**native window resolution**, recreated on resize (`EditorChromeRenderSystem`), composited 1:1
over the whole window via `RenderLayer.Native`, with opaque dark panel backgrounds so it reads
over any level. The inset lives on the `ViewportManager` (`SetViewportInset` /
`ClearViewportInset`) — the **single source of truth** — so FinalDraw compositing and
`ScaleMouseToVirtualCoordinates` follow the same rectangle: clicks inside the inset viewport map
to correct world positions with no extra math, clicks in the margins map to null
(`CursorInputComponent.OutsideViewport` is set, muting selection picks / gizmo drag-starts /
camera-nav zoom+pan) and are consumed by the chrome in screen space against `ScreenPosition`.
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
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (panels cover exactly the inset margins,
native-pixel button bounds, relayout on resize, the shell stays composed while Playing + dispose
restore, `OutsideViewport` press never picks; DPR: scale-2 metrics double, scale-1 is the pre-DPR
layout byte-identically, chrome hit-test space == chrome render space at DPR 2);
`MonoDreams.Tests/LevelEditor/OverlayProjectionTests.cs` (world→screen through camera + inset
destination, virtual space ignores the camera, zoom moves geometry but never emitted sizes,
viewport clipping, gizmo handle size constant across zoom on the Editor target, proxy outline
screen-baked at the expected pixels).
**Depends on:** rendering — "The viewport inset moves compositing and mouse mapping together",
"Three render targets, two behaviors" (+ `ViewportManager.DevicePixelRatio`); cursor —
`CursorInputSystem` (ScreenPosition × DPR) and `CursorPositionSystem` (sets `OutsideViewport`);
foundation — "Default RunMode=Play" (the flag-off/Play path must stay byte-identical).

## Editor camera navigation pans/zooms/frames the scene directly, Edit-guarded, before the cursor's world-pos derivation

In `RunMode.Edit` the editor — not `CameraFollowSystem` — owns the camera (the §9 interaction
matrix: camera-follow is `Freeze`-gated, "in Edit the editor drives `Camera.Position`/`Zoom`
directly"). `CameraNavSystem` provides that drive: **pan** (middle-mouse drag → the camera moves the
opposite way to the cursor's virtual-pixel delta so the grabbed world point stays under the cursor —
`Position -= virtualDelta / Zoom`), **zoom** (scroll wheel → a geometric step on `Camera.Zoom`,
clamped to a sane range, default 0.25–4.0), and **frame-scene** (a key edge centres the camera on the
AABB of all renderable content — every `SpriteInfoComponent` + `TransformComponent` entity, via the
pure `GizmoTransform.SpriteWorldQuad` corners — and zoom-fits it with a margin; **no content is a
no-op**). The system is **Edit-guarded** (inert in Play — it must not fight `CameraFollowSystem`) and is
registered **before `CursorPositionSystem`** so the camera mutation it makes this frame is the camera
state `CursorPositionSystem` reads when deriving the cursor's world position — no one-frame lag between
a pan/zoom and the cursor's world coordinate. Pan reads the cursor's **virtual** (pre-camera) position,
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
run-state model (`GameState.RunMode` + the `Freeze`-gated `CameraFollowSystem` the editor replaces in Edit).

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

## The editor run flag composes the always-on editor and the transport owns RunMode

The editor run configuration — the `--editor` launch arg **or** the `MONODREAMS_EDITOR=1`/`true`
environment variable, both settable in an IDE run configuration and parsed by the pure
`EditorRunFlag.IsEnabled` — is the **ONLY way into the editor**. It makes the host register every
screen with `editorEnabled: true` (composing the `EditorOverlay`: selection, gizmo, undo, toolbar,
camera nav, scene save/load, headless channel) and boots the transport **Paused**
(`ScreenController.State.RunMode = RunMode.Edit`). From there the editor is ALWAYS visible — no
key toggles it away (the F1 mode toggle is retired end-to-end) — and `RunMode` is flipped
exclusively by the `EditorTransport`: the toolbar's Play/Pause + Restart buttons or the headless
`Play`/`Pause`/`Restart` ops. The boot mutation is an explicit host-level opt-in **after**
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
selection picks on the final source-derived `LayerDepth`). Per-screen edit policies are declared at
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
through `EditorOverlay.BindPipelines` and builds its rows once (entries are fixed after `Build()`).
One protection: the panel **refuses to disable its own entry AND any ancestor group of it** — its
gate off (directly, or as cascade collateral) means no update, no hit-test, and no UI path back.
Collapse/expand of group rows is a deliberate non-feature for now: the wheel scroll covers the
reference compositions' row counts (a follow-up if trees grow much deeper).

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
**Tests:** `MonoDreams.Tests/LevelEditor/SystemsPanelTests.cs` (rows mirror both pipelines' entries
+ policy tags; checkboxes reflect live enabled state; a row click calls `SetEnabled` and the gated
system actually stops in both modes — side-effect counter — and a second click re-arms it; inert in
Play; wheel scroll in whole clamped lines; scrolled-out rows parked; the panel refuses to disable
itself; tree rows render groups before children with depth-indented checkboxes and local-name
labels; the group checkbox maps On/Mixed/Off to filled / filled-with-minus-bar / empty; a group
click follows the Gmail convention — Mixed or On → all off, Off → all on; a leaf click inside a
group toggles only that leaf; the panel refuses to toggle an ancestor group of its own entry while
the sibling leaf stays toggleable).
**Depends on:** this file — "The pipeline registrar is the composition seam" (the binding + the
derived tri-state), "The editor shell insets the game viewport and renders its chrome at native
resolution" (the strip, the `ScreenPosition` rule, the no-`VisibleComponent` rule).

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
is the ONE owner of `GameState.RunMode`: **Paused** = `RunMode.Edit`, **Playing** = `RunMode.Play`
(the shell stays composed in both — the transport buttons and the systems panel remain interactive
while the game runs in the inset viewport; the editing tools are Edit-guarded and therefore inert
while Playing). **Restart** returns the world to the state of the ORIGINAL load, in this exact
order: set Paused (nothing simulates over the teardown), `EditorHistory.Clear()` (the recorded
commands reference entities about to die — replaying them in either direction would dangle),
`world.Remove<CurrentLevelComponent>()` + `Remove<CurrentBackgroundColorComponent>()` (the LDtk
parsers subscribe to the component **added** event — a re-publish over a still-set component fires
*Changed* and never re-parses), dispose every scene entity, then invoke the screen-recorded
`Reload` (each screen registers "re-publish my original load request" in `Load`: the game screen
re-publishes `LoadLevelRequest(levelId)`, the menu re-runs its UI builder, the runner re-runs its
create methods). Restart while Playing also lands **Paused**. **Unsaved live edits since the load
are DISCARDED** — the standard play-mode trade-off; Save first to keep them. The survival boundary
is exclusion by editor markers (the engine has no entity↔level association): an entity survives
when it carries `EditorInfrastructureComponent` (every editor-owned entity — chrome, panel rows,
gizmo overlays/proxies, the gizmo-state entity — is tagged at creation), when it is the cursor
pipeline (`CursorControllerComponent`/`CursorInputComponent` — screen input infrastructure, not
scene content), or when the screen's `KeepAlive` predicate names it (system-constructed screen
infrastructure held by reference, e.g. the dialogue UI root via `DialogueStateComponent`) — keeps
propagate DOWN the `ChildOf` chain. A Restart with no recorded `Reload` is a **loud no-op**
(warning, nothing disposed): tearing the world down with no way to rebuild it would strand the
designer on a blank screen.

**Why:** direct user directive — the F1 toggle is retired; "play/pause and restart buttons to play
the game, pause it or reset it" are the way the designer moves between editing and playing, and
restart must be trustworthy: it either fully rebuilds the loaded scene or refuses loudly.
**Breaks:** an uncleared history dangles undo entries against disposed entities (undo after
restart crashes or silently no-ops against the wrong world); a restart that skips the
`CurrentLevelComponent` removal never re-parses (the documented broken-hot-reload path); a sweep
without the editor-marker exclusion disposes the chrome/panel/gizmo state (the editor UI vanishes
on restart); disposing the cursor pipeline kills all mouse input for the session; a silent no-op
restart (or a teardown without reload) strands a blank world.
**Tests:** `MonoDreams.Tests/LevelEditor/EditorTransportTests.cs` (restart disposes scene entities
and re-runs the recorded load; editor infrastructure + cursor + `KeepAlive`-named sub-graphs
survive; unsaved-edit discard demonstrated — edit a transform through the history, restart, the
value is back at the loaded state and undo is a no-op; the world-level components are removed;
restart while Playing lands Paused; a reloadless restart is a loud no-op; the headless
`Play`/`Pause`/`Restart` ops drive the same paths).
**Depends on:** foundation — "Default `RunMode = Play` preserves all existing pipelines" (the
transport is the only mode owner); level-loading — the `LoadLevelRequest` →
`CurrentLevelComponent`-added parse trigger this premise routes around; this file — "The editor run
flag composes the always-on editor and the transport owns RunMode", "Bounded undo with
drag-coalescing" (the history the restart clears).

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
`ToolModalityTest_EscapeRestoresSelectTransform`, `ToolModalityTest_RightClickDisarms`).
**Depends on:** this file — "Selection picks MAX final `LayerDepth` with a selection-owned
tiebreak, target-aware" (the claim rule this composes with).

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

**Why:** top-down walk-behind depends on the sort key and the visual base being the same point;
without the convention every prop needs a hand-tuned `YSortOffset` and a mis-set one reads as the
player clipping through the prop.
**Breaks:** props sort by their top-left corner — the player pops in front of a building while
visually behind it; ghost preview and placed prop disagree about where "under the cursor" is.
**Tests:** `MonoDreams.Tests/LevelEditor/SpritePropFactoryTests.cs`
(`FeetOriginOnYSortedBandTest`, `SpritePropStandardStackTest`, `SlicedEntrySourceRectTest`).
**Depends on:** rendering — `YSortSystem`'s `WorldPosition.Y + YSortOffset` key; this file — "The
serializer persists SOURCE sort fields…" (Origin/YSortOffset are SOURCE fields that round-trip).

## Save is blocked while Playing or when no project root is resolved

`EditorOverlay.DispatchToolbarAction`'s Save case no-ops with a loud `Logger.Warning` in TWO
distinguishable cases (`EditorOverlay.SaveBlock` → `SaveBlockReason.Playing` /
`SaveBlockReason.NoProjectRoot`; `Playing` is checked first): (1) while `RunMode == Play` — saving
is an authoring act over the **paused** scene, and a mid-Play save would bake transient run state (a
mid-air player, in-flight velocities, half-resolved collisions) into the scene file as if it were
authored truth; (2) while the editor's `EditorProjectContext` is unresolved (no `game.mdproj` found —
a shipped/relocated build, a console, or an unset `MONODREAMS_PROJECT_ROOT` with nothing to walk up
to) — there is nowhere versioned to write. The toolbar renders the Save button dimmed for EITHER
cause (the transport rule dims all editing buttons while Playing; a small per-button gate additionally
dims Save while Paused-but-unresolved), and this guard closes the remaining dispatch paths (the
headless `ToolbarAction` op, any programmatic dispatch). The two reasons are reported separately so
the log/toolbar can tell the user WHY. Undo/Redo/Load keep their existing Paused-only toolbar gating;
the transport buttons stay live in both states. (PS3 repoints the actual write from the ephemeral
build-output path to the resolved source tree — see "The editor Save writes versioned `.mdscene` into
the project source tree" — so the `NoProjectRoot` gate is now also what keeps the write target valid.)

**Why:** the authoring-vs-runtime trap (island-authoring plan §9, pulled into Slice 1 because it
is nearly free) AND the project-persistence trap (project-persistence plan §4): a mid-Play save
corrupts a scene, and a save with no resolved project root has nowhere versioned to land — both
fail silently in a way that only shows later.
**Breaks:** a scene reloads with the player embedded mid-jump or props mid-physics — "the level I
saved is not the level I authored"; or Save silently writes to (or crashes over) an ephemeral
build-output path instead of the versioned source tree.
**Tests:** `MonoDreams.Tests/LevelEditor/ToolbarTests.cs`
(`SaveGuardTest_BlockedWhilePlayingOrWithoutAProjectRoot`,
`SaveGuardTest_DispatchNoOpsWhileBlockedAndSavesWhenAllowed`).
**Depends on:** this file — "The editor run flag composes the always-on editor and the transport
owns RunMode" (Playing = `RunMode.Play` with the shell composed); "The project manifest anchors the
editor's project root; unresolved is fail-safe" (the `NoProjectRoot` cause).

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
`LevelLoadRequestSystem`; per request it probes the bundled `Content/Levels/<id>.mdscene` via
`TitleContainer` and, on a hit, publishes a `LoadSceneRequest(rel, fromContent:true)` (returning `true`)
so the **same** `SceneReaderSystem` that serves the editor's Load also serves the game boot — the reader
is thus generalized off the editor-only `LoadSceneRequest`. It runs in **both run modes** and **with no
editor composed**: `LoadLevelExampleGameScreen` reuses the overlay's reader when the editor is present,
else builds a standalone one (engine **and game** serializers — PS5), so a shipped game boots native
scenes too. When no `.mdscene` exists for the id the boot dispatcher **fails loud** — the LDtk/Blender
loaders are import-only (PS5) and not wired to boot, so there is no silent legacy attempt. The bundled
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
`CommittedSampleScene_LoadsBackViaTheNativeReader`), `MonoDreams.Tests/LevelEditor/ManifestBootTests.cs`,
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

## See also

- `docs/CORE_TENETS.md` — "The editor is part of the game" + the interaction matrix.
- `MonoDreams/foundation/docs/premises.md` — the run-state model premises.
- `docs/flows/level-editor.md` — the per-frame flow doc.
- `scene-format.md` — the native MonoDreams scene format spec (the registry fills its `components{}`).
- `docs/level-editor/roadmap.md` — the Wave A–F map + the foundation plug-in points.
