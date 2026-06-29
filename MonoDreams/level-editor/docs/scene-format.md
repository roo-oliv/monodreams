# Native MonoDreams scene format

> The on-disk format for a native MonoDreams scene — what the in-game level
> editor saves and loads. Distinct from the LDtk (`.ldtk`) and Blender
> (`.json` export) level formats: those are *imported* by per-format parsers
> that re-run factories; a native scene is a **full component serialization**
> that round-trips by reconstructing components, never by re-running factories.
> The native scene is loaded by a dedicated `LoadSceneRequest` message (Wave 3),
> never `LoadLevelRequest` (which is LDtk-coupled — see
> [`level-loading` premises](../../level-loading/docs/premises.md)).

**Status (Wave 2):** the format and its in-memory model
([`Serialization/SceneData.cs`](../Serialization/SceneData.cs)) and the
component-serializer registry that fills it
([`Serialization/ComponentSerializerRegistry.cs`](../Serialization/ComponentSerializerRegistry.cs))
are live. The file writer/reader and the `LoadSceneRequest` message land in
Wave 3; the parametric `sources[]` waves (D–F) land later still. The schema is
designed forward-stable so those waves extend it without a breaking version bump.

Serialization is **System.Text.Json**, consistent with `BlenderLevelData`. It
runs on **save/load only** — never per-frame.

## Top-level schema

| Field | Type | Meaning |
|---|---|---|
| `version` | int | Scene format version. Bump on a breaking schema change. Currently `1`. |
| `camera` | object | Camera state at save time: `position` `[x,y]`, `zoom` float, `rotation` float. |
| `layers` | array | Named draw-depth layers; each has `name`, `depth` `[min,max]`, `ySorted` bool. |
| `sources` | array | **Reserved** for later parametric-source waves (ground splatmap / road / scatter — Waves D–F). Empty today; a reader ignores unknown entries, so adding source kinds later needs no version bump. |
| `entities` | array | The serialized entities (see below). |

### `camera`

```json
{ "position": [320.0, 180.0], "zoom": 2.0, "rotation": 0.0 }
```

The editor drives `Camera.Position` / `Zoom` directly in `Edit` (camera-follow
is `Freeze`-gated), so the saved camera is the editor's view, restored on load.

### `layers[]`

Mirrors the game's draw-layer map (`DrawLayerMap.GetDepth`). Each layer records
the depth band a layer's sprites occupy and whether that layer is Y-sorted (its
final `LayerDepth` is derived per-frame from world Y by `YSortSystem`). This lets
a loaded scene reconstruct layer banding without re-deriving it from entities.

```json
{ "name": "Characters", "depth": [0.40, 0.50], "ySorted": true }
```

### `entities[]`

Each entity is a `components{}` map plus an optional `parent` reference:

| Field | Type | Meaning |
|---|---|---|
| `components` | object | `componentTypeKey` → serialized fields (one JSON object per component). Only **registered** components appear; unregistered components on the live entity are skipped with a loud `Logger.Warning` at write time. |
| `parent` | int \| null | Index (into `entities[]`) of this entity's structural parent (`ChildOfComponent`), or `null` for a root. Index-based so the parent graph round-trips without persisting volatile `Entity` ids. |

The `componentTypeKey` is the stable string the registry assigns a component
`Type`. The engine ships these keys (see
[`EngineComponentSerializers`](../Serialization/EngineComponentSerializers.cs)):

| Key | Component | Serialized fields (SOURCE only) |
|---|---|---|
| `core.Transform` | `TransformComponent` | `position`, `rotation`, `scale`, `origin` (the cached world matrix is derived, not stored) |
| `core.SpriteInfo` | `SpriteInfoComponent` | `assetKey` (**never** the live `Texture2D`), `source`, `size`, `color`, `origin`, `offset`, `target`, and the SOURCE sort fields `layerDepth` / `ySortOffset` / `ySortDepthBias` (**never** the per-frame-derived `DrawComponent.LayerDepth`) |
| `core.EntityInfo` | `EntityInfoComponent` | `type`, `name` |
| `core.BoxCollider` | `BoxColliderComponent` | `bounds`, `activeLayers`, `passive`, `enabled` (broad-phase AABB is derived) |
| `core.ConvexCollider` | `ConvexColliderComponent` | `modelVertices`, `activeLayers`, `passive`, `enabled`, `ignoreTransformRotation` (world vertices + AABB are derived) |
| `core.RigidBody` | `RigidBodyComponent` | `mass`, `gravityActive`, `gravityFactor`, `isKinematic`, `freezeRotation`, `freezePositionX`, `freezePositionY` |
| `core.Velocity` | `VelocityComponent` | `current`, `last` |
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

## Concrete example

A scene with a player (Transform + SpriteInfo + BoxCollider + RigidBody) and one
child orb parented to it:

```json
{
  "version": 1,
  "camera": { "position": [320.0, 180.0], "zoom": 2.0, "rotation": 0.0 },
  "layers": [
    { "name": "Background", "depth": [0.90, 1.00], "ySorted": false },
    { "name": "Characters", "depth": [0.40, 0.50], "ySorted": true },
    { "name": "Effects",    "depth": [0.20, 0.30], "ySorted": false }
  ],
  "sources": [],
  "entities": [
    {
      "components": {
        "core.EntityInfo": { "type": "Player", "name": "Hero" },
        "core.Transform":  { "position": [100.0, 200.0], "rotation": 0.0, "scale": [1.0, 1.0], "origin": [0.0, 0.0] },
        "core.SpriteInfo": {
          "assetKey": "Atlas/TX Player",
          "source": [0, 0, 16, 32],
          "size": [16.0, 32.0],
          "color": [255, 255, 255, 255],
          "origin": [0.0, 0.0],
          "offset": [-4.0, -8.0],
          "target": 0,
          "layerDepth": 0.45,
          "ySortOffset": 32.0,
          "ySortDepthBias": 0.0
        },
        "core.BoxCollider": { "bounds": [0, 0, 16, 32], "activeLayers": [-1], "passive": false, "enabled": true },
        "core.RigidBody":   { "mass": 1.0, "gravityActive": true, "gravityFactor": 1.0, "isKinematic": false, "freezeRotation": false, "freezePositionX": false, "freezePositionY": false },
        "core.Velocity":    { "current": [0.0, 0.0], "last": [0.0, 0.0] }
      },
      "parent": null
    },
    {
      "components": {
        "core.EntityInfo": { "type": "Orb", "name": "BlueOrb" },
        "core.Transform":  { "position": [50.0, 0.0], "rotation": 0.0, "scale": [1.0, 1.0], "origin": [0.0, 0.0] }
      },
      "parent": 0
    }
  ]
}
```

On load: pass 1 creates both entities and deserializes their components (the
player's `SpriteInfo.SpriteSheet` is `null`, to be rehydrated from `assetKey`);
pass 2 wires the orb's parent link (`parent: 0`) via `SetParent`, which syncs both
the structural `ChildOfComponent` and the `TransformComponent.Parent` matrix link.

## See also

- [`Serialization/SceneData.cs`](../Serialization/SceneData.cs) — the in-memory model (1:1 with this schema).
- [`Serialization/ComponentSerializerRegistry.cs`](../Serialization/ComponentSerializerRegistry.cs) — the opt-in registry that fills `components{}`.
- [`Serialization/EngineComponentSerializers.cs`](../Serialization/EngineComponentSerializers.cs) — the engine-shipped serializers + the keys above.
- [`premises.md`](premises.md) — the load-bearing invariants (registry opt-in; AssetKey-not-live-texture; SOURCE-not-derived sort fields).
- [`../../../docs/level-editor/roadmap.md`](../../../docs/level-editor/roadmap.md) — the Wave A–F map.
- [`../../level-loading/docs/premises.md`](../../level-loading/docs/premises.md) — why `LoadSceneRequest` is separate from `LoadLevelRequest`.
```
