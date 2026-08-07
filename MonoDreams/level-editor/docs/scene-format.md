# Native MonoDreams scene format

> The on-disk format for a native MonoDreams scene — what the in-game level
> editor saves and loads. Distinct from the LDtk (`.ldtk`) level format: it is *imported* by parsers
> that re-run factories; a native scene is a **full component serialization**
> that round-trips by reconstructing components, never by re-running factories.
> The native scene is loaded by a dedicated `LoadSceneRequest` message (Wave 3),
> never `LoadLevelRequest` — that message belongs to whichever level dispatcher a screen
> composed (the native-only `LevelLoadRequestSystem`, or an import pipeline's
> `LDtkLevelLoadSystem`), and the dedicated message keeps the reader independent of it (see
> [`level-loading` premises](../../level-loading/docs/premises.md)).

**Status (persistence phase complete):** the format, its in-memory model
([`Serialization/SceneData.cs`](../Serialization/SceneData.cs)), the
component-serializer registry that fills it
([`Serialization/ComponentSerializerRegistry.cs`](../Serialization/ComponentSerializerRegistry.cs)),
the file writer/reader (`SceneWriter` + `LoadSceneRequest` + `SceneReaderSystem`),
the **canonical byte-stable serializer** with **stable per-entity ids** (PS1),
the **`game.mdproj` manifest** (PS2), **save into the versioned source tree**
(PS3), **native-first boot via `LoadLevelRequest` + `/copy:` bundling** (PS4),
**LDtk import-only** (PS5), and the **ship-readiness lint** (PS6) are all
live. The parametric `sources[]` waves (D–F) land later still; the schema is
designed forward-stable so those waves extend it without a breaking version bump.

Serialization is **System.Text.Json** and
runs on **save/load only** — never per-frame. All scene JSON is written and read
through one **canonical policy** ([`CanonicalJson`](../Serialization/CanonicalJson.cs))
so the bytes are deterministic — see [Canonical serialization](#canonical-serialization).

## Top-level schema

| Field | Type | Meaning |
|---|---|---|
| `version` | int | Scene format version. Bump on a breaking schema change. Currently `3` (camera-as-entity, CM). |
| `layers` | array | Named draw-depth layers; each has `name`, `depth` `[min,max]`, `ySorted` bool. |
| `sources` | array | **Reserved** for later parametric-source waves (ground splatmap / road / scatter — Waves D–F). Empty today; a reader ignores unknown entries, so adding source kinds later needs no version bump. |
| `entities` | array | The serialized entities (see below) — including the camera, which is an ordinary entity now (CM). |

> **There is no `camera` block (CM, v3).** The scene camera is an ordinary
> `core.Camera` ENTITY in `entities[]`, not a special top-level block — the
> "one data model" tenet (`CORE_TENETS` §9). A legacy `camera` block (a v2 or
> earlier file) survives on `SceneData.Camera` only as a
> **deserialization-only DETECTION target**: `SceneVersionGuard` refuses such a
> file on read (*"run `monodreams migrate`"*) and the CLI camera migrator lifts
> the block into a camera entity. The writer never emits it. See
> [`camera-as-entity.md`](../../../docs/level-editor/camera-as-entity.md).

### The camera entity (`core.Camera`)

The camera is a scene root carrying `EntityInfoComponent("Camera")` +
`TransformComponent` (position AND rotation — one rotation, on the Transform) +
`CameraComponent` (the authored `zoom`; the virtual resolution stays render
config on the `Camera` adapter, never scene data). Exactly ONE per scene: the
writer refuses a second, the reader ensures one exists on load (a camera-less
scene gets a default), and prefabs refuse a camera entirely. In `Play`,
`CameraSyncSystem` copies the camera entity's `(WorldPosition, WorldRotation,
Zoom)` into the shared `Camera` render adapter; in `Edit` the adapter is the
editor's free view (the camera entity is just data). Serialized like any entity:

```json
{ "id": 5, "components": {
  "core.Camera": { "zoom": 4 },
  "core.EntityInfo": { "type": "Camera" },
  "core.Transform": { "position": [0, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
} }
```

### `layers[]`

Mirrors the game's draw-layer map (`DrawLayerMap.GetDepth`). Each layer records
the depth band a layer's sprites occupy and whether that layer is Y-sorted (its
final `LayerDepth` is derived per-frame from world Y by `YSortSystem`). This lets
a loaded scene reconstruct layer banding without re-deriving it from entities.

```json
{ "name": "Characters", "depth": [0.40, 0.50], "ySorted": true }
```

### `entities[]`

Each entity is an optional stable `id`, a `components{}` map, and an optional
`parent` reference:

| Field | Type | Meaning |
|---|---|---|
| `id` | int \| null | The **persisted, stable, scene-local id** of a scene ROOT — assigned at first serialization, preserved across `load → save`, and the key `entities[]` is ordered by. Only roots carry one; a `ChildOf` descendant omits it (it is ordered within its ancestor's closure). Omitted when null. Backed by a `SceneEntityIdComponent` on the live root; captured as a dedicated structural field (like `parent`), never a component body. |
| `components` | object | `componentTypeKey` → serialized fields (one JSON object per component). Only **registered** components appear; unregistered components on the live entity are skipped with a loud `Logger.Warning` at write time. The canonical writer emits these keys in **ordinal-sorted** order (deterministic, independent of live component-storage order). |
| `parent` | int \| null | Index (into `entities[]`) of this entity's structural parent (`ChildOfComponent`), or `null` (omitted) for a root. Index-based so the parent graph round-trips without persisting volatile `Entity` ids. |

The `componentTypeKey` is the stable string the registry assigns a component
`Type`. The engine ships these keys (see
[`EngineComponentSerializers`](../Serialization/EngineComponentSerializers.cs)):

| Key | Component | Serialized fields (SOURCE only) |
|---|---|---|
| `core.Transform` | `TransformComponent` | `position`, `rotation`, `scale`, `origin` (the cached world matrix is derived, not stored) |
| `core.SpriteInfo` | `SpriteInfoComponent` | `assetKey` (**never** the live `Texture2D`), `source`, `size`, `color`, `origin`, `offset`, `target`, and the SOURCE sort fields `layerDepth` / `ySortOffset` / `ySortDepthBias` (**never** the per-frame-derived `DrawComponent.LayerDepth`) |
| `core.EntityInfo` | `EntityInfoComponent` | `type`, `name` |
| `core.Camera` | `CameraComponent` | `zoom` (position + rotation come from the entity's `Transform`; virtual resolution is render config, not scene data). The scene camera is an ENTITY (CM) — one per scene. |
| `core.BoxCollider` | `BoxColliderComponent` | `size` (a **centered** `[w,h]` — the pose is the collider entity's `Transform`, no offset), `activeLayers`, `passive`, `enabled` (broad-phase AABB is derived). A collider is its own entity (colliders-as-entities, v2). |
| `core.ConvexCollider` | `ConvexColliderComponent` | `modelVertices` (collider-entity-local), `activeLayers`, `passive`, `enabled`, `ignoreTransformRotation` (world vertices + AABB are derived) |
| `core.RigidBody` | `RigidBodyComponent` | `mass`, `gravityActive`, `gravityFactor`, `isKinematic`, `freezeRotation`, `freezePositionX`, `freezePositionY` |
| `core.Velocity` | `VelocityComponent` | `current`, `last` |
| `core.CameraFollowTarget` | `CameraFollowTargetComponent` | `dampingX/Y`, `maxDistanceX/Y`, `isActive`, optional `bounds` — the follow tuning on the entity the camera tracks (in `Play` `CameraFollowSystem` lerps the camera entity toward it). |
| `core.TileGrid` | `TileGridComponent` | `cellSize`, `values[]` (each paint value: `id`, `name`, `color`, `activeLayers` sorted, `passive`, optional `entityType`/`tilesetKey`/`autotileRules`, `tileSize`, `layerDepth`) and `cells[]` as `[x, y, value]` triples sorted by (y, x). The DERIVED tiles + merged colliders are `BakedProductComponent` children — never serialized; `TileGridBakeSystem` re-bakes them on load/change in both the editor and the game. |
| `core.SceneLayer` | `SceneLayerComponent` | `order` (back-to-front, ties by name), `visible`, `locked`, `screenSpace` — the designer's scene LAYER as an ordinary ENTITY (its name is its `core.EntityInfo`, its members are the entities whose `parent` chain reaches it). Distinct from the top-level `layers[]` band map above: a layer entity persists only these authored fields, and its members' final `DrawComponent.LayerDepth` is derived per frame by `SceneLayerSystem` from (layer order, within-layer key = the member's SOURCE `layerDepth` clamped to 0..1). Reordering layers therefore never rewrites member rows. A later tile-paint wave's marker component will make a layer entity a paint layer by being present — the layer's kind is derived from what it carries, never a persisted enum. |
| `core.ChildOf` | `ChildOfComponent` | **none** — the parent link is the entity's `parent` index field, not a component body. Registered only so a parented entity does not trip the unregistered-component warning. |

Game-specific components (e.g. `PlayerState`) are **not** shipped by the engine —
game code registers their serializers on the registry (the extension seam, see
the [overview](overview.md)).

#### Why SOURCE fields, never derived values

`DrawComponent.LayerDepth` is rewritten every frame by `SpritePrepSystem` /
`YSortSystem` from the SOURCE sort fields on `SpriteInfoComponent`. Persisting the
*derived* depth would bake one camera frame's Y-sort result into the file; the
SOURCE fields (`LayerDepth` / `YSortOffset` / `YSortDepthBias`) reproduce the same
derived depth deterministically on the next prep+sort frame after load. This is the
"persisted sort fields are SOURCE not derived" row of the plan-contract's
derived-value table.

#### Why an asset key, never a live texture

A `Texture2D` is a GPU resource, not serializable data. `SpriteInfoComponent`
carries an additive optional `AssetKey` (the content key, e.g. `"Atlas/TX Player"`).
The writer persists the key; Wave 3's reader rehydrates `SpriteSheet` via
`ContentManager.Load(assetKey)`. After deserialization (Wave 2), `SpriteSheet` is
`null` — texture rehydration is a load-time concern the reader owns.

**The `file:` scheme (island-authoring Slice 1).** An asset key may instead use the
`file:` scheme — `"file:Island/props/tree01.png"`, with an optional `#region`
suffix naming a sliced-sheet palette entry (e.g.
`"file:Island/props/sheet.png#trunk"`). The reader routes `file:` keys through the
runtime file-asset loader (`Assets/FileAssetTextureLoader` — lazy
`Texture2D.FromStream` over the content stream; a **missing file loads a visible
magenta placeholder with a loud warning**, never an invisible entity). The region
suffix identifies the catalog entry only: loading always opens the base PNG, and
the region's `source` rectangle is serialized on the sprite itself, so the scene
survives sidecar changes. `file:` keys are the editor's fast authoring loop; when
art finalizes they graduate to plain content keys (see the "`file:` AssetKeys…"
premise in [`premises.md`](premises.md)).

### Ship-readiness (zero `file:` keys)

A scene is **"ship-ready / fully portable"** exactly when it has **zero `file:`
AssetKeys** — every asset reference has graduated to an MGCB content key
(processed, shipped, web-ready). A `file:` key resolves to a magenta placeholder
on a fresh checkout or on web (there is no directory scan there), so this is the
checkable definition of "this committed level is portable". `SceneLint`
([`Serialization/SceneLint.cs`](../Serialization/SceneLint.cs)) is the pure
analyzer — `SceneLint.IsShipReady(scene)` / `FindFileAssetKeys(scene)` — used
three ways: a loud warning on Save when the scene still has `file:` keys (never
blocking), a test that asserts the committed `Content/Levels/**` scenes are
ship-clean, and the plain predicate for any tool. The committed reference levels
(`Blender_Level` / `sample`) use only content-key AssetKeys.

### Bundling (how a scene reaches the shipped game)

A committed `.mdscene` is bundled to the title content by an MGCB `/copy:` entry
in `Content.mgcb` (raw copy, like `blender_level.json` / `game.mdproj`) and read
back read-only through `TitleContainer` on every platform (console-portable). A
**new** level is bundled **zero-touch**: on first Save the editor appends the
`/copy:` entry to `Content.mgcb` (MGCB has no glob syntax; a build-time Nopipeline
regen was rejected because it sweeps the gitignored placeholder-art pack into the
texture build). One mechanism, no double-copy. See the level-editor "New levels
bundle zero-touch…" and level-loading "Native `.mdscene` levels are bundled by an
MGCB `/copy:` entry…" premises.

## Canonical serialization

Every native file is written and read through one canonical policy
([`CanonicalJson`](../Serialization/CanonicalJson.cs)) so the bytes are
**deterministic**: `serialize(world)` is byte-identical across runs and machines,
and `load → save` equals the source file byte-for-byte. That fixed point is what
makes a `.mdscene` git diff meaningful and a merge tractable — the precondition
for versioning levels. The rules:

- **Stable property order.** Strongly-typed objects (the top-level schema, each
  component body) serialize their fields in declaration order. The open
  `components{}` map's keys are emitted in `StringComparer.Ordinal` order (STJ
  does not sort object keys by default — insertion order would leak the live
  component-storage order). Set-valued fields (a collider's `activeLayers`) are
  written sorted ascending (a `HashSet` has no stable enumeration order).
- **Deterministic entity order.** `entities[]` is ordered by each root's stable
  `id` (a stable sort, so each root's `ChildOf` closure stays contiguous and in
  parent-before-child order). A one-entity edit is a one-line diff, never a
  reshuffle.
- **Invariant, round-trippable floats.** Numbers are written culture-invariant
  (a comma-decimal locale still emits `0.1`, never `0,1`) in the shortest
  round-trippable form. Note this normalizes `1.0` to `1` — it still round-trips
  to the same float and re-serializes identically, so the fixed point holds.
- **Indented, LF newlines, trailing newline.** 2-space indent (net8.0's writer
  hardcodes `\n` for indentation, so it is platform-independent), one numeric
  array element per line, and a single trailing `\n`.
- **Null fields omitted.** A root's null `parent`, a null `assetKey`, a camera
  entity's null `name`, a child's null `id` are dropped rather than emitted as
  `"…": null`.

## Concrete example

A v3 scene with a player (Transform + SpriteInfo + RigidBody + Velocity) whose
box collider is its own CHILD entity (colliders-as-entities), a child orb, and
the scene camera entity. Shown **compactly** (arrays inlined, keys grouped) for
readability; the real file is 2-space-indented with one array element per line,
`components{}` keys ordinal-sorted, and whole-number floats in shortest form
(e.g. `1.0` written `1`). Note the roots' stable `id`s (the camera sorts last)
and the omitted `parent`/`assetKey`/`name` nulls:

```json
{
  "version": 3,
  "layers": [
    { "name": "Background", "depth": [0.9, 1], "ySorted": false },
    { "name": "Characters", "depth": [0.4, 0.5], "ySorted": true },
    { "name": "Effects",    "depth": [0.2, 0.3], "ySorted": false }
  ],
  "sources": [],
  "entities": [
    {
      "id": 0,
      "components": {
        "core.EntityInfo": { "type": "Player", "name": "Hero" },
        "core.RigidBody":   { "mass": 1, "gravityActive": true, "gravityFactor": 1, "isKinematic": false, "freezeRotation": false, "freezePositionX": false, "freezePositionY": false },
        "core.SpriteInfo": {
          "assetKey": "Atlas/TX Player",
          "source": [0, 0, 16, 32],
          "size": [16, 32],
          "color": [255, 255, 255, 255],
          "origin": [0, 0],
          "offset": [-4, -8],
          "target": 0,
          "layerDepth": 0.45,
          "ySortOffset": 32,
          "ySortDepthBias": 0
        },
        "core.Transform":  { "position": [100, 200], "rotation": 0, "scale": [1, 1], "origin": [0, 0] },
        "core.Velocity":    { "current": [0, 0], "last": [0, 0] }
      }
    },
    {
      "components": {
        "core.BoxCollider": { "size": [16, 32], "activeLayers": [-1], "passive": false, "enabled": true },
        "core.Transform":   { "position": [8, 16], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
      },
      "parent": 0
    },
    {
      "components": {
        "core.EntityInfo": { "type": "Orb", "name": "BlueOrb" },
        "core.Transform":  { "position": [50, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
      },
      "parent": 0
    },
    {
      "id": 1,
      "components": {
        "core.Camera": { "zoom": 2 },
        "core.EntityInfo": { "type": "Camera" },
        "core.Transform":  { "position": [320, 180], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
      }
    }
  ]
}
```

On load: pass 1 creates every entity and deserializes its components (the
player's `SpriteInfo.SpriteSheet` is `null`, to be rehydrated from `assetKey`);
pass 2 wires the child parent links (the collider and the orb both `parent: 0`)
via `SetParent`, which syncs both the structural `ChildOfComponent` and the
`TransformComponent.Parent` matrix link. The reader then re-tags each root
(player + camera) with `SceneObjectComponent` and restores its
`SceneEntityIdComponent` from the entry's `id` (so the next save reuses the same
ids and the array order stays byte-stable — `load → save` equals the source file).

## See also

- [`Serialization/CanonicalJson.cs`](../Serialization/CanonicalJson.cs) — the one canonical JSON policy (byte-stable options + ordinal-sorted-map converter) all scene/manifest writes flow through.
- [`Serialization/SceneData.cs`](../Serialization/SceneData.cs) — the in-memory model (1:1 with this schema).
- [`Serialization/ComponentSerializerRegistry.cs`](../Serialization/ComponentSerializerRegistry.cs) — the opt-in registry that fills `components{}`.
- [`Serialization/EngineComponentSerializers.cs`](../Serialization/EngineComponentSerializers.cs) — the engine-shipped serializers + the keys above.
- [`premises.md`](premises.md) — the load-bearing invariants (registry opt-in; AssetKey-not-live-texture; SOURCE-not-derived sort fields).
- [`../../../docs/level-editor/roadmap.md`](../../../docs/level-editor/roadmap.md) — the Wave A–F map.
- [`../../level-loading/docs/premises.md`](../../level-loading/docs/premises.md) — why `LoadSceneRequest` is separate from `LoadLevelRequest`.
```
