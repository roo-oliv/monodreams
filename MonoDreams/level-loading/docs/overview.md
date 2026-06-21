# level-loading — overview

Shared plumbing for loading levels and spawning entities: a `LoadLevelRequest` message, a parser-dispatch component (`CurrentLevelComponent`), and the `EntitySpawnRequest` → `EntitySpawnSystem` → `IEntityFactory` pipeline. Install this when a game uses any level-data format, then install a parser module (`level-ldtk` or `level-blender`) to actually read level files.

## Purpose

This module is the engine's "load a level" contract — the seam between level-data parsers (LDtk, Blender, future custom formats) and the game's entity-creation factories. It defines two distinct hops:

1. **Level loading:** publish `LoadLevelRequest`, `LevelLoadRequestSystem` resolves the file and adds `CurrentLevelComponent` to the world. Parsers subscribe to the component being added (component-driven, not message-driven), so tests and tooling can trigger parsing by adding the component directly.
2. **Entity spawning:** parsers emit `EntitySpawnRequest` messages instead of creating entities directly. `EntitySpawnSystem` routes each request to a string-keyed `IEntityFactory` registered at startup. The same parser can drive a gameplay build, a preview build, and a headless-test build by swapping the factory map.

Without this module, a parser has to know how to construct game-specific entities — coupling that this module exists to dissolve. Install this module alone and you have the plumbing but nothing to read level files; pair it with a parser module.

## What ships

### Components

- `CurrentLevelComponent` — world-scoped singleton holding the currently loaded level data; parsers subscribe to its add event
- `CurrentBackgroundColorComponent` — background color from the loaded level
- `TilemapLayerComponent` — tag for tilemap layer entities created during parsing

### Systems

- `LevelLoadRequestSystem` — subscribes to `LoadLevelRequest`, loads the LDtk file from content, sets `CurrentLevelComponent`
- `EntitySpawnSystem` — subscribes to `EntitySpawnRequest`, dispatches to the registered `IEntityFactory` for the request's identifier string. Today logs a warning and drops on unregistered identifiers (intended to throw — see premises)

### Messages

- `LoadLevelRequest` — `{ LevelIdentifier }`; game code publishes this to load a level
- `EntitySpawnRequest` — `{ Identifier, Iid, Position, Size, Pivot, TilesetPosition, Layer, CustomFields }`; parsers emit this per entity

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
   - `LevelLoadRequestSystem` first (consumes `LoadLevelRequest`).
   - Parser systems from `level-ldtk` or `level-blender` (LDtk parsers subscribe to `CurrentLevelComponent` being added; the Blender parser subscribes to `LoadLevelRequest` directly — see the asymmetry under that module's overview).
   - `EntitySpawnSystem` (consumes `EntitySpawnRequest`s the parsers emitted).
3. **Trigger a load** anywhere in game code by publishing `world.Publish(new LoadLevelRequest("Level1"))`.

Identifiers starting with `Blender_` route to the Blender parser; everything else routes to LDtk (today's prefix-based dispatch — see premises for the refactor candidate).

## Cross-module dependencies

- `foundation` — uses the world, messages, and `EntityInfoComponent` on spawned entities.

## Extension points

- **New `IEntityFactory` per entity type.** This is the canonical extension point. A factory receives the structured `EntitySpawnRequest` (including `CustomFields` for per-instance configuration from the level editor) and returns the constructed entity.
- **Custom parsers.** Implement a system that subscribes to either `CurrentLevelComponent` added (preferred — component-driven) or `LoadLevelRequest` (message-driven; Blender's pattern), reads level data, and publishes `EntitySpawnRequest`s. No changes to this module needed.
- **Custom factory dispatch.** Today `EntitySpawnSystem` keys on the identifier string. A game with thousands of identifiers might prefer category-based dispatch — write a wrapper factory that switches internally.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (component-driven parser pattern, `CurrentLevelComponent` singleton, `Blender_` prefix dispatch, unregistered-factory warn-and-drop behavior)
- Related modules: `level-ldtk` (LDtk format parser; uses the component-driven pattern), `level-blender` (Blender JSON parser; uses message-driven pattern — the asymmetry is acknowledged as refactor candidate)
