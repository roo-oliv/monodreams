---
flow: level-editor
covers:
  - MonoDreams/level-editor/**
sensitive: true
---

# Level-editor frame: the game pipeline, gated by run state

> **Status: Wave 8a (universal overlay + systems panel).** Live today:
> the run-state gate in `foundation` (`GameState.RunMode`, `EditTimeBehavior`, `GatedSystem`);
> the scene round-trip (Wave 3); the Wave-4a interactive substrate — `SelectionSystem`
> (click-to-pick), `EditorHistory` bounded undo/redo with drag-coalescing, the
> `EditorModeToggleSystem` RunMode flip, and the create/delete/transform commands; the Wave-4b
> interaction layer — the transform **gizmo** (`GizmoSystem` + `GizmoStateComponent` + the pure
> `GizmoTransform` math) and the engine-native **toolbar** (`ToolbarSystem`); the Wave-5
> **headless editor-op channel**; post-Wave-A **camera navigation** (`CameraNavSystem`); the
> Wave-6 **composition seam** (`Composition/`): `EditorPipelineRegistrar` (named, gate-wrapped,
> runtime-toggleable pipeline entries), `EditorOverlay` (the whole editor block as reusable
> hooks), and `EditorRunFlag` (`--editor` / `MONODREAMS_EDITOR=1` — game screens compose the
> overlay and boot in Edit); and the Wave-7 **Blender-style shell** — in Edit the game
> composite is inset into a centered viewport (`ViewportManager.SetViewportInset`) with the
> chrome (top toolbar bar, right panel strip, bottom strip — `EditorChromeBuilder` /
> `EditorChromeLayout`) rendered at **native window resolution** around it
> (`RenderTargetID.Editor` + `EditorChromeRenderSystem` + `RenderLayer.Native`), synced by
> `EditorShellSystem`; and the Wave-8a **universal overlay + systems panel** — under the run flag
> EVERY Examples screen (menu + infinite runner included) composes the overlay through the
> registrar (the overlay supplies its own cursor pipeline to a cursor-less screen), selection and
> the gizmo are **target-aware** (UI/HUD-target entities hit-test/drag in virtual space; across
> targets the composite order wins), and the **systems panel** (`SystemsPanelSystem` +
> `SystemsPanelLayout`) in the right strip renders the registrar **tree** (update + draw, in order,
> name + policy + checkbox; registrar groups — `AddGroup` with named, auto-prefixed children —
> render indented above their children with **tri-state** checkboxes: all/none/mixed, the mixed
> state drawn as the Gmail/Material minus bar) and toggles any row live via `SetEnabled`
> (group click = Gmail cascade: checked/indeterminate → all off, unchecked → all on); and the Wave-8b **collider
> gizmo proxies** — colliders are component-local spatial data, not entities, so in Edit
> `ProxySyncSystem` (entry `editor.proxySync`) materializes standalone, per-frame-re-derived
> proxy entities (`GizmoProxyComponent` bindings) over the selected entity's collider shapes;
> they join the same selection pick (border-only hit-test) and the same gizmo drag (move tool),
> writing back into the bound component via `ColliderEditCommand` through the coalescing undo
> transaction; and the post-Wave-8b **cross-host wiring** — the universal overlay now spans
> hosts: every Demos screen (the launcher + the four module demo screens) composes it under the
> same run flag, pairing the overlay with the engine's `DefaultEditorKeys` default keyboard
> surface (for hosts without their own action mapping; the Demos host honours the flag under
> `--headless`, so the shell lands in the captured self-verification frames). The step-by-step
> recipe any new host/screen follows is `MonoDreams/level-editor/docs/overview.md` § "Adding
> the editor to a screen/host". Anything not yet built is marked **(planned, Wave N)**.
>
> Marked **sensitive** because the flow leans on the `foundation` run-state contract: a
> single wrong policy (render frozen in Edit, or physics left live) silently breaks either
> the preview or the editing, with no crash. The interaction matrix in
> `docs/CORE_TENETS.md` ("The editor is part of the game") is binding.

The level editor does not run a pipeline of its own — it runs the **game's** pipeline with
a run-state gate in front of each system. `GameState.RunMode` (default `Play`) is flipped to
`Edit` to enter editing **without a screen swap** (no `Dispose`/`Load`, so all in-world state
is preserved), and back to `Play` to resume. The editor previews exactly what the player sees
because it reuses the same world, the same `Camera`, and the same
`CullingSystem → SpritePrepSystem → YSortSystem → MeshPrepSystem → TextPrepSystem →
MasterRenderSystem` draw stack — there is no second renderer and no second scene model.

The mechanism is `GatedSystem`: a screen wraps each game system in
`GatedSystem(child, policy)` where `policy` is an `EditTimeBehavior`. Each frame the gate
reads `state.RunMode` and forwards to the child only if the policy admits it
(`Freeze` ⇒ Play only; `RunNormally`/`RunPartial`/`RuntimeEditable` ⇒ both, with the latter
two reserved for finer later-wave semantics). The gate also honors its own `IsEnabled` and
forwards `Dispose`, so a gated `CameraFollowSystem` can still be toggled independently. Because
the default mode is `Play` and gating is **opt-in**, any screen that never wraps a system or
never sets `Edit` is byte-identical to before the model existed.

## Entities & lifecycle

In `Edit`, three kinds of entities coexist in one world:

1. **Game entities** — the scene being edited. Their game-logic / physics / camera-follow
   systems are `Freeze`-gated, so they hold still; only the editor (and `HierarchySystem`)
   moves them.
2. **Editor-overlay entities** — the selection highlight + the transform gizmo handles (the
   `GizmoSystem`'s outline + active-tool handle mesh entities, tagged `GizmoOverlayComponent`,
   Wave 4b), the shell chrome — panel backgrounds + toolbar buttons on the native-resolution
   `Editor` target, laid out in physical pixels (Wave 7) — and the collider gizmo proxies (cyan
   outline entities tagged `GizmoProxyComponent`, one per collider on the selected entity,
   Wave 8b). These are **standalone** —
   never `ChildOfComponent`-parented to a game entity — so `HierarchySystem.DisposeOrphans` (live in
   Edit) cannot cascade-dispose them. The gizmo/outline/proxy meshes set `VisibleComponent` themselves
   (`CullingSystem` only visits `SpriteInfoComponent` entities). Selection tags the picked **game**
   entity with `SelectedComponent` (a transient marker, not an overlay entity); the gizmo reads that
   tag to draw the overlay around it. A proxy is the one overlay entity that IS selectable — clicking
   its border tags it `SelectedComponent` so the gizmo can drag it — but a drag writes back into the
   bound game entity's collider component, never into the proxy.
3. **Transient input entities** — the cursor, positioned by the live `CursorInputSystem` →
   `CursorPositionSystem` pair the editor reads for hit-testing and dragging.

Per frame, in pipeline order (the reference assembly is the shared composition in
`LoadLevelExampleGameScreen` behind its `editorEnabled` flag, built through the
`EditorPipelineRegistrar` and the `EditorOverlay` hooks; `LevelEditorScreen` is that screen with
the flag pinned on, and the `--editor` run flag turns it on for **every** registered screen —
`LevelSelectionScreen` and `InfiniteRunnerScreen` weave the same hooks with their own per-screen
policies, the runner's overlay providing its own cursor pipeline):

1. **Input** (`RunNormally`) — input mapping + `CursorInputSystem` (raw mouse / edge state).
2. **Mode toggle** (`RunNormally`) — `EditorModeToggleSystem` flips `RunMode` in place on the toggle key.
3. **Level / scene load** — `LoadLevelRequest` (LDtk/Blender) + `LoadSceneRequest` (`SceneReaderSystem`).
4. **Game logic / physics / collision** (`Freeze`) — runs in `Play`, skipped in `Edit`.
5. **Editor command systems** (Edit-guarded) — `EditorCommandSystem` (delete/undo/redo → `EditorHistory`).
6. **Gizmo** (Edit-guarded) — `GizmoSystem` reads `SelectedComponent`, hit-tests the active handle, and
   on a drag opens a coalescing transaction and pushes a `TransformEditCommand` per frame (one undo step
   on release). It runs **before** `HierarchySystem` so the edit propagates the same frame, and rebuilds
   the standalone overlay meshes (outline + handle) each frame. When the selected entity is a collider
   proxy the tool is forced to Move and each drag frame pushes a `ColliderEditCommand` against the
   proxy's bound game entity instead (Wave 8b).
7. **Proxy sync** (Edit-guarded, Wave 8b) — `ProxySyncSystem` spawns/places/despawns the collider
   proxies for the selected entity, re-deriving each from its bound component (so it tracks both this
   frame's gizmo write-back and owner moves), and refreshes the selected entity's convex
   `WorldVertices` (physics is frozen — the debug outline would otherwise go stale).
8. **Hierarchy** (`RunNormally`) — `HierarchySystem` propagates the editor's transform edits to
   world space so the preview is correct *this* frame (it must run in both modes).
9. **Camera** — `CameraFollowSystem` (`Freeze`); in `Edit` the editor drives `Camera.Position`/
   `Zoom` directly.
10. **Toolbar + systems panel** (Edit-guarded) — `ButtonMeshPrepSystem` rebuilds the chrome meshes,
   then `ToolbarSystem` hit-tests the cursor's raw `ScreenPosition` (physical pixels — the chrome
   is native-resolution) against the button bounds and fires a clicked button's
   `EditorToolbarAction` through the screen's dispatch (Save/Load/Undo/Redo/tool/snap) — the two
   are the `editor.toolbar` group's children (`meshPrep`, `clicks`); then `SystemsPanelSystem`
   (the right strip) renders the registrar tree of both pipelines — groups indented above their
   children, name + policy + checkbox, tri-state on groups (all/none/mixed; mixed = the minus
   bar) — hit-tests `ScreenPosition`, scrolls on the wheel, and flips a clicked row via
   `EditorPipelineRegistrar.SetEnabled` (leaf = a both-modes master switch; group = the Gmail
   cascade over its descendant leaves; the panel refuses to disable its own entry or any
   ancestor group of it).
11. **Cursor projection** (`RunNormally`) — `CursorPositionSystem` after the camera's final move;
   it also flags `CursorInputComponent.OutsideViewport` when the pointer is in the chrome
   margins, which mutes selection picks, gizmo drag-starts, and camera-nav zoom/pan there.
12. **Shell sync** (`RunNormally`, after `CursorDrawPrepSystem`) — `EditorShellSystem` makes the
   viewport inset, the chrome layout (relayout on window resize), and the cursor swap (OS
   pointer shown + game cursor sprite hidden in Edit; both reverted in Play) track `RunMode`;
   its `Dispose` clears the inset + re-hides the OS pointer so a screen swap never leaks the shell.
13. **Render** (`RunNormally`) — the full draw stack, unchanged in both modes. `SelectionSystem` runs
   at the **end** of the draw prep (after `YSortSystem`) so it picks on the final post-Y-sort
   depth this frame; the gizmo/selection overlay draws on Main (world-space, sized by
   `1/Camera.Zoom`); the chrome renders through `EditorChromeRenderSystem` (a screen-space
   `MasterRenderSystem` pass over `RenderTargetID.Editor` into a native-resolution target,
   Edit-only) and `RenderLayer.Native` composites it 1:1 over the whole window, above the game
   layers (it resolves to null and is skipped in Play).

## Invariants

Authoritative list in [`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md)
and the run-state premises in [`MonoDreams/foundation/docs/premises.md`](../../MonoDreams/foundation/docs/premises.md);
the ones this flow leans on:

- The editor reuses the real pipeline; Edit behaviour is produced by gating, never by a forked
  renderer or a second scene model.
- Render / input / cursor and `HierarchySystem` are `RunNormally` (live in both modes); game
  logic / physics / camera-follow are `Freeze` (Play only). Get one wrong → black screen or
  entities drifting while editing.
- Default `RunMode = Play` + opt-in gating ⇒ existing screens unchanged.
- Editor-overlay entities are standalone (no `ChildOfComponent`) so the live
  `DisposeOrphans` can't reap them; the gizmo/outline meshes self-set `VisibleComponent`; delete
  snapshots the disposed sub-graph for undo (Wave 4a — `DeleteEntityCommand`).
- Selection picks MAX final post-Y-sort `DrawComponent.LayerDepth` with a selection-owned
  tiebreak (`EditorIdComponent`), read after `YSortSystem` this frame (Wave 4a) — target-aware
  since Wave 8a: UI/HUD-target sprites hit-test `VirtualPosition` (their transforms are virtual
  coordinates), the composite order ranks across targets (UI/HUD above Main), and the gizmo drags
  such an entity in virtual space with its overlays on the entity's own target.
- Undo is bounded (FIFO eviction past the cap) with drag-coalescing (one drag = one entry);
  empty-stack undo/redo is a no-op (Wave 4a). The gizmo drives this: drag-start opens the
  transaction, each frame pushes a `TransformEditCommand`, release commits → one entry (Wave 4b).
- The gizmo applies a quantized (snap-on) or raw (snap-off) world-space transform edit honoring
  `Origin`; the toolbar (on the native-resolution Editor target, never Main) drives the SAME
  shared `EditorHistory` / `SceneSerializer` / `GizmoStateComponent`, never a second instance
  (Wave 4b, retargeted Wave 7).
- Collider shapes are edited through standalone gizmo proxies bound by `GizmoProxyComponent`;
  the drag writes back into the bound game entity's component (`ColliderEditCommand`, one undo
  step per drag, convex writes refresh `WorldVertices`/`BroadPhaseAABB`), never into the
  transient proxy; proxies hit-test their border only (Wave 8b).
- The shell's viewport inset lives on the `ViewportManager` (single source of truth): FinalDraw
  compositing and `ScaleMouseToVirtualCoordinates` follow the same rectangle, so world picking
  needs no extra math and margin clicks map to null (chrome hit-tests `ScreenPosition`); zero
  inset = the historical full-window letterbox, byte-identical (Wave 7).
- Native scenes load via a dedicated `LoadSceneRequest`, never `LoadLevelRequest` (which is
  LDtk-coupled) (Wave 3).

## Load-bearing quantities

- `GameState.RunMode` — `Play` | `Edit`, default `Play`. The single input the gate reads;
  `GatedSystem.ShouldRun(policy, mode)` is the pure decision table.
- `EditTimeBehavior` per system — `RunNormally` / `Freeze` / `RunPartial` / `RuntimeEditable`.
  In Wave 1 only `RunNormally` and `Freeze` differ in effect; the other two are reserved and
  behave as `RunNormally`.

## Failure modes

- **Render frozen in Edit** — wrapping the draw stack in `Freeze` gives a black screen the
  moment the designer enters Edit. Render must be `RunNormally`.
- **Physics live in Edit** — game systems left ungated (or `RunNormally` by mistake) keep
  applying gravity/velocity while the designer places entities; objects fall out from under
  the gizmo. Game logic must be `Freeze`.
- **Hierarchy frozen in Edit** — `HierarchySystem` `Freeze`-gated means an editor transform
  edit never reaches world space, so the preview shows the entity at its old position. It must
  be `RunNormally`.
- **Overlay reaped** (guarded Wave 4a; the overlay it protects is Wave 4b) — a gizmo handle
  `ChildOfComponent`-parented to the selected entity would be cascade-disposed by the live
  `DisposeOrphans` when the entity is deleted. Overlay entities must be standalone; delete is the
  reversible `DeleteEntityCommand`, never a bare `entity.Dispose()`.
- **Scene load clobbered** (Wave 3) — loading a native scene through
  `LoadLevelRequest` triggers the unconditional LDtk `Content.Load` + `Remove<CurrentLevelComponent>`.
  Use the dedicated `LoadSceneRequest`.
- **Shell leaked onto the next screen** (Wave 7) — the `ViewportManager` and the host `Game`
  outlive a screen; swapping screens while in Edit without `EditorShellSystem.Dispose` running
  leaves the menu inset into a corner with a visible OS pointer. The dispose restore is the guard.
- **Chrome hit-test in virtual coordinates** (Wave 7) — the chrome sits in the margins where the
  virtual mapping is null and `VirtualPosition` is frozen; hit-testing it (the pre-Wave-7 toolbar
  behavior) makes buttons dead or misfiring. Chrome hit-tests raw `ScreenPosition`; world systems
  gate on `CursorInputComponent.OutsideViewport` instead.
- **Chrome carrying `VisibleComponent`** (Wave 7) — it would pull the mesh chrome into
  `MeshPrepSystem`'s query, which overwrites `DrawComponent.WorldMatrix` and double-offsets
  meshes whose vertices are baked at absolute pixel positions. Chrome entities carry no
  `VisibleComponent` (only the Main pass consults it).
- **Undo recorded against a proxy** (Wave 8b) — a `TransformEditCommand` targeting the proxy
  entity dangles the moment the proxy despawns (deselect/mode exit) and never moves the collider.
  Proxy drags must push `ColliderEditCommand` against the bound game entity. Symmetrically, a
  convex write-back that skips `UpdateWorldVertices` leaves a stale `BroadPhaseAABB` — contacts
  are silently missed after returning to Play (the collision premise).
