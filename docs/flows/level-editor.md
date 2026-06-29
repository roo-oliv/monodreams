---
flow: level-editor
covers:
  - MonoDreams/level-editor/**
sensitive: true
---

# Level-editor frame: the game pipeline, gated by run state

> **Status: scaffold (Wave 1).** The only part of this flow that exists today is the
> run-state gate in `foundation` (`GameState.RunMode`, `EditTimeBehavior`, `GatedSystem`).
> The editor screen, selection, gizmo, undo, and scene I/O land in Waves 3–5. This doc
> describes the *intended* per-frame flow so a fresh session can implement them against a
> fixed contract; anything not yet built is marked **(planned, Wave N)**.
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
2. **Editor-overlay entities** **(planned, Wave 4)** — the selection highlight, the transform
   gizmo handles, and the toolbar. These are **standalone** — never `ChildOfComponent`-parented
   to a game entity — so `HierarchySystem.DisposeOrphans` (live in Edit) cannot cascade-dispose
   them. Gizmo/selection meshes set `VisibleComponent` themselves.
3. **Transient input entities** — the cursor, positioned by the live `CursorInputSystem` →
   `CursorPositionSystem` pair the editor reads for hit-testing and dragging.

Per frame, in pipeline order (intended; reference editor screen lands in Wave 4):

1. **Input** (`RunNormally`) — input mapping + `CursorInputSystem` (raw mouse / edge state).
2. **Game logic / physics / collision** (`Freeze`) — runs in `Play`, skipped in `Edit`.
3. **Editor systems** **(planned)** — selection, gizmo drag, undo-apply, scene save/load;
   registered always, Edit-guarded so inert in `Play`.
4. **Hierarchy** (`RunNormally`) — `HierarchySystem` propagates the editor's transform edits to
   world space so the preview is correct *this* frame (it must run in both modes).
5. **Camera** — `CameraFollowSystem` (`Freeze`); in `Edit` the editor drives `Camera.Position`/
   `Zoom` directly.
6. **Cursor projection** (`RunNormally`) — `CursorPositionSystem` after the camera's final move.
7. **Render** (`RunNormally`) — the full draw stack, unchanged in both modes; the toolbar draws
   on the UI/HUD target, the gizmo/selection overlay on Main.

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
  `DisposeOrphans` can't reap them; delete snapshots the disposed sub-graph for undo
  **(planned, Wave 4)**.
- Native scenes load via a dedicated `LoadSceneRequest`, never `LoadLevelRequest` (which is
  LDtk-coupled) **(planned, Wave 3)**.

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
- **Overlay reaped** **(planned, Wave 4)** — a gizmo handle `ChildOfComponent`-parented to the
  selected entity is cascade-disposed by the live `DisposeOrphans` when the entity is deleted.
  Overlay entities must be standalone.
- **Scene load clobbered** **(planned, Wave 3)** — loading a native scene through
  `LoadLevelRequest` triggers the unconditional LDtk `Content.Load` + `Remove<CurrentLevelComponent>`.
  Use the dedicated `LoadSceneRequest`.
