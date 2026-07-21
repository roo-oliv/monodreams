# deep-plan contract — in-game level editor (foundation + Wave A)

**Branch:** feat/level-editor · **Base:** main · **Plan:** ~/.claude/plans/level-editor-wave-a-foundation.md
**Domains (sensitive):** foundation, platform (+ rendering, camera, cursor, ui, level-loading, new `level-editor`)
**Gate:** PASS — heavy deep-plan pass run; the matrix-cell agent crashed on the per-cell char cap at scale, the 4 analyze slices were harvested, and the contract was then independently refuted (7 findings, all resolved below).
**Residual GAPs:** 0 — GAP-A (serialization model) and GAP-B (Blender save scope) were decided with the user at approval (2026-06-28).

> Scope: Wave A (authoring substrate) + the foundation/roadmap for Waves B–F. Render-specific forks (ground splatmap-vs-stamps, road mesh-vs-stamps, scatter seed granularity) are DEFERRED to their waves — not in this contract.

## Contract

Foundation (spans waves):
1. New module `MonoDreams/level-editor/` (`module.json` + `docs/overview.md` + `docs/premises.md`); module count 13→14 in `MonoDreams/MODULES.md` and `.claude/CLAUDE.md` (module list + module-to-premises row); new `docs/flows/level-editor.md` flow doc.
2. `foundation` gains the run-state model: `GameState.RunMode` (`Play`/`Edit`, default `Play`), an edit-time-behavior policy enum (`RunNormally`/`Freeze`/`RunPartial`/`RuntimeEditable`), and a `GatedSystem<GameState>` decorator honoring policy by reading `RunMode`. Default `Play` + opt-in-only gating leaves every existing screen byte-identical (back-compat, tested).
3. `docs/CORE_TENETS.md` gains a tenet "The editor is part of the game" + the run-state contract; `foundation/docs/premises.md` gains the run-state premise with `Tests:` filled.
4. The native MonoDreams scene format is specified in docs (`version`, `camera`, `layers[]` with depth ranges + ySorted flag, reserved `sources[]` for later waves, `entities[]` with per-entity `components{}` + a `parent` ref).
5. `docs/level-editor/roadmap.md` maps Waves A–F to seams/dependencies/decisions-made-vs-deferred + plug-in points into the foundation.

Wave A:
6. A purpose-built reference editor screen (Examples.Core) composes editor systems over the game pipeline in the SAME world, editor systems pre-registered (inert in `Play`); toggling `RunMode` enters/exits editing without a screen swap (no `Dispose`/`Load`, state preserved). Editor-overlay entities (gizmo/selection/toolbar) are standalone — never `ChildOfComponent`-parented to game entities — so `HierarchySystem.DisposeOrphans` (live in Edit) can't cascade-dispose them; delete is an undo-command that snapshots the disposed sub-graph.
7. `SceneObjectComponent` tags save-root entities; the writer includes each tagged root PLUS its `ChildOfComponent` descendants, so factory sub-graphs round-trip with the parent graph intact. Transient cursor/UI/HUD/gizmo/overlay entities are untagged → excluded. Blender-origin entities untagged in Wave A (GAP-B deferred) → view-only.
8. A component-serializer registry: engine ships `(write,read)` serializers for its serializable components (`Transform`, `SpriteInfo`, colliders, `RigidBody`/`Velocity`, mesh `Draw` params, layer/ySort SOURCE fields, the `ChildOf`/parent link); game code registers serializers for its own components (e.g. `PlayerState`). The writer serializes every registered component of every in-scope entity + `camera` + `layers[]`. `Texture2D` → an additive optional `AssetKey` on `SpriteInfoComponent` (set at creation), never the live texture; SOURCE sort fields, never per-frame-derived `DrawComponent.LayerDepth`. An unregistered component type is skipped with a loud warning. Written through `IPlatformServices` (desktop file; new web download/clipboard member).
9. A scene reader on a dedicated `LoadSceneRequest` message (NOT `LoadLevelRequest` — avoids the unconditional LDtk `Content.Load`/`Remove<CurrentLevelComponent>` clobber). Two passes: create all entities + deserialize each `components{}` via the registry (rehydrating `Texture2D` from the asset key), then wire the `parent` graph; fails loud on an unregistered component type in the file. Round-trip: save→`LoadSceneRequest` reproduces the same entity set (incl. sub-graph children) + all serialized components + parent graph; one prep+YSort frame recomputes `DrawComponent.LayerDepth` identically.
10. The registry is opt-in per component type (only registered types serialize); engine tags/transient state (`VisibleComponent`, cursor) are never written; registration at module/screen init so the in-scope set is explicit.
11. `SelectedComponent` + a selection system: cursor-click hit-tests `CursorInputComponent.WorldPosition` against world-space sprite bounds (honoring `Transform` rotation/scale + `SpriteInfo` `Size`/`Origin`/`Offset`), selecting topmost = MAX final post-YSort `LayerDepth` with a selection-owned deterministic tiebreak (the renderer's insertion index is private); click-empty clears.
12. Transform gizmo components + system: move/rotate/scale handles via existing mesh generators; gizmo/selection meshes set `VisibleComponent` themselves (CullingSystem only visits `SpriteInfoComponent` entities — manual Visible is stable), world-space on Main (optionally `1/Zoom`-scaled); dragging mutates the selected `TransformComponent`; grid-snap toggle quantizes the world-space result (rotate/scale honor `Origin`).
13. An editor-command abstraction + bounded undo/redo history (configurable cap, oldest evicted FIFO) with drag-coalescing (one full drag = exactly one undo step), wired to create/delete/transform; empty-stack undo is a no-op.
14. A minimal engine-native editor toolbar (`AutoLayoutBuilder`) — tool select, save, load, undo, redo, snap toggle — on the UI/HUD render target (not Main, `AutoLayoutBuilder`'s default), web-capable, no ImGui.
15. Headless editor-op testability: a `SkipHardwareRead` flag on `CursorInputSystem` (mirroring `InputMappingSystem`) so injected cursor state isn't overwritten, plus an editor-op channel (`select`/`move`/`save`/`undo` + target + coords) that holds the session open (replay auto-exits on drain); usable from `GameTestRunner`.
16. Every new premise's `Tests:` field names its test; no premise ships `Tests: none yet`.

## Interaction matrix — `GameState.RunMode` × system

| System / group | Policy | `Play` | `Edit` |
|---|---|---|---|
| Input mapping, `CursorInputSystem`, `CursorPositionSystem` | RunNormally | runs | runs |
| Cull/Prep/Sort/Render (`CullingSystem`,`SpritePrepSystem`,`YSortSystem`,`MeshPrepSystem`,`TextPrepSystem`,`MasterRenderSystem`,`FinalDrawSystem`) | RunNormally | runs | runs |
| `HierarchySystem` (local→world + `WorldPosition` cache) | RunNormally / RunPartial | runs | runs (editor edits must propagate) |
| Movement / velocity / physics / collision / `OrbSystem` | Freeze | runs | frozen |
| NPC / dialogue / zone-dialogue | Freeze | runs | frozen |
| `CameraFollowSystem` (own `IsEnabled`; gate composes) | Freeze | runs | frozen (editor drives `Camera.Position/Zoom`) |
| `TransformVelocity`/`TransformCommit` (physics Delta→commit) | Freeze (RunPartial if reused) | runs | frozen (editor sets `Position` directly + Hierarchy) |
| Editor systems (selection, gizmo, undo-apply, scene save/load) | RunNormally (Edit-guarded) | inert | runs |
| Editor toolbar UI (UI/HUD target) | RunNormally | hidden | active |

## Derived-value (dimension) table — load-bearing non-monetary quantities

| Value | Base / unit | Invariant | Seam |
|---|---|---|---|
| `DrawComponent.LayerDepth` | y-sort depth-band slot | clamped `[minDepth,maxDepth]` | `YSortSystem.cs:54` (exists) |
| Persisted sort fields | SOURCE `SpriteInfo` (`LayerDepth`+`YSortOffset`+`YSortDepthBias`) | SOURCE not derived | scene writer (item 8) — resolves the "bakes one camera frame" risk |
| Selection topmost key | final post-YSort `LayerDepth`, selection-owned tiebreak | == render front (MAX) | SelectionSystem (item 11) — resolves the "picks the back sprite" risk |
| Sprite hit-test AABB | world-space `Transform`×`SpriteInfo` | honor rotation/scale/origin/offset | SelectionSystem (item 11) |
| Undo history depth | ring-buffer entry count | `<= cap`, oldest evicted | undo push seam (item 13) |
| Drag-coalesce count | undo-steps-per-drag | `== 1` per full drag | gizmo drag-end commit (item 13) — resolves "N steps per drag" |
| Grid-snap quantum | world units → grid step | applied world-space, honor origin | GizmoSystem (item 12) |
| `RunMode` gating | policy × `RunMode` → run/skip | default `Play` ⇒ pipelines unchanged | `GatedSystem.Update` (item 2) |
| Scene membership set | tagged roots + `ChildOf` descendants | only `[With(SceneObjectComponent)]` closure | writer (items 7/8) |
| `Texture2D` identity | asset key string | round-trips via `content.Load` | `SpriteInfo.AssetKey` (item 8) |

## Precondition diff

| Predicate | Before | After |
|---|---|---|
| `GameState` carries run-mode | no | yes (`RunMode` default `Play`) — back-compatible, opt-in gating |
| Game-logic runs every Update | always | only when `RunMode==Play` if wrapped in a `Freeze` gate; ungated unchanged |
| Hierarchy/world-cache while editing | n/a | must (RunNormally/RunPartial in Edit) so edits render |
| Entity scene membership | none | tagged `SceneObjectComponent` roots + `ChildOf` closure |
| Save path | none (load-only) | component-serializer registry + scene writer (SOURCE fields + asset key, not derived depth / live texture) |
| `Texture2D` serialization | live ref only | `AssetKey` on `SpriteInfoComponent`, key written, `content.Load` rehydrates |
| Native scene load dispatch | LDtk unconditional + Blender prefix on `LoadLevelRequest` | dedicated `LoadSceneRequest` message + native reader — never clobbered |
| Unregistered component on load | n/a | fails loud (vs `EntitySpawnSystem.cs:54` silent factory-id drop) |
| Editor chrome render target | n/a | UI/HUD (not `AutoLayoutBuilder`'s Main default) |
| Web file output | `WriteAllText` only (desktop) | new `IPlatformServices` download/clipboard member |
| Concurrent game + editor in one world | unsupported | supported via same-world overlay + run-mode gating (no screen swap) |

## Premises (new; `Tests:` filled)

- **foundation — "Default RunMode=Play preserves all existing pipelines."** A system not wrapped in a `Freeze` gate, or any screen that never sets `Edit`, behaves exactly as before. Why: back-compat across 13 modules. Breaks: a screen silently freezes. Tests: `GatingBackCompatTest`. Depends on: —.
- **foundation — "Edit-time behavior is a per-system policy honored by GatedSystem."** render/input/cursor + Hierarchy `RunNormally`; physics/AI/camera-follow `Freeze`; editor systems `RunNormally`+Edit-guarded. Why: cornerstone C2. Breaks: black screen (render frozen) or physics moving in Edit. Tests: `RunStateGatingTest`. Depends on: rendering — culling/prep/sort ordering.
- **level-editor — "Scene round-trip reconstructs from registered components, not factories."** Save = registered components of tagged roots + `ChildOf` closure; load = two-pass create+deserialize+wire-parents on `LoadSceneRequest`. Why: factory sub-graphs + edited state must survive. Breaks: child loss / count mismatch / clobbered by LDtk load. Tests: `SceneRoundTripGoldenTest`, `MembershipFilterTest`, `DerivedDepthReproductionTest`. Depends on: level-loading — `LoadLevelRequest` is LDtk-coupled.
- **level-editor — "Editor-overlay entities are standalone; delete snapshots the sub-graph."** Why: `HierarchySystem.DisposeOrphans` runs in Edit. Breaks: gizmo/selection entities cascade-disposed; un-undoable delete. Tests: `HierarchyLiveInEditTest`, `DeleteUndoSnapshotTest`. Depends on: foundation — hierarchy orphan dispose.
- **level-editor — "Bounded undo with drag-coalescing."** cap enforced (oldest evicted), one drag = one step, empty-stack no-op. Tests: `UndoBoundedCapTest`, `DragCoalescingTest`. Depends on: —.
- **level-editor — "Selection picks MAX final LayerDepth with a selection-owned tiebreak."** Why: must match the render front; the renderer's insertion index is private. Tests: `SelectionTopmostTest`, `SelectionOrderingTest`. Depends on: rendering — final sort key.

## Resolved refutations (independent refuter)
BLOCKER 1 (factory sub-graphs) → full component serialization + `ChildOf` closure (items 7–9). BLOCKER 2 (no asset key) → `SpriteInfo.AssetKey` (item 8). BLOCKER 3 (overlay/HierarchyDispose) → pre-registered editor screen + standalone overlay entities + delete-undo snapshot (item 6). HIGH 4 (load clobber) → dedicated `LoadSceneRequest` (item 9). HIGH 5 (gizmo visibility) → manual `VisibleComponent` (item 12). HIGH 6 (headless) → `CursorInputSystem.SkipHardwareRead` + session-holding op channel (item 15). MEDIUM 7 (tiebreak) → selection-owned deterministic tiebreak (item 11).
