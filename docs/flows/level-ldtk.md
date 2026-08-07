---
flow: level-ldtk
covers:
  - MonoDreams/level-ldtk/**
sensitive: false
---

# LDtk level parse

> **Import-only, and self-contained.** The LDtk path is not wired to live game boot (the game boots
> native `.mdscene` only). It runs only in the import op's `importMode` composition, to migrate an
> `.ldtk` level into a native scene the game then owns. Since issue #54 the **whole** path lives in
> this module — its `LoadLevelRequest` handler, its level component, and its spawn-channel keys —
> because `level-loading` carries no LDtk type. The flow below is the **import** path.

An LDtk level reaches the parsers as state, not as a message. Game code publishes a
`LoadLevelRequest`; **`LDtkLevelLoadSystem`** (this module — composed *instead of*
`level-loading`'s native-only `LevelLoadRequestSystem`, since both subscribe to that message) loads
the `.ldtk` file via the content pipeline and calls
`world.Set(new LDtkLevelDataComponent(level))`, plus `level-loading`'s decoupled
`CurrentLevelComponent(identifier)` and `CurrentBackgroundColorComponent`. The two parsers —
`LDtkTileParserSystem` and `LDtkEntityParserSystem` — never see the message. Each subscribes in
its constructor to `world.SubscribeWorldComponentAdded<LDtkLevelDataComponent>(...)` and runs its
parse in the add handler; their `Update(GameState)` is empty. This is the engine's intended
**component-driven** dispatch. Each parser also runs a
constructor-time catch-up (`if (_world.Has<LDtkLevelDataComponent>()) HandleLevelLoaded(...)`) so
registration order relative to the load doesn't lose a level already present.

The two parsers are independent — registering one does not register the other. The tile parser
walks `LevelData.LayerInstances`, keeping only `Tiles` and `AutoLayer` layers that pass the LDtk
editor's per-layer `layer.Visible` filter (this is **not** the engine's `VisibleComponent`), loads
each layer's tileset texture once, and publishes one `EntitySpawnRequest` per `GridTiles`/
`AutoLayerTiles` entry. The entity parser walks **every** layer's `EntityInstances` (the layer-type
guard is commented out) and publishes one request per instance, parsing each instance's
`FieldInstances` into the request's `CustomFields`. Both also publish the layer-derived values a
factory used to read off `request.Layer` into that same dictionary, under the `ldtk:`-prefixed keys
of `LDtkSpawnFields` (`ldtk:layerOpacity`, `ldtk:gridSize`) — the shared message carries no LDtk
type. Neither parser creates entities itself — both emit into the shared seam (`EntitySpawnRequest`
→ `EntitySpawnSystem` → registered `IEntityFactory`) defined in `level-loading`. An LDtk identifier
with no registered factory is warned-and-dropped there, not here.

## Entities & lifecycle

No ECS entity is created in this module — it produces `EntitySpawnRequest` messages. Per
`LDtkLevelDataComponent` add:

1. **Tile parse** (`LDtkTileParserSystem`) — `CleanupTileEntities()` disposes the previous level's
   tracked tiles, then for each visible Tile/AutoLayer layer it emits a request with identifier
   `"Tile"` (or `"Wall"` for the `Wall_AutoLayer` layer), `instanceIid` keyed by layer+pixel, and
   `CustomFields` carrying `layerDepth`, `tilesetTexture`, `tileId`, and the `ldtk:` layer channel.
2. **Entity parse** (`LDtkEntityParserSystem`) — emits a request per `EntityInstance` using the
   instance's `_Identifier`, `Iid`, pixel position, size, pivot, and tile-source position, plus
   parsed `CustomFields` and the `ldtk:` layer channel.
3. **Spawn** (downstream, `level-loading`) — `EntitySpawnSystem` dispatches each request to the
   factory registered for its identifier; the factory builds the actual renderable/physics stack,
   reading the `ldtk:` keys with safe defaults (opacity `1f`, grid size `16`).

On `LDtkLevelDataComponent` removal the tile parser disposes its tracked tile entities; the entity
parser does not subscribe to removal (entity cleanup is the factory/game's concern).

## Invariants

Authoritative list in [`MonoDreams/level-ldtk/docs/premises.md`](../../MonoDreams/level-ldtk/docs/premises.md); the ones this parse path leans on:

- Both parsers fire on this module's own `LDtkLevelDataComponent` **added**, never on
  `LoadLevelRequest` — the component-driven seam shared with `level-loading`, keyed on the
  component that actually carries the `LDtkLevel`.
- The shared `EntitySpawnRequest` carries no LDtk type; layer-derived data rides `CustomFields`
  under `LDtkSpawnFields`' `ldtk:` keys, which factories read with safe defaults.
- Tile and entity parsers are independent registrations; a game can register either alone.
- `layer.Visible` is the LDtk editor toggle, not the runtime `VisibleComponent` owned by
  `CullingSystem` — never collapse the two in a rename.

## Load-bearing quantities

- `layer._GridSize` — tile edge length in **pixels**; written as the request `size` (square) for
  tiles, and published on every request as `CustomFields["ldtk:gridSize"]` (`int`, factory default
  `16`) for factories that size themselves off the grid. Drives world placement scale.
- `layer._Opacity` — the LDtk layer's alpha, published as `CustomFields["ldtk:layerOpacity"]`
  (`float`, factory default `1f`); factories multiply their sprite color by it.
- `tile.Px` (`Vector2`) — tile's top-left **pixel** position in level space; tiles use
  `pivot: Vector2.Zero` (origin at top-left). `entityInstance.Px` is the analogous per-entity pixel
  position, paired with the instance's real `_Pivot`.
- `tile.Src` (`Vector2`) — source **pixel** coordinate into the tileset texture (the sub-rect to
  blit).
- `TILEMAP_BASE_LAYER_DEPTH = 0.09f`, `TILEMAP_LAYER_DEPTH_STEP = 0.001f` — layer depth starts at
  `0.09` for the first layer and **decreases** by `0.001` per subsequent layer, passed as
  `CustomFields["layerDepth"]`. LDtk stores layers foreground-first, so earlier array layers get the
  higher (nearer) depth.

## Failure modes

- **Missing factory registration** — an LDtk entity/tile identifier (`"Tile"`, `"Wall"`, or any
  entity `_Identifier`) with no registered `IEntityFactory` is warn-and-dropped by `EntitySpawnSystem`;
  the entity is silently absent. Diagnosed only via the `Logger.Warning`.
- **`layer.Visible` conflated with `VisibleComponent`** — a refactor that renames "Visible" broadly
  flips the parse-time layer filter, dropping whole tile layers from the level.
- **Tileset path/texture failure** — a layer with an empty `_TilesetRelPath`, or a `_content.Load`
  miss, is logged and skipped; that layer's tiles never spawn while others do, producing a partially
  rendered level.
- **Unknown custom-field type** — `ParseFieldValue` warns and stores the raw `_Value`; a factory
  expecting a typed value gets an unparsed object and the per-instance LDtk configuration is lost.
- **Factory reading an `ldtk:` key without a default** — the same factory also serves code-driven
  spawns (the lightweight `EntitySpawnRequest(identifier, position)` ctor, the `prefab:` channel),
  which carry no `ldtk:` keys at all; an unguarded read throws, or zeroes the sprite color so the
  entity spawns invisible. Always `TryGetValue` + a typed default (opacity `1f`, grid size `16`).
