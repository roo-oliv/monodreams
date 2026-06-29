# level-editor — overview

The in-game level editor: an **Edit run mode** layered over the real game pipeline. The editor is not a separate application or renderer — it is a mode of the game (see `docs/CORE_TENETS.md`, "The editor is part of the game"). In `Edit`, the same world, the same `Camera`, and the same `CullingSystem → SpritePrepSystem → YSortSystem → MasterRenderSystem` draw stack the player sees stay live; game logic, physics, and camera-follow freeze; and editor tooling (selection, gizmos, undo, scene save/load, toolbar) runs on top.

> **Status: Wave 4 (4a + 4b).** Wave 1 shipped the run-state model (in `foundation`) and the docs that codify it. Wave 2 added the **native scene format** + the **component-serializer registry** (under `Serialization/`) and an additive `AssetKey` on `SpriteInfoComponent`. Wave 3 added the **scene round-trip** (`SceneObjectComponent`, `SceneWriter`, `LoadSceneRequest` + `SceneReaderSystem`, `IPlatformServices.ExportScene`). Wave 4a added the **interactive-editor substrate**: the reference `LevelEditorScreen` (in `MonoDreams.Examples.Core`) that composes the gated game pipeline + editor systems in one world with an in-place `RunMode` toggle; `SelectedComponent` + `EditorIdComponent` + `SelectionSystem` (click-to-pick the frontmost sprite); the `EditorHistory` bounded undo/redo with drag-coalescing + the `IEditorCommand` abstraction and the create/delete/transform commands; and the `EditorModeToggleSystem` / `EditorCommandSystem`. Wave 4b adds the **transform gizmo** (`GizmoSystem` + `GizmoStateComponent` / `GizmoOverlayComponent` + the pure `Transform/GizmoTransform` math — move/rotate/scale handles, a selection outline, grid-snap, a drag coalesced into one undo step) and the **engine-native toolbar** (`ToolbarSystem` + `Component/ToolbarButtonComponent` + `UI/EditorToolbarBuilder` — tool-select/Save/Load/Undo/Redo/snap on the HUD target). The headless editor-op channel is Wave 5. Anything not yet built is marked **(planned, Wave N)**.

## Purpose

A game built on MonoDreams should be editable *inside the running game*, previewing exactly what the player sees rather than approximating it in a bespoke editor renderer. The editor achieves this by gating the game pipeline on an engine-wide run state instead of forking it: each game system is wrapped (opt-in) in a `GatedSystem` carrying an `EditTimeBehavior` policy, and flipping `GameState.RunMode` from `Play` to `Edit` enters/exits editing with no screen swap, preserving all in-world state.

## What ships

### Run-state foundation (ships in `foundation`, Wave 1)

The editor's cornerstone is not in this module — it is in `foundation`, because every screen (editor or not) must understand it:

- `RunMode` enum (`Play` / `Edit`) + `GameState.RunMode` property (default `Play`).
- `EditTimeBehavior` enum (`RunNormally` / `Freeze` / `RunPartial` / `RuntimeEditable`).
- `GatedSystem` — an `ISystem<GameState>` decorator that wraps a child system + a policy and runs the child only when the run mode admits it.

See `MonoDreams/foundation/docs/premises.md` ("Edit-time behavior is a per-system policy honored by `GatedSystem`" and "Default `RunMode = Play` preserves all existing pipelines").

### Serialization (Wave 2, live — under `Serialization/`)

The scene-persistence substrate. It is infrastructure (services), not components — ECS purity keeps components pure data and puts the read/write behaviour in the registry/serializers.

- `SceneData` / `SceneEntityData` / `SceneCameraData` / `SceneLayerData` — the in-memory model of a native scene (1:1 with `scene-format.md`): `version`, `camera`, `layers[]`, a reserved `sources[]`, and `entities[]` (each a `components{}` map + an optional `parent` index).
- `ComponentSerializer` — a `(write, read)` pair for one component `Type`, keyed by a stable string.
- `ComponentSerializerRegistry` — the opt-in registry (`Type` → serializer). Discovers every component on an entity via DefaultEcs `ReadAllComponents`, writes the registered ones, and emits a `Logger.Warning` for any unregistered one (skip, never silent drop). Loading an unregistered component key throws (fail loud).
- `EngineComponentSerializers.RegisterEngineComponents` — registers the engine's serializers (`Transform`, `SpriteInfo`, `EntityInfo`, the colliders, `RigidBody`, `Velocity`, the `ChildOf` parent link). Centralized here because this module already depends on the modules those components live in.
- `SceneSerializer` — the in-memory round-trip seam: serializes a set of entities to `SceneData` (preserving the `ChildOf` parent graph as indices) and reconstructs them in two passes (create + deserialize, then wire parents). Wave 3 layers JSON file I/O + `LoadSceneRequest` + `Texture2D` rehydration on top.

### Components

- `SceneObjectComponent` (Wave 3, live, under `Component/`) — a pure tag marking a **save-root** entity; the `SceneWriter` includes each tagged root plus its `ChildOfComponent` descendant closure.
- `SelectedComponent` (Wave 4a, live, under `Component/`) — a pure tag marking the currently-selected entity for the gizmo and toolbar (single-select; transient, not serialized).
- `EditorIdComponent` (Wave 4a, live, under `Component/`) — a stable monotonic id the selection system assigns to each candidate the first time it sees it; the selection-owned tiebreak for an exact-depth pick (the renderer's insertion index is private).
- `GizmoStateComponent` (Wave 4b, live, under `Component/`) — the gizmo's configuration (active `GizmoTool`, `SnapEnabled`, `GridStep`, `RotationStepRadians`); a single editor-owned entity the toolbar mutates and `GizmoSystem` reads. Pure data — the per-drag accumulation is `GizmoSystem`'s private frame state.
- `GizmoOverlayComponent` (Wave 4b, live, under `Component/`) — tags the gizmo's own overlay mesh entities (the selection outline + the move/rotate/scale handles) so the system can manage them. They are standalone (never `ChildOfComponent`-parented) and set `VisibleComponent` themselves.
- `ToolbarButtonComponent` (Wave 4b, live, under `Component/`) — tags a toolbar button with its `EditorToolbarAction` + its screen-space `Bounds` for `ToolbarSystem`'s hit-test.

### Systems

- `SceneReaderSystem` (Wave 3, live, under `System/`) — subscribes to `LoadSceneRequest`, reads the scene JSON (content stream via `TitleContainer`, or `IPlatformServices` for host-filesystem user data), deserializes via the Wave-2 `SceneSerializer` (two-pass create + deserialize + parent-wire), rehydrates each sprite's `Texture2D` from its `AssetKey`, and fails loud on an unregistered component key. The texture loader is injectable (`Func<string, Texture2D>`) so it is unit-testable without a `GraphicsDevice`.
- `SelectionSystem` (Wave 4a, live, under `System/`) — on a left-button press in Edit, hit-tests the cursor's `WorldPosition` against each rendered sprite's world-space quad (`SpriteHitTest`, honoring rotation/scale/origin/offset) and selects the frontmost (MAX final post-YSort `DrawComponent.LayerDepth`, tiebroken by `EditorIdComponent`). Ordered at the end of the draw pipeline so it reads the final depth this frame; Edit-guarded; click-empty clears.
- `EditorModeToggleSystem` (Wave 4a, live, under `System/`) — flips `GameState.RunMode` Play↔Edit in place (no screen swap) when a `Func<GameState,bool>` predicate fires.
- `EditorCommandSystem` (Wave 4a, live, under `System/`) — Edit-guarded; translates delete/undo/redo intent into `EditorHistory` operations (delete builds a sub-graph-snapshotting `DeleteEntityCommand`).
- `GizmoSystem` (Wave 4b, live, under `System/`) — reads `SelectedComponent`, draws the move/rotate/scale handle + a selection outline as standalone overlay mesh entities (self-`VisibleComponent`, world-space on Main, sized by `1/Camera.Zoom`), hit-tests the active handle, and on a drag opens an `EditorHistory` transaction, pushes a `TransformEditCommand` each frame (computed by the pure `GizmoTransform`, applying grid-snap), and commits on release → one undo step. Inert + overlays torn down in Play. Runs before `HierarchySystem` so the edit propagates the same frame.
- `ToolbarSystem` (Wave 4b, live, under `System/`) — Edit-guarded; hit-tests the cursor's `VirtualPosition` against each `ToolbarButtonComponent.Bounds` and hands a click's `EditorToolbarAction` to a game-supplied dispatch; also hides the toolbar (blanks the button mesh + label) in Play.
- `Transform/GizmoTransform` (Wave 4b, live) — the pure, GraphicsDevice-free transform math behind the gizmo (move/rotate/scale + grid-snap, honoring Origin); separated from `GizmoSystem` so it is directly unit-testable.
- `UI/EditorToolbarBuilder` (Wave 4b, live) — builds the toolbar (an `AutoLayoutBuilder` HUD root + a fixed row of labelled `SimpleButtonComponent` buttons each tagged with a `ToolbarButtonComponent`); the engine button rendering (`ButtonMeshPrepSystem`) draws them. No ImGui; web-capable.

### Undo (Wave 4a, live — under `Undo/`)

- `IEditorCommand` — a reversible editor mutation as DATA + an `Apply`/`Revert` pair (ECS purity: not a behavior-laden OO object).
- `EditorHistory` — bounded undo/redo (configurable cap, oldest-evicted FIFO; empty-stack no-op) with the drag-coalescing transaction API (`BeginTransaction`/`CommitTransaction`/`CancelTransaction` → one entry per drag).
- `CompositeCommand` — bundles a coalesced transaction's commands into one undo step.
- `CreateEntityCommand` / `DeleteEntityCommand` — create tags the new root `SceneObjectComponent` + snapshots its sub-graph; delete snapshots the disposed sub-graph (reusing `SceneSerializer`) so undo restores it whole. `EntitySubgraph.Collect` is the shared `ChildOf` descendant-closure walk.
- `TransformEditCommand` — a before/after transform edit; `GizmoSystem` (Wave 4b) constructs it via `FromCurrent` each drag frame and the coalescing transaction collapses the drag into one entry.

### Serialization writer (Wave 3, live — under `Serialization/`)

- `SceneWriter` — computes the membership closure (`SceneWriter.CollectMembership`: every `SceneObjectComponent` root + each one's `ChildOfComponent` descendants), serializes it through the Wave-2 `SceneSerializer` into a `SceneData` (attaching `Camera` state + the `DrawLayerMap` banding), JSON-serializes it, and exports through `IPlatformServices.ExportScene`. Save-time only — never per frame.

### Messages

- `LoadSceneRequest` (Wave 3, live, under `Message/`) — loads a native MonoDreams scene file; distinct from `LoadLevelRequest` so it never triggers the LDtk `Content.Load` / `Remove<CurrentLevelComponent>` path. Carries the scene `Path` and a `FromContent` flag (content asset vs. host-filesystem user data).

## Pipeline wiring (intended)

An editor-capable screen composes editor systems over the game pipeline in the *same* world, with the game systems wrapped per the interaction matrix in `docs/CORE_TENETS.md`:

- **`RunNormally`** (live in both modes): input mapping, `CursorInputSystem`, `CursorPositionSystem`; the whole render module (`CullingSystem`, `SpritePrepSystem`, `YSortSystem`, `MeshPrepSystem`, `TextPrepSystem`, `MasterRenderSystem`); and `HierarchySystem` (editor edits to a transform must still propagate to world space).
- **`Freeze`** (Play only): movement / velocity / physics / collision, NPC / dialogue, and `CameraFollowSystem` (in Edit the editor drives `Camera.Position`/`Zoom` directly).
- **Editor systems** (selection, gizmo, undo-apply, scene save/load, toolbar): registered always, but Edit-guarded so they are inert in `Play`.

Editor-overlay entities (gizmo, selection highlight, toolbar) are **standalone** — never parented via `ChildOfComponent` — so `HierarchySystem.DisposeOrphans` (live in Edit) cannot cascade-dispose them.

## Cross-module dependencies

- `foundation` — `GameState.RunMode` + `GatedSystem` (the run-state model), `TransformComponent`, `ChildOfComponent`, `HierarchySystem`.
- `rendering` — the editor previews through the real `DrawComponent` pipeline + `Camera`; gizmo meshes use the mesh primitives.
- `ui` — the toolbar is built with `AutoLayoutBuilder` on the UI/HUD render target.
- `cursor` — selection and gizmo dragging read `CursorInputComponent.WorldPosition`.
- `level-loading` — scene save/load reuses the spawn-request plumbing and the `IPlatformServices` storage seam; the native `LoadSceneRequest` is deliberately separate from the LDtk/Blender `LoadLevelRequest`.

## Extension points

- **Game-component serialization (Wave 2, live).** Register `(write, read)` serializers for game-specific components (e.g. `PlayerState`) on the registry — `registry.Register(key, typeof(MyComponent), write, read)` — so they round-trip; the engine ships serializers for its own serializable components via `registry.RegisterEngineComponents()`. Only registered types serialize (opt-in); an unregistered component on an entity is skipped with a loud warning at write time.
- **Per-system edit-time policy.** Opt a system into freezing by wrapping it in `GatedSystem(child, EditTimeBehavior.Freeze)`; leave render/input/cursor/hierarchy ungated or `RunNormally`.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (run-state + serialization live; later-wave invariants pre-committed).
- [Scene format](scene-format.md) — the native MonoDreams scene schema (Wave 2).
- `docs/CORE_TENETS.md` — "The editor is part of the game" tenet + the run-state interaction matrix.
- `MonoDreams/foundation/docs/premises.md` — the run-state premises (`GatedSystem` policy + back-compat).
- `docs/flows/level-editor.md` — the per-frame flow doc.
- `docs/level-editor/roadmap.md` — wave map and plug-in points (Wave 2).
