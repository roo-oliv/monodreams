# level-ldtk — overview

Load levels authored in LDtk (Level Designer Toolkit): both tile layers and entity layers. The `.ldtk` file is built via the LDtkMonogame content pipeline; two parser systems (`LDtkTileParserSystem`, `LDtkEntityParserSystem`) subscribe to `CurrentLevelComponent` being added and emit `EntitySpawnRequest`s for everything in the level. Install this when authoring 2D levels in LDtk.

## Purpose

LDtk is one of the two level-authoring tools MonoDreams ships first-class support for (Blender is the other). This block ships the parser systems plus the content-pipeline integration that lets MGCB read `.ldtk` files. It splits parsing into two independent systems — tiles and entities — so a game can opt into both, only one, or neither by which systems it registers. The block uses the engine-wide component-driven dispatch pattern: parsers subscribe to `CurrentLevelComponent` being added, which makes tests and tooling able to trigger parsing without faking a `LoadLevelRequest` message.

## What ships

### Systems

- `LDtkTileParserSystem` — subscribes to `CurrentLevelComponent` added; walks `LayerInstances` of type `Tile` and `AutoLayer`, publishes one `EntitySpawnRequest` per tile. Honors LDtk's per-layer `Visible` flag (parse-time filter; distinct from the engine's `VisibleComponent`)
- `LDtkEntityParserSystem` — subscribes to `CurrentLevelComponent` added; walks all layers' `EntityInstances`, publishes one `EntitySpawnRequest` per entity. Parses LDtk custom fields into `EntitySpawnRequest.CustomFields`

No components or messages — this block consumes/emits the contracts defined in `level-loading`.

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

**Runtime wiring.** In your screen's update pipeline, after `LevelLoadRequestSystem` (from `level-loading`):
```csharp
new LDtkTileParserSystem(world, content, ...)
new LDtkEntityParserSystem(world, ...)
```
Register only the parsers you need — entities-only games can omit `LDtkTileParserSystem`.

Trigger a load by publishing `LoadLevelRequest` from game code. Note: do not name LDtk levels with the `Blender_` prefix — that prefix routes to the Blender parser (and the LDtk path explicitly removes `CurrentLevelComponent` for those identifiers).

## Cross-block dependencies

- `level-loading` — uses `LoadLevelRequest`, `CurrentLevelComponent`, and emits `EntitySpawnRequest` into `EntitySpawnSystem`.
- `rendering` — tile entities are sprites; the spawned entities need a renderable component stack (the `IEntityFactory`s registered against the spawn identifiers wire the actual `SpriteInfoComponent` / `DrawComponent`).

## Extension points

- **Custom field types.** `LDtkEntityParserSystem.ParseFieldValue` warns and returns raw values for unknown LDtk field types. Add a case there to recognize a new custom-field shape.
- **Per-entity behavior.** Register an `IEntityFactory` for each LDtk entity identifier (in `EntitySpawnSystem` from `level-loading`). The factory reads `EntitySpawnRequest.CustomFields` to apply per-instance configuration from the LDtk editor.
- **Multi-format dispatch (future).** The `Blender_` prefix dispatch is a known refactor candidate; the aspirational direction is a format field in `LoadLevelRequest` (or per-format registration) replacing the prefix hack.

## See also

- [Premises](premises.md) — load-bearing invariants (component-driven dispatch, tile/entity parser independence, LDtk's `layer.Visible` ≠ engine `VisibleComponent`, the content-pipeline DLL reference quirk, `Blender_` prefix routing around this block)
- Related blocks: `level-loading` (the plumbing this block plugs into), `level-blender` (the alternative parser; uses message-driven pattern — note the asymmetry), `rendering` (consumes the sprite entities spawned by tile parsing)
