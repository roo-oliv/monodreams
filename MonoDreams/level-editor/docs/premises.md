# level-editor — premises

> Technical invariants the engine assumes about the level editor: the editor
> runs as an in-game `Edit` mode over the **real** game pipeline (not a forked
> renderer or a parallel data model), gated by the `foundation` run-state model.
> Read this before changing the editor screen, its overlay entities, or the
> scene save/load path.
>
> **Status: scaffold (Wave 1).** Only the premise this module already stands on
> is live below. The remaining invariants — scene round-trip, overlay-standalone
> + delete-snapshot, bounded undo, selection topmost — land with their code in
> Waves 3–5 and are listed under "Planned premises" so a future session has the
> exact text and the test each must name. No premise here ships `Tests: none yet`.

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

## Planned premises (land with their code in later waves — text + named test pre-committed)

These are **not yet live** (their code does not exist in Wave 1). They are
recorded here so the implementing wave drops in the premise verbatim and wires
the named test in the same PR — honoring the repo rule that no premise ships
`Tests: none yet`.

- **"Scene round-trip reconstructs from registered components, not factories."**
  (Wave 3) Save = the registered components of every `SceneObjectComponent` root
  plus its `ChildOfComponent` closure; load = two passes (create + deserialize via
  the registry, rehydrate `Texture2D` from the asset key, then wire the parent
  graph) on a dedicated `LoadSceneRequest`. **Tests:** `SceneRoundTripGoldenTest`,
  `MembershipFilterTest`, `DerivedDepthReproductionTest`. **Depends on:**
  level-loading — `LoadLevelRequest` is LDtk-coupled.
- **"Editor-overlay entities are standalone; delete snapshots the sub-graph."**
  (Wave 4) Gizmo / selection / toolbar entities are never `ChildOfComponent`-parented
  to game entities, so `HierarchySystem.DisposeOrphans` (live in Edit) cannot
  cascade-dispose them; delete is an undo command that snapshots the disposed
  sub-graph. **Tests:** `HierarchyLiveInEditTest`, `DeleteUndoSnapshotTest`.
  **Depends on:** foundation — "Children are disposed with their parents".
- **"Bounded undo with drag-coalescing."** (Wave 4) Configurable cap with
  oldest-evicted FIFO; one full gizmo drag = exactly one undo step; empty-stack
  undo is a no-op. **Tests:** `UndoBoundedCapTest`, `DragCoalescingTest`.
  **Depends on:** —.
- **"Selection picks MAX final `LayerDepth` with a selection-owned tiebreak."**
  (Wave 4) The selected entity is the one rendered frontmost (MAX post-YSort
  `LayerDepth`) with a deterministic selection-owned tiebreak, because the
  renderer's insertion index is private. **Tests:** `SelectionTopmostTest`,
  `SelectionOrderingTest`. **Depends on:** rendering — final sort key.

## See also

- `docs/CORE_TENETS.md` — "The editor is part of the game" + the interaction matrix.
- `MonoDreams/foundation/docs/premises.md` — the run-state model premises.
- `docs/flows/level-editor.md` — the per-frame flow doc.
