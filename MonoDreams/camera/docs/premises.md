# camera — premises

> Technical invariants the engine assumes about the camera module:
> `CameraComponent`, `CameraFollowTargetComponent`, `CameraFollowSystem`, and
> `CameraSyncSystem`. Read this before changing any of them or any code that
> expects the camera to track an entity. **The camera is a scene ENTITY now
> (CM):** anything authored (position, rotation, zoom) is component state on a
> `core.Camera` entity — the `Camera` class demoted to a render adapter.

## The camera is a scene entity; `CameraComponent` holds only zoom

A scene has exactly one **camera entity**: an ordinary scene-owned root carrying
`EntityInfoComponent("Camera")` + `TransformComponent` + `CameraComponent`,
serialized in `entities[]` and `SceneObjectComponent`-tagged like everything
else. `CameraComponent` carries **only** `Zoom` — position AND rotation come
from the entity's `TransformComponent` (ONE rotation, not two), and the virtual
resolution stays render config on the `Camera` adapter, never scene data.

**Why:** the CM tenet — there is one data model; anything authored is component
state on an entity, singletons included. A camera that is a real entity gets
file authoring, Inspector editing, undo, dirty, prefab overrides, byte-stable
diffs and sandbox protection for free (the collider-as-entity cure applied to
the camera — three camera defects in three days lived in camera-only special
plumbing).
**Breaks:** adding a `Rotation` field to `CameraComponent` reintroduces two
rotations (the Transform's and the component's) that drift apart — an `R` gizmo
edit lands on the Transform, and the sync must read it there (pre-mortem #1).
Treating the virtual resolution as scene data couples the file to a render
setting that is immutable on the live `Camera`.
**Tests:** `MonoDreams.Tests/LevelEditor/CameraEntityTests.cs`
(`Writer_OneCameraEntity_SerializesIt`, `CameraEntity_RoundTrips_ByteFixedPoint`);
`Sync_CopiesEntityPositionRotationZoom_IntoTheAdapter` (rotation flows from the
Transform).
**Depends on:** rendering — "`Camera.VirtualResolution` is immutable"; foundation
— "`TransformComponent` is the single spatial component".

## `CameraSyncSystem` is the only writer of the `Camera` adapter in Play

The `Camera` class is a **render adapter**: the draw stack reads its view matrix
every frame, and `CameraSyncSystem` (camera module) is the single system that
writes it in Play — each frame it copies the camera entity's
`(WorldPosition, WorldRotation)` + `CameraComponent.Zoom` into the adapter. This
is **Play-only**: register it wrapped in a `GatedSystem` with
`EditTimeBehavior.Freeze` at every site. In `Edit` the live `Camera` is the
editor's FREE VIEW that `CameraNavSystem` drives — syncing it from the camera
entity would clobber the designer's pan/zoom every frame. When the editor is not
composed, `RunMode` is always `Play` and the gate is a pass-through, so a shipped
game syncs every frame. The rendering module is UNCHANGED — the camera-as-entity
model needs zero draw-stack edits.

**Why:** demoting the `Camera` to an adapter written from entity state keeps the
authored truth on the entity (inspectable, serializable) while the draw stack
keeps consuming a plain `Camera` — one seam, no rendering changes. Play-only
gating is what lets the editor keep a free view that is independent of the
authored camera.
**Breaks:** registering `CameraSyncSystem` with `RunNormally` (or not gating it)
clobbers the editor's free view the instant you enter `Edit` — the viewport snaps
to the authored camera and pan/zoom fight the sync every frame (pre-mortem #2). A
second writer of `Camera.Position`/`Zoom` in Play creates a tug-of-war (last
write wins) — a transient effect layered ON TOP of the synced base (screen shake)
is fine, but must run AFTER the sync and never feed back into the entity.
**Tests:** `MonoDreams.Tests/LevelEditor/CameraEntityTests.cs`
(`Sync_CopiesEntityPositionRotationZoom_IntoTheAdapter`,
`Sync_FrozenInEdit_NeverWritesTheAdapter_ButRunsInPlay`).
**Depends on:** foundation — "`GatedSystem` freezes a child in `Edit`"; rendering
— "`Camera.VirtualResolution` is immutable".

## `CameraFollowSystem` eases the camera ENTITY, not the adapter

In Play, `CameraFollowSystem` lerps the camera **entity's**
`TransformComponent.Position` toward the active follow target — it does NOT write
the `Camera` adapter (that is `CameraSyncSystem`'s sole job). Follow state is
therefore live-inspectable: the camera entity moves like any entity, and the
sync (registered right after) pushes its pose to the adapter the same frame. It
is still **optional** and Freeze-gated in `Edit` as before: a fixed-camera game
registers only `CameraSyncSystem` (or neither) and positions the camera entity
however it likes. When there is no camera entity or no active target it is a
no-op.

**Why:** writing the entity (not the adapter) makes follow one more thing the
editor can see and undo, and keeps a single adapter writer (`CameraSyncSystem`) —
the tug-of-war the old "second writer to `Camera.Position`" warning described is
structurally impossible when follow targets the entity.
**Breaks:** writing the adapter directly from follow reintroduces two adapter
writers (follow + sync) racing per frame, and the followed pose is no longer on
the entity (not inspectable, not saved). Running the sync BEFORE follow lags the
camera by one frame (the adapter reflects last frame's entity pose).
**Tests:** `MonoDreams.Tests/LevelEditor/CameraEntityTests.cs`
(`Follow_MovesTheCameraEntity_Inspectable_AdapterFollowsViaSync`);
`MonoDreams.Tests/Camera/CameraFollowBoundsTests.cs` (the follow→sync bounds
behaviour).
**Depends on:** "`CameraSyncSystem` is the only writer of the `Camera` adapter in
Play"; "Follow runs after movement, before the sync, before rendering".

## Exactly one camera entity per scene

Every scene **context** has exactly ONE `core.Camera` entity — not just every file
that is loaded (CM-D). `SceneWriter.BuildScene` REFUSES a world with two or more
(loud, naming them — the sibling of the prefab one-root rule); `PrefabWriter` and
the `PrefabExpander` REFUSE a prefab carrying ANY camera (a camera inside a prefab
is multi-camera terrain); and the ENSURE creates one when a scene context has none
— a default `Camera` root, positioned by the auto-frame math (origin for a
content-less scene), `SceneObjectComponent`-tagged so it saves. That ensure is ONE
shared implementation (`SceneCameraEnsure.EnsureCameraEntity`) reused by BOTH the
reader (post file / in-memory load, over the loaded content) AND the
optional-scene-load **file-absent branch** (`NativeLevelLoader.TryPublishSceneLoad`).
The absent branch is what covers a **code-built screen bound to an absent scene
id** (LevelSelection's `level_selection`, every Demos screen) that never runs the
reader: it too gets exactly one camera (at the origin — its content is code-built,
not scene data), so the "Camera" tree row and the round-trip are uniform across
every editor-visible scene context. The ensure is idempotent (a world that already
has a `CameraComponent` is left alone), so a later real load, a `Restart` (sweep +
re-run), a Game-tab round-trip and a cross-screen pending-activation restore all
converge on exactly one; it is excluded for a prefab context (the reader's
`SuppressCameraEnsure`; `TryPublishSceneLoad` is never the prefab path).

**Why:** a single-camera invariant keeps the follow/sync systems simple (they
take the first camera entity), and matches the v1 scope decision (multi-camera +
a Primary flag is named terrain). The writer refusal + the shared ensure converge
on exactly one from any direction (a file with none, a file with one, a corrupt
file with two, and a scene context that never loads a file at all).
**Breaks:** two camera entities make `CameraSyncSystem`/`CameraFollowSystem` pick
a non-deterministic one (whichever enumerates first); the writer refusal stops
that reaching a file. A camera inside a prefab would multiply cameras on every
instance. An ensure that ran only on file loads (not the absent branch) leaves a
code-built menu/demo context camera-less — no "Camera" tree row, a non-uniform
context, and (in Play) a frozen adapter with nothing to drive it. A SECOND copy of
the ensure would let the two paths drift.
**Tests:** `MonoDreams.Tests/LevelEditor/CameraEntityTests.cs`
(`Writer_RefusesTwoCameraEntities_NamingThem`, `PrefabWriter_RefusesACameraEntity`,
`PrefabExpander_RefusesALegacyPrefabCarryingACamera`,
`Reader_EnsuresOneCamera_WhenSceneHasNone_PositionedOnContent_Tagged`,
`Reader_EnsureIsIdempotent_WhenSceneAlreadyHasACamera`,
`Reader_EnsureContentlessScene_PlacesCameraAtOrigin`);
`MonoDreams.Tests/LevelEditor/OptionalSceneLoadTests.cs`
(`Absent_PublishesNoLoad_ButEnsuresExactlyOneCameraEntity`,
`AbsentEnsure_IsIdempotent_WhenTheAbsentBranchRunsAgain`,
`AbsentEnsure_ThenARealLoadThroughTheReader_DoesNotDoubleTheCamera`,
`AbsentEnsuredCamera_FirstSavePersistsIt_AsV3` — the CM-D scene-context coverage);
`MonoDreams.Tests/LevelEditor/PrefabUxTests.cs`
(`SceneReader_SuppressCameraEnsure_NeverCreatesACamera_UnsuppressedDoes` — prefab
contexts stay camera-free).
**Depends on:** level-editor — "A loaded sprite entity carries a `DrawComponent`
(reader-restored); the reader frames the view on content and ensures one camera
entity"; level-editor — "The scene format is version 3; a legacy file with an
embedded collider (v1) or a camera block (v2) is refused loud".

## The `Camera` class itself ships in the `rendering` module

`Camera` is a hard dependency of the draw stack — `MasterRenderSystem` needs its
view matrix every frame — so it lives at `MonoDreams/rendering/Camera.cs`, not in
this module. Under CM it is a **render adapter**: this module's `CameraSyncSystem`
writes it from the camera entity, but the class + its contract are unchanged.
This module adds the camera BEHAVIOUR (`CameraComponent`, `CameraSyncSystem`,
`CameraFollowSystem`, `CameraFollowTargetComponent`); game code can use `Camera`
without ever installing `camera`.

**Why:** decoupling who-drives-the-adapter (this module) from what-the-adapter-is
(the `rendering` module) lets fixed-camera games skip this module without losing
rendering. The split is the cleanest expression of the invariant: rendering
depends on *a* `Camera`, not on *how* it is updated.
**Breaks:** moving `Camera` into this module forces every rendering game to
install camera behaviour it may not use, and gives `rendering` a mandatory
dependency on a behaviour module.
**Tests:** exercised indirectly by every screen (rendering reads the adapter
`CameraSyncSystem` writes); the adapter contract is `rendering`'s
"`Camera.VirtualResolution` is immutable".
**Depends on:** rendering — "`Camera.VirtualResolution` is immutable".

## `CameraFollowSystem` picks the first `IsActive` target each frame

When multiple entities have `CameraFollowTargetComponent`, `CameraFollowSystem`
follows the first one whose `IsActive` flag is true, in whatever order
DefaultEcs's `EntitySet` enumeration produces (no deterministic cross-run
guarantee). Toggle `IsActive` to switch the tracked target.

**Why:** the common case (one active target, toggled via `IsActive`) with a
minimal implementation. A proper multi-target API would need a priority field or
a selection message — framework work, not a workaround.
**Breaks:** two entities with `IsActive = true` produces a non-deterministic
choice; the camera "snaps" to a different entity on reload with no obvious cause.
**Tests:** none yet.
**Depends on:** —

## Follow runs after movement, before the sync, before rendering

The reference pipeline places `CameraFollowSystem` after the physics / movement
module (so the target's final position this frame is what it eases toward), then
`CameraSyncSystem` right after it (so the adapter reflects the just-eased entity
pose), and both before the prep / cull / render stage (so culling and the view
matrix see this frame's camera). Following before movement lags the target by a
frame; syncing before following lags the adapter by a frame; following/syncing
after culling makes the cull frustum reflect last frame's camera.

**Why:** the camera's job is to track the *final* position of its target and hand
it to the draw stack the same frame. Movement → follow (entity) → sync (adapter)
→ cull/render is the only order in which no stage reads stale state.
**Breaks:** `CameraFollowSystem` ahead of `VelocitySystem` trails the player by a
frame; `CameraSyncSystem` before `CameraFollowSystem` shows last frame's followed
pose; either after `CullingSystem` pops entities at the edges.
**Tests:** `MonoDreams.Tests/LevelEditor/CameraEntityTests.cs`
(`Follow_MovesTheCameraEntity_Inspectable_AdapterFollowsViaSync` orders
follow → sync).
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## Follow bounds clamp the target before smoothing

`CameraFollowTargetComponent.Bounds` is an optional world-space `Rectangle?`.
When set, `CameraFollowSystem` clamps the *desired target position* to that
rectangle *before* the smoothing/snap step, then eases the camera ENTITY toward
that clamped point. Because it is the target that is clamped, a camera entity
currently *outside* the bounds (control just handed back from an unbounded
target) eases smoothly back inside rather than snapping to the edge. When null
the camera follows freely.

**Why:** games want the camera to stop at level edges without each screen
re-implementing a clamp (which would mean a second writer to the camera position).
Clamping the *target* keeps a single owner of the camera entity's position AND a
single smooth easing path to an in-bounds goal.
**Breaks:** clamping the *resolved* position after smoothing hard-caps X/Y each
frame, so handing control back to a bounded target from outside snaps to the edge
in one frame. Bounds smaller than the viewport are legal (the camera barely
moves) — the caller's choice.
**Tests:** `MonoDreams.Tests/Camera/CameraFollowBoundsTests.cs` (each case runs
follow → sync and asserts the resolved adapter position).
**Depends on:** "`CameraFollowSystem` eases the camera ENTITY, not the adapter".

## Open questions

- **Multi-camera support** — `Camera` adapters are not registered centrally, and
  the one-camera-per-scene rule is a v1 decision. Split-screen / minimap / CCTV
  is served today by composing multiple `MasterRenderSystem` passes (each with
  its own `Camera`), but a scene authoring TWO camera entities is deliberately
  refused; a Primary-flag multi-camera model is named terrain.

## Aspirational direction

- A priority field on `CameraFollowTargetComponent` (and a deterministic pick
  when multiple are active) would replace today's first-active-wins iteration.
- Camera shake, look-ahead, dead-zones, and look-at as composable systems that
  read `CameraFollowTargetComponent` / the camera entity and layer on top of the
  synced adapter (the camera demo's `CameraHitSystem` is the shake prototype).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `CameraFollowSystem` picks the first `IsActive` target each frame
