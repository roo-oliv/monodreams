# level-blender — overview

Load levels designed in Blender as JSON exported by the bundled Blender plugin (`Tools/blender_level_export.py`). `BlenderLevelParserSystem` reads `Content/blender_level.json`, walks the object hierarchy, and **creates entities directly** for each Blender object — customizing them through game-registered collection handlers (`RegisterCollectionHandler`), not the `EntitySpawnRequest`/`IEntityFactory` path the LDtk parsers use. Includes a `-collider` child-mesh convention that attaches a `ConvexColliderComponent` to the parent. Install this when 3D modeling tools fit the game's level-authoring workflow better than tile editors.

## Purpose

Blender as a level editor is appealing for games where geometry and arrangement matter more than tile-based layouts — physics-driven games, vector-art platformers, sandbox setups. The bundled exporter plugin converts Blender objects into a JSON document containing positions, dimensions, rotations, hierarchy, custom properties, and (optionally) per-vertex data for convex colliders. The parser walks that JSON and creates entities directly in the world — unlike the LDtk parsers it does **not** route through `EntitySpawnRequest`/`EntitySpawnSystem`/`IEntityFactory`; game code customizes entities by registering a handler per Blender collection (`RegisterCollectionHandler`).

**Design quirk:** this is the lone message-driven parser in the engine. Where the LDtk parsers subscribe to `CurrentLevelComponent` being added (the engine-wide pattern), `BlenderLevelParserSystem` subscribes directly to `LoadLevelRequest`. The asymmetry is acknowledged as a refactor candidate — tests that add `CurrentLevelComponent` manually trigger LDtk parsers but bypass this one. The prefix-based dispatch (`Blender_*` identifiers route here, everything else to LDtk) is similarly acknowledged as a quick hack.

## What ships

### Data

- `Level/BlenderLevelData.cs` — DTOs (`BlenderLevelData`, `BlenderObject`, `CustomProperty`, etc.) that map to the JSON schema. The contract between the exporter plugin and the parser

### Systems

- `BlenderLevelParserSystem` — subscribes to `LoadLevelRequest` (message-driven, unlike the LDtk parsers); processes only if `LevelIdentifier.StartsWith("Blender_")`. Reads `Content/blender_level.json`, deserializes into `BlenderLevelData`, walks objects (with collider-child pre-pass), and **creates entities directly via `_world.CreateEntity()`**, applying any handler registered for each object's Blender collection

### Tools

- `Tools/blender_level_export.py` — the Blender plugin (Edit → Preferences → Add-ons → Install from Disk). Adds File → Export → MonoDreams Level (.json). **Part of this module** — the exporter and parser are two halves of one schema contract; update them together

## Pipeline wiring

**Install the exporter** in Blender from `MonoDreams/level-blender/Tools/blender_level_export.py`.

**Authoring workflow.** Design the level in Blender. Group objects into Blender **collections** matching the handlers you register with `RegisterCollectionHandler` — each handler receives the created `Entity` plus its `BlenderObject` and attaches the game-specific components (the parser itself sets `TransformComponent` + `EntityInfoComponent` from the object name). For non-rectangular collision shapes, add a child mesh whose name ends with `-collider` (e.g. `Rock-collider`) — the parser will turn it into a `ConvexColliderComponent` on the parent instead of spawning a separate entity. Export to `Content/Levels/<name>.json`.

**MGCB entry.** Copy-only — no special importer needed:
```
#begin Levels/<name>.json
/copy:Levels/<name>.json
```

**Runtime wiring.** After `LevelLoadRequestSystem` in your screen pipeline:
```csharp
new BlenderLevelParserSystem(world, content)
```
Trigger a load by publishing `LoadLevelRequest("Blender_World1")` — the `Blender_` prefix is the parser's opt-in hook.

## Cross-module dependencies

- `level-loading` — provides `LoadLevelRequest`, which this parser subscribes to directly. Note it does **not** use the shared `EntitySpawnRequest`/`EntitySpawnSystem`/`IEntityFactory` path the LDtk parsers and runner use — it creates entities itself.
- `rendering` — Blender meshes become sprite entities; the parser sets `SpriteInfoComponent` / `DrawComponent` (and collider shapes) directly, and registered collection handlers can add or override components.
- `collision` — a child mesh whose name ends with `-collider` is turned into a `ConvexColliderComponent` on the parent (consumed by the `collision` module) instead of spawning a separate entity.

## Extension points

- **JSON schema evolution.** Update both the exporter (`bl_info.version` + the field set it writes) and `BlenderLevelData` (matching property set). The `version` field is intended for version enforcement but isn't checked today — see premises.
- **New Blender custom properties.** Add to `BlenderObject.CustomProperties` deserialization (a flexible dictionary today). A registered collection handler receives the `BlenderObject` and reads `CustomProperties` to per-tune entity construction.
- **Multi-file Blender projects (future).** Today the parser hardcodes `Content/blender_level.json` regardless of the level identifier. Multi-level support is on the aspirational direction list.
- **Schema versioning (future).** The exporter writes a `version` field; the parser doesn't read it. Adding the check is straightforward — see premises for the open question.

## See also

- [Premises](premises.md) — load-bearing invariants (`Blender_` prefix as the parser's opt-in hook, message-driven asymmetry vs LDtk, JSON-schema contract between exporter and parser, `-collider` suffix convention)
- Related modules: `level-loading` (the plumbing), `level-ldtk` (the component-driven alternative — the asymmetry between the two parsers is acknowledged and intentional pending refactor), `collision` (consumes the `ConvexColliderComponent`s produced by the `-collider` convention)
