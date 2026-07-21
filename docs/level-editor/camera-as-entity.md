# Camera as entity (CM phase) — one data model, singletons included

> User-approved design (2026-07-10), all five calls confirmed: explicit
> `CameraComponent.Zoom` (not Transform.Scale-as-zoom); exactly ONE camera per scene
> v1 (multi-camera + Primary flag = named terrain); the `layers` block STAYS for now;
> a clean migration lift (the CLI pattern, v2→v3); `CameraFollowSystem` writes the
> camera ENTITY in Play (live-inspectable; the Game-tab snapshot protects authored
> truth). Motivating bug: the third camera defect in three days (zoom edits not
> reaching `CameraRigComponent`/the file) — all three lived in camera-only special
> plumbing (the `scene.camera` block + the editor-materialized rig + bespoke
> commands + re-sync seams). Colliders stopped breaking the day they became real
> entities; the camera gets the same cure.
>
> **The tenet this codifies (CORE_TENETS):** *there is ONE data model — anything
> authored is component state on an entity, singletons included; special file blocks
> are debt.* Corollary: if the Inspector shows it, Save persists it; if Save
> persists it, the round-trip owns it. **The extension recipe** (documented for
> future devs): define a component (name authored vs derived fields) → register its
> serializer → optionally add an Inspector default-initializer. That buys file
> authoring, Inspector editing, undo, dirty, prefab overrides, byte-stable diffs,
> and sandbox protection — depth by composition, not configuration.

## 1. The model (wave CM-A — engine core)

- **`CameraComponent { float Zoom = 1f }`** (camera module; registered as
  `core.Camera`). Position AND rotation come from the entity's `TransformComponent`
  (one rotation, not two). Virtual size stays render config (the adapter/
  `ViewportManager`), never scene data. The camera entity is an ordinary
  scene-owned root: `EntityInfoComponent("Camera")` + Transform + CameraComponent,
  serialized in `entities[]`, `SceneObjectComponent`-tagged like everything else.
- **The live `Camera` class demotes to a render adapter.** `CameraSyncSystem`
  (camera module, **Play-only / Freeze in Edit**): each frame copies the camera
  entity's `(WorldPosition, WorldRotation)` + `CameraComponent.Zoom` into the
  shared `Camera` object the draw stack already consumes — ZERO rendering-module
  changes. In Edit the live `Camera` remains the free editor VIEW (nav unchanged);
  the camera entity is just data, moved/edited like any entity.
- **`CameraFollowSystem` retargets**: in Play it lerps the camera ENTITY's
  Transform toward the follow target (then sync pushes to the adapter) — follow
  state becomes inspectable live. Freeze-gated in Edit as today.
- **One camera per scene (v1)**: the writer REFUSES a scene with ≥2
  `CameraComponent` entities (the prefab one-root rule's sibling, loud); prefabs
  REFUSE `core.Camera` entirely (a camera inside a prefab is multi-camera terrain).
  The READER ensures exactly one: a camera-less scene gets a default camera entity
  created post-load (positioned by the existing auto-frame logic — the UX3-A sane-
  default lesson), both editor and shipped paths.
- **Serialization v3**: `SceneData.Version` → 3; the `camera` block LEAVES the
  schema. Guard (the CE-B precedent, symmetric): a v2 file WITHOUT a camera block
  loads fine and re-saves v3; a v2 file WITH one is refused loud → "run
  `monodreams migrate`". `SceneCameraData` survives only inside the migrator.
  **`SceneWriter.BuildScene` drops its camera parameter entirely** (the camera is
  in the world) — every capture/save/snapshot call site simplifies; the special
  captures die.

## 2. The editor (wave CM-B — the rig dies)

- DELETE: `EditorCameraRig`, `CameraRigComponent`, the rig materialization +
  `SyncFromScene`/`AsCamera` seams, `CameraZoomEditCommand`, the rig tree-row
  special-include and labeler case, the reader's `applyCameraToRig` seam. The
  camera entity needs none of it.
- The **frustum glyph** retargets: emitted from the camera ENTITY (world pos +
  Zoom → bounds + X cross) when the view ≉ the entity; `view:camera` snaps the
  view to the entity; Game-tab entry adopts the entity's state. Prefab contexts
  have no camera entity → glyph/button naturally inert there.
- **Editing**: Zoom is an ordinary Inspector float (type-colored, editable); the
  `S` gesture on the camera entity maps to Zoom via the standard
  `MemberEditCommand` (transaction-coalesced — one drag/modal session = one undo
  step); `G` moves it; `R` rotates its Transform (now legal — one rotation).
  Deleting the LAST camera entity is refused (loud hint); `core.Camera` is
  excluded from Add-component candidates (the one-camera rule; the reader's
  ensure covers absence).
- **New-scene template**: Create Empty Scene births a default camera entity.
  Session/tab snapshot capture simplifies (no camera side-channel).
- **The acceptance test = the user's bug**: scale the camera (gizmo AND modal S)
  → Zoom edits visibly (Inspector), persists through save → load → save
  byte-stably, and survives tab switches. Written repro-first.

## 3. Migration + docs (wave CM-C)

- **`monodreams migrate`** — the umbrella command: applies every known lift in
  order (v1→v2 colliders, v2→v3 camera-block→camera-entity) per file, idempotent,
  byte-canonical, `--dry-run`, dir recursion; `migrate-colliders` remains as an
  alias for the single lift. The camera lift: remove the block, append a camera
  entity (Transform from block position/rotation, `core.Camera` zoom,
  EntityInfo "Camera"), stamp v3; camera-less v2 → version bump + the default
  camera entity (so v3 files are uniformly explicit).
- Committed content migrated in-repo (sample, Blender_Level, fixtures) via the
  real CLI. The user's WIP files: migrated by the orchestrator at delivery (the
  collider-migration precedent; idempotent, approved approach).
- **Docs**: the CORE_TENETS tenet + extension recipe; camera/rendering/
  level-editor premises rewritten (rig premises deleted; adapter premise;
  one-camera premise; follow-writes-entity premise); roadmap CM section; the
  named terrain recorded (multi-camera + Primary, cameras-in-prefabs, layers-as-
  entity, split-screen viewport rects).

## 4. Pre-mortem

1. **Two rotations** — `CameraComponent` must NOT grow a Rotation field; the
   Transform owns it (tested: R edits Transform, sync reads it).
2. **Sync vs nav fights** — `CameraSyncSystem` MUST be Play-only or it clobbers
   the editor view every frame; entering Edit leaves the view wherever it was.
3. **The ensure-default double-create** — reader-ensure + template + migrator
   must converge on exactly one camera (idempotence tests at each layer).
4. **Snapshot symmetry** — Game-tab exit restores the camera entity like any
   entity; a Play session that moved the camera (follow) must NOT leak into the
   scene tab (the sandbox already guarantees this — assert it for the camera).
5. **BuildScene signature ripple** — dropping the camera param touches every
   writer call site + test; mechanical but wide; do it in ONE commit.
6. **v2-with-camera refusal breadth** — the user's freshly-migrated v2 files DO
   carry camera blocks (recent saves) → they will refuse post-CM until migrated;
   the delivery includes their migration (orchestrator-run).
