# level-ldtk — overview

Load levels authored in LDtk (Level Designer Toolkit): both tile layers and entity layers. The `.ldtk` file is built via the LDtkMonogame content pipeline; `LDtkLevelLoadSystem` turns a `LoadLevelRequest` into an `LDtkLevelDataComponent`, and two parser systems (`LDtkTileParserSystem`, `LDtkEntityParserSystem`) subscribe to that component being added and emit `EntitySpawnRequest`s for everything in the level. Install this when authoring 2D levels in LDtk.

> **Import-only.** The shipped game boots native `.mdscene` levels through `level-loading`'s `LevelLoadRequestSystem`; this module is composed only in an import/migration op, to re-parse an `.ldtk` level so it can be serialized to native. Nothing in `level-loading` references LDtk — the arrow points level-ldtk → level-loading, never the reverse (issue #54).

## Purpose

LDtk is a level-authoring tool MonoDreams ships first-class support for. This module ships its own `LoadLevelRequest` handler, its own level-data component, the two parser systems, and the content-pipeline integration that lets MGCB read `.ldtk` files. It splits parsing into two independent systems — tiles and entities — so a game can opt into both, only one, or neither by which systems it registers. The module uses the engine-wide component-driven dispatch pattern: parsers subscribe to `LDtkLevelDataComponent` being added, which makes tests and tooling able to trigger parsing without faking a `LoadLevelRequest` message or touching the content pipeline.

## What ships

### Components

- `LDtkLevelDataComponent` — world-scoped singleton holding the full `LDtkLevel` (all LDtk richness stays available to games that install this module); the parsers' add-trigger

### Systems

- `LDtkLevelLoadSystem` — subscribes to `LoadLevelRequest`, loads `World/<identifier>` as an `LDtkLevel`, and sets `LDtkLevelDataComponent` plus `level-loading`'s `CurrentLevelComponent(identifier)` and `CurrentBackgroundColorComponent`. This is the LDtk load entry point; compose it *instead of* `LevelLoadRequestSystem` in an import pipeline
- `LDtkTileParserSystem` — subscribes to `LDtkLevelDataComponent` added; walks `LayerInstances` of type `Tile` and `AutoLayer`, publishes one `EntitySpawnRequest` per tile. Honors LDtk's per-layer `Visible` flag (parse-time filter; distinct from the engine's `VisibleComponent`). Disposes its tracked tile entities when the component is removed
- `LDtkEntityParserSystem` — subscribes to `LDtkLevelDataComponent` added; walks all layers' `EntityInstances`, publishes one `EntitySpawnRequest` per entity. Parses LDtk custom fields into `EntitySpawnRequest.CustomFields`

### Spawn-channel keys

- `LDtkSpawnFields` — the `ldtk:`-namespaced `CustomFields` keys the parsers publish so factories get layer-derived data without an LDtk type on the shared message: `LayerOpacity` (`"ldtk:layerOpacity"`, `float`) and `GridSize` (`"ldtk:gridSize"`, `int`). LDtk field identifiers cannot contain `':'`, so these never collide with a designer's own fields

No messages — this module consumes/emits the contracts defined in `level-loading`.

## Pipeline wiring

**Content pipeline setup.** LDtk requires the `LDtkMonogame.ContentPipeline` DLL to be referenced from your csproj so MGCB can find the importer/processor:

```xml
<MonoGameMGCBAdditionalArguments>$(MonoGameMGCBAdditionalArguments) /reference:$(NuGetPackageRoot)ldtkmonogame.contentpipeline/1.8.0/lib/net8.0/LDtk.ContentPipeline.dll</MonoGameMGCBAdditionalArguments>
```

This is a content-pipeline quirk — MGCB runs as a separate process from the csproj and doesn't inherit its NuGet references; the explicit `/reference:` line is the supported workaround.

**MGCB entries.** Add your `.ldtk` file to `Content.mgcb`:
```
#begin Levels/your_project.ldtk
/importer:LDtkImporter
/processor:LDtkProcessor
/build:Levels/your_project.ldtk
```

**Runtime wiring.** This module's loader replaces `level-loading`'s native dispatcher in an LDtk (import) pipeline — compose the loader, then the parsers, then `EntitySpawnSystem`:
```csharp
new LDtkLevelLoadSystem(world, content)   // LoadLevelRequest → LDtkLevelDataComponent
new LDtkTileParserSystem(world, content, ...)
new LDtkEntityParserSystem(world, ...)
entitySpawnSystem                          // from level-loading
```
Register only the parsers you need — entities-only games can omit `LDtkTileParserSystem`. Do **not** also compose `LevelLoadRequestSystem`: both subscribe to `LoadLevelRequest`, and the native-only one fails loud on an id it can't resolve natively.

Trigger a load by publishing `LoadLevelRequest` from game code.

**Reading layer data in a factory.** `EntitySpawnRequest` carries no LDtk type, so read the layer-derived values off the `ldtk:` channel defensively — a code-driven spawn (the lightweight `EntitySpawnRequest(identifier, position)` ctor, the `prefab:` channel) carries no `ldtk:` keys at all:
```csharp
private static float LayerOpacity(in EntitySpawnRequest request) =>
    request.CustomFields.TryGetValue(LDtkSpawnFields.LayerOpacity, out var v) && v is float opacity ? opacity : 1f;

private static int GridSize(in EntitySpawnRequest request) =>
    request.CustomFields.TryGetValue(LDtkSpawnFields.GridSize, out var v) && v is int gridSize ? gridSize : 16;
```

## Cross-module dependencies

- `level-loading` — consumes `LoadLevelRequest`, sets `CurrentLevelComponent` / `CurrentBackgroundColorComponent`, and emits `EntitySpawnRequest` into `EntitySpawnSystem`. The dependency is one-way: `level-loading` never references LDtk.
- `rendering` — tile entities are sprites; the spawned entities need a renderable component stack (the `IEntityFactory`s registered against the spawn identifiers wire the actual `SpriteInfoComponent` / `DrawComponent`).

## Extension points

- **Custom field types.** `LDtkEntityParserSystem.ParseFieldValue` warns and returns raw values for unknown LDtk field types. Add a case there to recognize a new custom-field shape.
- **Per-entity behavior.** Register an `IEntityFactory` for each LDtk entity identifier (in `EntitySpawnSystem` from `level-loading`). The factory reads `EntitySpawnRequest.CustomFields` for per-instance configuration from the LDtk editor and for the module's own `ldtk:` layer channel.
- **More layer-derived data.** Need another per-layer value in factories? Add a key to `LDtkSpawnFields` and populate it in the parsers — never a new member on the shared `EntitySpawnRequest`.

## See also

- [Premises](premises.md) — load-bearing invariants (import-only composition, the `LDtkLevelDataComponent` add-trigger, the `ldtk:` spawn channel, tile/entity parser independence, LDtk's `layer.Visible` ≠ engine `VisibleComponent`, the content-pipeline DLL reference quirk)
- Related modules: `level-loading` (the plumbing this module plugs into), `rendering` (consumes the sprite entities spawned by tile parsing)
