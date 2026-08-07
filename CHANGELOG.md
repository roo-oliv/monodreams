# Changelog

MonoDreams is in **alpha**: breaking changes land as clean breaks, with no
compatibility shims. Each entry below names the old shape, the new shape, and the
one-line edit that migrates a call site. Modules are source you own (shadcn-style),
so migrating is editing your own copy.

## Unreleased

### Breaking — `level-loading` no longer depends on LDtk ([#54](https://github.com/roo-oliv/monodreams/issues/54))

`level-loading` is now format-agnostic: no LDtk type appears in its source, and the
dependency arrow points **level-ldtk → level-loading, never the reverse**. A game that
doesn't author levels in LDtk no longer compiles against `LDtkMonogame` or ships its
packages. Three public shapes changed.

- **`EntitySpawnRequest` lost its `LayerInstance Layer` member** (and the matching
  constructor parameter). Layer-derived data now rides `CustomFields` under the
  `ldtk:`-prefixed keys of `level-ldtk`'s `LDtkSpawnFields`.
  *Migration:* a `request.Layer._Opacity` read becomes
  `request.CustomFields[LDtkSpawnFields.LayerOpacity]` (float, default `1f`); a
  `request.Layer._GridSize` read becomes
  `request.CustomFields[LDtkSpawnFields.GridSize]` (int, default `16`). Read through
  `TryGetValue` + a type check + that default — a code-driven spawn (the lightweight
  `EntitySpawnRequest(identifier, position)` ctor, the `prefab:` channel) carries no
  `ldtk:` keys at all.

- **`LevelLoadRequestSystem` is native-only, unconditionally.** Its `ContentManager`
  parameter, the `enableLegacyLdtkFallback` flag, and the ~85-line LDtk
  `Content.Load<LDtkLevel>` fallback are gone; an unknown level id now fails loud
  (`Logger.Error`) with no silent fallback.
  *Migration:*
  `new LevelLoadRequestSystem(world, content, probe, enableLegacyLdtkFallback: false)`
  becomes `new LevelLoadRequestSystem(world, probe)`; the import op composes
  `level-ldtk`'s new `LDtkLevelLoadSystem(world, content)` **instead of** this system.

- **`CurrentLevelComponent` holds a string, not an `LDtkLevel`.** It stays the
  world-scoped marker for "the current level"; the LDtk payload moved into the module
  that reads it.
  *Migration:* `CurrentLevelComponent.LevelData` (`LDtkLevel`) becomes
  `CurrentLevelComponent.LevelIdentifier` (`string`); the LDtk payload lives on
  `level-ldtk`'s new `LDtkLevelDataComponent`, which is also what both LDtk parsers now
  subscribe to (previously `CurrentLevelComponent`).

### Added — `level-ldtk` owns the whole LDtk path ([#54](https://github.com/roo-oliv/monodreams/issues/54))

- `LDtkLevelDataComponent` — the module's own world singleton carrying the full
  `LDtkLevel`; the parsers' add-trigger.
- `LDtkLevelLoadSystem` — the import-path `LoadLevelRequest` handler: loads
  `World/<id>` and sets `LDtkLevelDataComponent` + `CurrentLevelComponent(id)` +
  `CurrentBackgroundColorComponent`.
- `LDtkSpawnFields` — the `ldtk:layerOpacity` / `ldtk:gridSize` `CustomFields` keys
  that replace `request.Layer` (LDtk field identifiers cannot contain `':'`, so they
  never collide with a designer's own fields).
