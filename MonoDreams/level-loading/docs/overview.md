# level-loading — overview

Shared, format-agnostic plumbing for loading levels and spawning entities: a `LoadLevelRequest` message, the native-only `LevelLoadRequestSystem` dispatcher, a `CurrentLevelComponent` marker for the current level, and the `EntitySpawnRequest` → `EntitySpawnSystem` → `IEntityFactory` pipeline. Install this when a game uses any level-data format; install a parser module (`level-ldtk`) on top only if you also author levels in that format.

## Purpose

This module is the engine's "load a level" contract — the seam between level data (native `.mdscene` scenes, an imported format, a future custom one) and the game's entity-creation factories. **It contains no format-specific code**: no LDtk type appears anywhere in its source, and it never depends on a parser module — the arrow points parser → plumbing (issue #54). It defines two distinct hops:

1. **Level loading:** publish `LoadLevelRequest`; `LevelLoadRequestSystem` resolves the identifier to a bundled native `Content/Levels/<id>.mdscene` and loads it through the native reader, or **fails loud**. A format-specific loader (e.g. `level-ldtk`'s `LDtkLevelLoadSystem`) is a *different* system a screen composes *instead*, and it sets its own module's level component — the component-driven pattern, so tests and tooling trigger parsing by setting that component directly.
2. **Entity spawning:** parsers emit `EntitySpawnRequest` messages instead of creating entities directly. `EntitySpawnSystem` routes each request to a string-keyed `IEntityFactory` registered at startup. The same parser can drive a gameplay build, a preview build, and a headless-test build by swapping the factory map.

Without this module, a parser has to know how to construct game-specific entities — coupling that this module exists to dissolve. Install this module alone and you can boot native scenes; pair it with a parser module to also read that format's files.

## What ships

### Components

- `CurrentLevelComponent` — world-scoped singleton marking the current level by **string identifier** (`LevelIdentifier`); format-specific level data lives on the owning module's own component (`level-ldtk`'s `LDtkLevelDataComponent`)
- `CurrentBackgroundColorComponent` — background clear color, set by a loader that has one
- `TilemapLayerComponent` — tag for tilemap layer entities created during parsing

### Systems

- `LevelLoadRequestSystem` — subscribes to `LoadLevelRequest` and is **native-only**: `new LevelLoadRequestSystem(world, tryLoadNativeScene)` probes for a bundled `Content/Levels/<id>.mdscene` (source-first in the editor) and loads it through the native reader; an id with no native scene logs an error and loads nothing. No `ContentManager`, no format fallback
- `EntitySpawnSystem` — subscribes to `EntitySpawnRequest`, dispatches to the registered `IEntityFactory` for the request's identifier string (exact match first, then longest registered prefix). Today logs a warning and drops on unregistered identifiers (intended to throw — see premises)

### Messages

- `LoadLevelRequest` — `{ LevelIdentifier }`; game code publishes this to load a level
- `EntitySpawnRequest` — `{ Identifier, InstanceIid, Position, Size, Pivot, TilesetPosition, CustomFields }`; parsers emit this per entity. Format-specific extras ride `CustomFields` under a namespaced key (`level-ldtk` publishes `"ldtk:layerOpacity"` / `"ldtk:gridSize"` via `LDtkSpawnFields`)

### Factories

- `IEntityFactory` — interface implemented by game code: `Entity CreateEntity(World world, EntitySpawnRequest request)`

## Pipeline wiring

1. **Register factories at startup** — for every identifier your levels reference:
   ```csharp
   entitySpawnSystem.RegisterEntityFactory("Player", new PlayerFactory());
   entitySpawnSystem.RegisterEntityFactory("Wall", new WallFactory());
   // ... one per entity type
   ```
2. **Pipeline order** in the screen's update pipeline:
   - The load dispatcher first — `LevelLoadRequestSystem` for a native game boot, or a parser module's own loader (`LDtkLevelLoadSystem`) for an import pipeline. Compose exactly one: they both subscribe to `LoadLevelRequest`.
   - Parser systems from the format module (they subscribe to *their* level component being added).
   - `EntitySpawnSystem` (consumes `EntitySpawnRequest`s the parsers emitted).
3. **Trigger a load** anywhere in game code by publishing `world.Publish(new LoadLevelRequest("Level1"))`.

## Cross-module dependencies

- `foundation` — uses the world, messages, and `EntityInfoComponent` on spawned entities.
- No dependency on any parser module. `level-ldtk` depends on this module, never the reverse.

## Extension points

- **New `IEntityFactory` per entity type.** This is the canonical extension point. A factory receives the structured `EntitySpawnRequest` (including `CustomFields` for per-instance configuration from the level editor, and for a format module's namespaced channel) and returns the constructed entity.
- **Custom parsers / a new format.** Ship the format's own loader + level component in the format's own module: a system that subscribes to `LoadLevelRequest`, sets its component, and a parser that subscribes to that component being added and publishes `EntitySpawnRequest`s. No changes to this module needed — and format-specific per-spawn data goes in `CustomFields` under a `<format>:` key rather than as a new member on the shared message.
- **Custom factory dispatch.** Today `EntitySpawnSystem` keys on the identifier string. A game with thousands of identifiers might prefer category-based dispatch — write a wrapper factory that switches internally, or use the prefix channel.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (the LDtk-free module boundary, native-only load with fail-loud, component-driven parser pattern, `CurrentLevelComponent` singleton, unregistered-factory warn-and-drop behavior)
- Related modules: `level-ldtk` (import-only LDtk loader + parsers; uses the component-driven pattern), `level-editor` (writes the native `.mdscene` levels this module boots)
