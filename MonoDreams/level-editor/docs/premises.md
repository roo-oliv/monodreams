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
> load and discards unsaved edits). No premise here ships `Tests: none yet`.

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
clears, and a click on another sprite away from every handle still re-picks.

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
gizmo drags a HUD-target entity in virtual space and its overlays follow the entity's target);
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
standalone (never `ChildOf`-parented) and set `VisibleComponent` themselves, drawing world-space on
Main with handle sizes scaled by `1/Camera.Zoom` for constant on-screen size.

**Why:** the contract's derived-value rows "grid-snap quantum applied world-space, honor origin" and
the editor-overlay-standalone rule; a designer dragging with snap on must land on grid lines, and a
rotate/scale must spin/grow about the entity's pivot rather than translate it.
**Breaks:** a snap-off drag that quantizes (or vice versa) surprises the designer; a rotate/scale that
moves the entity (pivoting about the wrong point) or that mutates `Origin`; an overlay parented to the
selected entity gets cascade-disposed on delete; a mesh overlay with no self-`VisibleComponent` never
renders.
**Tests:** `MonoDreams.Tests/LevelEditor/GizmoTests.cs` (`GizmoTransformSnapTest` — move/rotate/scale
with snap off = raw delta and snap on = quantized; rotate and scale preserve `Origin` and pivot about
the world pivot).
**Depends on:** rendering — `MeshPrepSystem` / `MasterRenderSystem` render a mesh `DrawComponent` on
Main through the camera; foundation — `HierarchySystem.DisposeOrphans` (why overlays are standalone).

## The editor toolbar's buttons drive the same shared editor instances; the chrome is native-resolution on the Editor target, always on while the editor is composed

The engine-native toolbar (the engine's `SimpleButtonComponent` / `ButtonMeshPrepSystem` /
`DynamicTextComponent` primitives, no ImGui) lives on the **Editor** render target — a target at
native window resolution composited 1:1 over the whole window (never Main, never the virtual-res
HUD) — inside the shell's top bar, with buttons sized in physical pixels and each carrying a
`ToolbarButtonComponent` binding a click to an `EditorToolbarAction`. `ToolbarSystem` hit-tests
the cursor's raw `ScreenPosition` (hardware pixels — the chrome sits in the margins where the
virtual mapping is null and `VirtualPosition` is frozen) against the button `Bounds` and hands
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

**Why:** the Wave-7 user directives — "the game screen … rendered in the center … the editor
tools … placed around it, just like in Blender" and "highres and readable, independent from the
game resolution or fonts". Keeping the inset on the `ViewportManager` is what makes the mouse
mapping follow the smaller game viewport automatically.
**Breaks:** an inset applied only to compositing (not mouse mapping) desyncs every world pick by
the margin offsets; chrome on the virtual-resolution HUD is upscaled and blurry again; a leaked
inset (no dispose restore) squeezes the next screen into a corner; `VisibleComponent` on chrome
double-offsets the panel meshes.
**Tests:** `MonoDreams.Tests/Rendering/ViewportInsetTests.cs` (inset math centered/aspect-correct,
zero-inset = legacy letterbox byte-identical, set+clear restores, resize recomputes, mouse maps
inside / nulls in margins, pixel-perfect uses the available area);
`MonoDreams.Tests/LevelEditor/EditorShellTests.cs` (panels cover exactly the inset margins,
native-pixel button bounds, relayout on resize, the shell stays composed while Playing + dispose
restore, `OutsideViewport` press never picks).
**Depends on:** rendering — "The viewport inset moves compositing and mouse mapping together",
"Three render targets, two behaviors"; cursor — `CursorPositionSystem` sets `OutsideViewport`;
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
Wave-8b mechanism: when the selected entity carries collider components in Edit, `ProxySyncSystem`
materializes one **standalone proxy entity per collider** — `GizmoProxyComponent` is the pure-data
binding descriptor `(target entity, ProxyBindingKind, reserved index)`; the proxy carries a
`TransformComponent` kept at the shape's world centre, a cyan outline mesh (vertices baked
relative to that transform, `1/Zoom`-thick, on Main at depth 0.998) and a self-set
`VisibleComponent` — re-derived from the bound component **every frame** (cheap: selected entity
only) and despawned on deselect / mode exit / target death. Proxies join the **same** pick
(`SelectionSystem` folds them in through the same rank+depth+id ordering, hit-testing only the
shape's **border** within `8px/Zoom` so a sprite-covering collider never shadows its entity) and
the **same** gizmo drag (move handle at the proxy pivot; the tool is forced to Move for proxies).
The write-back never touches the proxy's own transform: each drag frame pushes a
`ColliderEditCommand` (before/after snapshot of `Bounds` or `ModelVertices`) against the **bound
game entity**, inside the coalescing transaction — one drag = one undo step — and the convex
write-back refreshes `WorldVertices` + `BroadPhaseAABB` in the same command (physics is frozen in
Edit; nothing else would). `ProxySyncSystem` also refreshes the selected entity's convex
`WorldVertices` per frame so the `ColliderDebugSystem` outline (which coexists as the global,
selection-unaware diagnostic) tracks edits instead of drifting. The binding kind is the
generalization seam: a future spline-control-point binding (the road tool, Waves D/F) or a
per-vertex convex handle is another `ProxyBindingKind` + a `ProxyGeometry` derivation case + a
write-back case — never a second proxy mechanism.

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
pure inverse-transform delta math).
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

## See also

- `docs/CORE_TENETS.md` — "The editor is part of the game" + the interaction matrix.
- `MonoDreams/foundation/docs/premises.md` — the run-state model premises.
- `docs/flows/level-editor.md` — the per-frame flow doc.
- `scene-format.md` — the native MonoDreams scene format spec (the registry fills its `components{}`).
- `docs/level-editor/roadmap.md` — the Wave A–F map + the foundation plug-in points.
