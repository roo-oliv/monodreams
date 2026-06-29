# level-editor — overview

The in-game level editor: an **Edit run mode** layered over the real game pipeline. The editor is not a separate application or renderer — it is a mode of the game (see `docs/CORE_TENETS.md`, "The editor is part of the game"). In `Edit`, the same world, the same `Camera`, and the same `CullingSystem → SpritePrepSystem → YSortSystem → MasterRenderSystem` draw stack the player sees stay live; game logic, physics, and camera-follow freeze; and editor tooling (selection, gizmos, undo, scene save/load, toolbar) runs on top.

> **Status: Wave 2.** Wave 1 shipped the run-state model (in `foundation`) and the docs that codify it. Wave 2 adds the **native scene format** + the **component-serializer registry** (this module's first code, under `Serialization/`) and an additive `AssetKey` on `SpriteInfoComponent`. The interactive editor (screen, selection, gizmo, undo, toolbar) and the file writer/reader on `LoadSceneRequest` land in later waves (3–5). The sections below describe the *intended* shape so a fresh session can implement the remaining waves without re-deriving the architecture; anything not yet built is marked **(planned, Wave N)**.

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

### Components (planned, Waves 3–4)

- `SceneObjectComponent` — tags a save-root entity; the scene writer includes each tagged root plus its `ChildOfComponent` descendants.
- `SelectedComponent` — marks the currently-selected entity for the gizmo and toolbar.
- Transform-gizmo components — the move/rotate/scale handle entities (standalone overlay entities, never `ChildOfComponent`-parented to game entities).

### Systems (planned, Waves 3–5)

- The scene writer/reader on a dedicated `LoadSceneRequest` message (NOT `LoadLevelRequest`), built on the Wave-2 registry + `SceneSerializer`.
- A selection system (hit-test cursor world position vs world-space sprite bounds; topmost = MAX final post-YSort `LayerDepth`).
- A transform-gizmo system (gizmo/selection meshes set `VisibleComponent` themselves).
- An editor-command abstraction + bounded undo/redo (drag-coalesced).
- An engine-native toolbar (`AutoLayoutBuilder`, on the UI/HUD render target).

### Messages (planned, Wave 3)

- `LoadSceneRequest` — loads a native MonoDreams scene file; distinct from `LoadLevelRequest` so it never triggers the LDtk `Content.Load` / `Remove<CurrentLevelComponent>` path.

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
