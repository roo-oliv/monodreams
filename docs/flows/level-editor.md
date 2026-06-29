---
flow: level-editor
covers:
  - MonoDreams/level-editor/**
sensitive: true
---

# Level-editor frame: the game pipeline, gated by run state

> **Status: Wave 4 (4a + 4b).** Live today: the run-state gate in `foundation`
> (`GameState.RunMode`, `EditTimeBehavior`, `GatedSystem`); the scene round-trip (Wave 3);
> the Wave-4a interactive substrate — the reference `LevelEditorScreen`
> (`MonoDreams.Examples.Core`), `SelectionSystem` (click-to-pick), `EditorHistory` bounded
> undo/redo with drag-coalescing, the `EditorModeToggleSystem` RunMode flip, and the
> create/delete/transform commands; and the Wave-4b interaction layer — the transform
> **gizmo** (`GizmoSystem` + `GizmoStateComponent` + the pure `GizmoTransform` math) and the
> engine-native **toolbar** (`ToolbarSystem` + `EditorToolbarBuilder` on the HUD target). The
> headless editor-op channel is Wave 5. Anything not yet built is marked **(planned, Wave N)**.
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
   Wave 4b) and the toolbar buttons (on the HUD target, Wave 4b). These are **standalone** —
   never `ChildOfComponent`-parented to a game entity — so `HierarchySystem.DisposeOrphans` (live in
   Edit) cannot cascade-dispose them. The gizmo/outline meshes set `VisibleComponent` themselves
   (`CullingSystem` only visits `SpriteInfoComponent` entities). Selection tags the picked **game**
   entity with `SelectedComponent` (a transient marker, not an overlay entity); the gizmo reads that
   tag to draw the overlay around it.
3. **Transient input entities** — the cursor, positioned by the live `CursorInputSystem` →
   `CursorPositionSystem` pair the editor reads for hit-testing and dragging.

Per frame, in pipeline order (the reference assembly is `LevelEditorScreen`):

1. **Input** (`RunNormally`) — input mapping + `CursorInputSystem` (raw mouse / edge state).
2. **Mode toggle** (`RunNormally`) — `EditorModeToggleSystem` flips `RunMode` in place on the toggle key.
3. **Level / scene load** — `LoadLevelRequest` (LDtk/Blender) + `LoadSceneRequest` (`SceneReaderSystem`).
4. **Game logic / physics / collision** (`Freeze`) — runs in `Play`, skipped in `Edit`.
5. **Editor command systems** (Edit-guarded) — `EditorCommandSystem` (delete/undo/redo → `EditorHistory`).
6. **Gizmo** (Edit-guarded) — `GizmoSystem` reads `SelectedComponent`, hit-tests the active handle, and
   on a drag opens a coalescing transaction and pushes a `TransformEditCommand` per frame (one undo step
   on release). It runs **before** `HierarchySystem` so the edit propagates the same frame, and rebuilds
   the standalone overlay meshes (outline + handle) each frame.
7. **Hierarchy** (`RunNormally`) — `HierarchySystem` propagates the editor's transform edits to
   world space so the preview is correct *this* frame (it must run in both modes).
8. **Camera** — `CameraFollowSystem` (`Freeze`); in `Edit` the editor drives `Camera.Position`/
   `Zoom` directly.
9. **Toolbar** (Edit-guarded) — `ButtonMeshPrepSystem` rebuilds the toolbar button meshes, then
   `ToolbarSystem` hit-tests the cursor's `VirtualPosition` against the button bounds, fires a clicked
   button's `EditorToolbarAction` through the screen's dispatch (Save/Load/Undo/Redo/tool/snap), and
   hides the toolbar in Play.
10. **Cursor projection** (`RunNormally`) — `CursorPositionSystem` after the camera's final move.
11. **Render** (`RunNormally`) — the full draw stack, unchanged in both modes. `SelectionSystem` runs
   at the **end** of the draw pipeline (after `YSortSystem`) so it picks on the final post-Y-sort
   depth this frame; the toolbar draws on the HUD target (screen-space), the gizmo/selection
   overlay on Main (world-space, sized by `1/Camera.Zoom`).

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
  tiebreak (`EditorIdComponent`), read after `YSortSystem` this frame (Wave 4a).
- Undo is bounded (FIFO eviction past the cap) with drag-coalescing (one drag = one entry);
  empty-stack undo/redo is a no-op (Wave 4a). The gizmo drives this: drag-start opens the
  transaction, each frame pushes a `TransformEditCommand`, release commits → one entry (Wave 4b).
- The gizmo applies a quantized (snap-on) or raw (snap-off) world-space transform edit honoring
  `Origin`; the toolbar (on the HUD target, never Main) drives the SAME shared `EditorHistory` /
  `SceneSerializer` / `GizmoStateComponent`, never a second instance (Wave 4b).
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
