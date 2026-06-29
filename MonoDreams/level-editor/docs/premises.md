# level-editor — premises

> Technical invariants the engine assumes about the level editor: the editor
> runs as an in-game `Edit` mode over the **real** game pipeline (not a forked
> renderer or a parallel data model), gated by the `foundation` run-state model.
> Read this before changing the editor screen, its overlay entities, or the
> scene save/load path.
>
> **Status: Wave 4a.** The run-state premise (Wave 1), the three serialization
> premises (Wave 2 — registry opt-in, AssetKey-not-live-texture,
> SOURCE-not-derived sort fields), the scene round-trip premise (Wave 3 —
> membership closure + the `LoadSceneRequest` reader + `Texture2D` rehydration),
> and the Wave-4a interactive-editor invariants — overlay-standalone +
> delete-snapshot, bounded undo with drag-coalescing, and selection topmost —
> are all live below. The remaining "Planned" entry (the headless editor-op
> channel) lands in Wave 5 with its named tests. No premise here ships
> `Tests: none yet`.

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
into a `SceneData` (attaching the active `Camera` state and the `DrawLayerMap` banding), and exports
the JSON through `IPlatformServices.ExportScene` (desktop file / web download). Loading is a
**dedicated `LoadSceneRequest`** message — separate from `LoadLevelRequest` so it never triggers
(or, on failure, clobbers) the LDtk `Content.Load` / `Remove<CurrentLevelComponent>` path —
handled by `SceneReaderSystem` in two passes (create + deserialize each entity's components, then
wire the parent graph from the recorded indices), after which it **rehydrates** each sprite's
`Texture2D` from its `SpriteInfo.AssetKey` via `ContentManager.Load`. The reader **fails loud** on a
component key in the file with no registered serializer (the registry throws; the load aborts with a
clear message rather than silently dropping data). A re-prep + Y-sort frame after load recomputes
`DrawComponent.LayerDepth` identically, because the SOURCE sort fields — not the derived depth — were
persisted.

**Why:** the round-trip must reconstruct from components, not by re-running factories (GAP-A), so
edited state and factory sub-graphs survive; a dedicated load message keeps the native and LDtk load
paths independent; rehydration restores the live GPU texture the JSON cannot carry; failing loud on an
unknown component turns a dropped component into a visible error rather than the missing-entity class of
bug.
**Breaks:** sharing `LoadLevelRequest` would let a native-scene load clobber the LDtk
`CurrentLevelComponent`; serializing from factories would lose edited state; a missing membership
closure would drop a tagged root's children; persisting the derived depth would bake one camera
frame's Y-sort into the file; swallowing an unregistered key would silently lose a designer's data.
**Tests:** `MonoDreams.Tests/LevelEditor/SceneRoundTripTests.cs` (`SceneRoundTripGoldenTest` — tag a
sprite root + a `ChildOf` child, write, reload via `LoadSceneRequest`, assert Transform + `SpriteInfo`
SOURCE sort fields + `AssetKey` + texture rehydration + parent graph + camera/layers reproduce;
`MembershipFilterTest` — only tagged roots + their `ChildOf` closure serialize, transient/untagged
and Blender-style entities excluded; `DerivedDepthReproductionTest` — after reload, a prep + `YSortSystem`
frame recomputes the identical derived `DrawComponent.LayerDepth`).
**Depends on:** level-loading — `LoadLevelRequest` is LDtk-coupled (the asymmetry this premise routes
around); rendering — "Layer depth ownership" (`SpritePrepSystem` → `YSortSystem` re-derive depth each
frame); foundation — the `IPlatformServices` portability seam.

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
`DragCoalescingTest` — a transaction of many pushes commits one entry that one undo reverses whole).
**Depends on:** —.

## Selection picks MAX final `LayerDepth` with a selection-owned tiebreak

Click-to-select picks the **topmost** sprite under the cursor — the one the renderer draws
frontmost, i.e. **MAX final post-Y-sort `DrawComponent.LayerDepth`**. The selection system reads
that depth **after** `YSortSystem` has run this frame (it is ordered at the end of the draw pipeline,
after prep + Y-sort), mirroring `MasterRenderSystem`, which sorts on the same final depth. For an
**exact-depth tie**, selection cannot use the renderer's tiebreak (its per-frame insertion index is
private), so it owns a deterministic one: each candidate gets a stable monotonic `EditorIdComponent`
the first time the selection system sees it (first-seen / creation order), and the larger id — the
later-seen entity, which an undisturbed scene draws last — wins the tie. Hit-testing honors the
sprite's world-space rotation, scale, origin and offset (it inverts the exact draw transform), and a
click on empty space clears the selection. Single-select for Wave A (marquee/multi-select is a later
extension). The system is Edit-guarded (inert in Play).

**Why:** the selected entity must be the one the designer sees on top; matching the render front means
reading the same final depth the renderer sorts on, and the tie must break on a key selection can
observe (the renderer's index can't be).
**Breaks:** picking the back sprite of an overlapping stack (reading source depth, or pre-Y-sort
depth); a non-deterministic / unstable pick on an exact-depth tie; a rotated/scaled sprite mis-picked
because the hit-test ignored its transform.
**Tests:** `MonoDreams.Tests/LevelEditor/SelectionTests.cs` (`SelectionTopmostTest` — stacked sprites
on different depths, click selects MAX final depth, click-empty clears, hit-test honors
rotation/scale/origin; `SelectionOrderingTest` — exact-depth tie resolves by the selection-owned
`EditorId` tiebreak, deterministically).
**Depends on:** rendering — "Layer depth ownership" (`SpritePrepSystem` → `YSortSystem` →
`MasterRenderSystem` derive + sort on final `DrawComponent.LayerDepth`).

## Planned premises (Wave 5 — text + named test pre-committed)

The headless editor-op channel (item 15) lands in Wave 5. Its invariant is recorded here so the
implementing wave drops it in verbatim with the named test, honoring the no-`Tests: none yet` rule.

- **"Injected editor cursor/op state survives the input pass; the op channel holds the session
  open."** (Wave 5) A `SkipHardwareRead` flag on `CursorInputSystem` (mirroring
  `AKeyboardInputHandlingSystem`) lets a test inject `CursorInputComponent` state without the
  hardware read overwriting it; the editor-op channel (`select`/`move`/`save`/`undo` + target +
  coords) holds the replay session open until the op queue drains. **Tests:**
  `HeadlessEditorOpTest` (named, Wave 5). **Depends on:** cursor — `CursorInputSystem`; foundation —
  input replay.

## See also

- `docs/CORE_TENETS.md` — "The editor is part of the game" + the interaction matrix.
- `MonoDreams/foundation/docs/premises.md` — the run-state model premises.
- `docs/flows/level-editor.md` — the per-frame flow doc.
- `scene-format.md` — the native MonoDreams scene format spec (the registry fills its `components{}`).
- `docs/level-editor/roadmap.md` — the Wave A–F map + the foundation plug-in points.
