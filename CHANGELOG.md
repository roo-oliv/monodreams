# Changelog

MonoDreams is in **alpha**: breaking changes land as clean breaks, with no
compatibility shims. Each entry below names the old shape, the new shape, and the
one-line edit that migrates a call site. Modules are source you own (shadcn-style),
so migrating is editing your own copy.

## Unreleased

### Added — scripted pointer replay in `debug` ([#90](https://github.com/roo-oliv/monodreams/issues/90))

`input_replay.json` speaks only named actions, so an entire genre — menus, business sims,
card games, editors — had no scripted-verification story. `PointerReplaySystem` adds the
pointer half: a `debug/pointer_replay.json` plan of `move` / `click` / `wheel` / `type` /
`waitUntil` / `label` commands in authoring-space coordinates, counted in frames, file-gated
and auto-exiting on drain exactly like the input replay. It **injects into the real
`CursorInputComponent`**, so a scripted click exercises the game's actual picking / focus / UI
path. Details in [`MonoDreams/debug/docs/overview.md`](MonoDreams/debug/docs/overview.md)
§ Pointer replay.

Nothing existing changes behaviour, but three source-owned modules gained surface:

- **`debug` now depends on `cursor`** (`module.json`), because the channel injects into
  `CursorInputComponent` rather than simulating one. `monodreams add debug` installs `cursor`
  too.
- **`Logger.LineSink`** (`foundation`) — a static, default-`null`, single-owner tap on every
  emitted message, invoked outside the writer lock. The socket lives in `foundation`, the plug
  in `debug` (the `waitUntil log` predicate), mirroring `GatedSystem.TimingSink`.
- **`TextInputSystem.KeyboardStateProvider`** (`ui`) — the repo's usual `Func<KeyboardState>`
  seam, defaulting to `Keyboard.GetState`, so a scripted `type` reaches a field through the
  system's own key diff.
- **`Cursor.ApplyPose`** (`cursor`) — the per-render-target cursor pose rule, extracted from
  `CursorPositionSystem` so the real mouse and an injected pointer place the cursor identically.
- **`ViewportManager.ScaleVirtualToScreenCoordinates`** (`rendering`) — the exact inverse of
  `ScaleMouseToVirtualCoordinates`, so an injection channel can fill
  `CursorInputComponent.ScreenPosition` in the backbuffer pixels that field means (chrome
  hit-tests read it raw, and it stays right at `DevicePixelRatio` 2).
- **`GameTestRunner.RunAsync(…, pointerPlan:)`** writes the plan into the run's debug dir.

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
