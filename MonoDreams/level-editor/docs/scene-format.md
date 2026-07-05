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

Serialization is **System.Text.Json**, consistent with `BlenderLevelData`, and
runs on **save/load only** — never per-frame. All scene JSON is written and read
through one **canonical policy** ([`CanonicalJson`](../Serialization/CanonicalJson.cs))
so the bytes are deterministic — see [Canonical serialization](#canonical-serialization).

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
- **Null fields omitted.** An absent `camera`, a root's null `parent`, a null
  `assetKey`, a child's null `id` are dropped rather than emitted as `"…": null`.

## Concrete example

A scene with a player (Transform + SpriteInfo + BoxCollider + RigidBody) and one
child orb parented to it. Shown **compactly** (arrays inlined, keys grouped) for
readability; the real file is 2-space-indented with one array element per line,
`components{}` keys ordinal-sorted, and whole-number floats in shortest form
(e.g. `1.0` written `1`). Note the root's stable `id` and the omitted
`parent`/`assetKey` nulls:

```json
{
  "version": 1,
  "camera": { "position": [320, 180], "zoom": 2, "rotation": 0 },
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
        "core.BoxCollider": { "bounds": [0, 0, 16, 32], "activeLayers": [-1], "passive": false, "enabled": true },
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
        "core.EntityInfo": { "type": "Orb", "name": "BlueOrb" },
        "core.Transform":  { "position": [50, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
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
The reader then re-tags each root with `SceneObjectComponent` and restores its
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
