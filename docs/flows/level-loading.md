---
flow: level-loading
covers:
  - MonoDreams/level-loading/**
sensitive: false
---

# Level load and entity spawn

Loading a level is a one-way message-and-component relay, never a direct call: game
code publishes a `LoadLevelRequest(levelIdentifier)`, a parser turns the file into a
stream of `EntitySpawnRequest` messages, and `EntitySpawnSystem` hands each request to
the `IEntityFactory` registered under its string `Identifier`. No stage knows the next
stage's concrete type — the parser doesn't know which factory exists, the factory
doesn't know which parser emitted the request, and the parsers don't know how the level
arrived. That indirection is what lets the same level data drive a gameplay build, a
render-only preview, and a headless test by swapping only the factory map.

> **Status — native-only boot, LDtk-free plumbing.** The game boot is **native-only**:
> `LevelLoadRequestSystem` resolves `LoadLevelRequest` to a native `.mdscene` (or fails loud).
> Since issue #54 this module contains **no LDtk code at all** — the LDtk loader, its level
> component, and its parsers live in `level-ldtk`, composed only in the reference screen's
> `importMode` (the export op), never at boot. The dependency arrow is level-ldtk →
> level-loading, never the reverse.

**Native-only — the unification.** At game boot `LevelLoadRequestSystem` is a
native-only dispatcher — its whole constructor is
`(World world, Func<string,bool>? tryLoadNativeScene = null)`. It calls `tryLoadNativeScene`
(built by `NativeLevelLoader.CreateProbe`, level-editor), which probes for a bundled
`Content/Levels/<id>.mdscene` via `TitleContainer` and, on a hit, loads it through `SceneReaderSystem`
(generalized off the editor-only `LoadSceneRequest`, so the same reader serves the game boot — in both
run modes, and with no editor composed) and returns `true`. **No native scene ⇒ it fails loud** (a
logged error, no entities) — there is no fallback branch left in the system. Loading an `.ldtk` file
is a *different* system in a *different* module: the import op composes `level-ldtk`'s
`LDtkLevelLoadSystem` **instead of** this one (both subscribe to `LoadLevelRequest`, so exactly one
belongs in a pipeline), plus the LDtk parsers + factories; the export op then hands the re-parsed
world to `LevelImporter`. This keeps one dispatch at boot — the native reader — with the LDtk path
confined to the import composition (CORE_TENETS §6/§10). The `tryLoadNativeScene` delegate is a
plain `Func<string,bool>` so `level-loading` never depends upward on `level-editor`; the manifest's
`startScene` (read at boot from the bundled `game.mdproj` via `TitleContainer`) drives the entry when
its native scene exists.

**The import path is component-driven** (and lives entirely in `level-ldtk` now).
`LDtkLevelLoadSystem` subscribes to `LoadLevelRequest`, loads `World/{identifier}` as an
`LDtkLevel`, and `world.Set`s its own `LDtkLevelDataComponent` singleton plus this module's
format-agnostic `CurrentLevelComponent(identifier)` and `CurrentBackgroundColorComponent`. The LDtk
parsers (`LDtkEntityParserSystem`, `LDtkTileParserSystem`) never see the message — they
`SubscribeWorldComponentAdded<LDtkLevelDataComponent>` and parse when that component appears (and
each backfills in its constructor if it is already present).

## Entities & lifecycle

- **`CurrentLevelComponent`** — a world-scoped singleton (`world.Set`/`world.Get`) carrying a plain
  `string LevelIdentifier`: the marker for "this level is current", not a payload. The native reader
  does not set it (it reconstructs entities from serialized components); the LDtk import loader sets
  it beside its own `LDtkLevelDataComponent`. The editor transport's Restart removes it. Re-loading a
  different level removes and re-adds it, re-triggering the added-subscribers.
- **Spawned entities** — created exclusively inside `IEntityFactory.CreateEntity(world,
  request)`, one factory invocation per `EntitySpawnRequest`. The request carries
  `Identifier`, `InstanceIid`, `Position`, `Size`, `Pivot`, `TilesetPosition`, and a
  `CustomFields` dictionary — the level designer's per-entity config channel **and** the seam
  format-specific data rides under a namespaced key (`level-ldtk` publishes
  `"ldtk:layerOpacity"` / `"ldtk:gridSize"` via `LDtkSpawnFields`; the LDtk-typed `Layer` member
  was removed in issue #54). The factory owns the full component stack of the entity it builds.
- **One emitter, one shape** — entities reach the world only via factories dispatched
  by `EntitySpawnSystem`; a format module's parser is the upstream emitter of spawn requests. A
  parser that creates entities inline (bypassing `EntitySpawnRequest`) is the
  un-enumerated writer to watch for.

## Invariants

Authoritative list in
[`MonoDreams/level-loading/docs/premises.md`](../../MonoDreams/level-loading/docs/premises.md);
the ones this flow's ordering and dispatch lean on:

- No LDtk type appears in `level-loading` source, and the module depends on no parser module.
  Format-specific data belongs on the format module's own component or under a namespaced
  `CustomFields` key.
- Parsers react to **their own module's** level component being **added**
  (`LDtkLevelDataComponent` for the LDtk parsers), not to `LoadLevelRequest`. Game code that
  subscribes to the message to react to a load races the dispatcher and may run before the level
  is actually loaded.
- `CurrentLevelComponent` is a single world singleton carrying a plain string identifier; a second
  one without removing the first leaves subscribers reading ambiguous state.
- Dispatch is by `Identifier` string: each `EntitySpawnRequest` routes to the factory
  registered under its identifier — stringly-typed and unchecked at compile time.
- An unregistered factory identifier logs a `Logger.Warning` and **silently drops** the
  spawn (refactor candidate — intended behavior is to throw).
- Native-only: `LevelLoadRequestSystem` probes a bundled `Content/Levels/<id>.mdscene`
  (`TitleContainer`, source-first under a resolved editor project) and fails loud on a miss —
  there is no second attempt to clean up after. Native scenes are `/copy:`-bundled and
  read via `TitleContainer` (console-portable); only the desktop editor writes them.

## Load-bearing quantities

This flow is structural, not numeric — it routes messages and dispatches by string key
rather than computing values. The only "quantities" are identity strings: the level
identifier (the `Content/Levels/{identifier}.mdscene` scene path natively, `World/{identifier}`
on the LDtk import path) and the factory `Identifier` dictionary key. Both are exact-match — a
typo or a renamed identifier misses (fail-loud on the level id) or drops (silently, on the
factory key), it does not throw.

## Failure modes

- **Unregistered / misspelled factory identifier** — `EntitySpawnSystem` warns and drops
  the spawn; the entity is silently absent. The dev sees nothing at a location, hunts
  level data, and finds a buried `Logger.Warning` hours later. Highest-frequency real bug
  in this flow, and the one the backlog wants converted to a throw.
- **Parser subscribing to `LoadLevelRequest` directly** — a new parser that subscribes to
  the message instead of following the component-driven pattern can't be triggered by a
  test that sets the level component, and runs before the level state exists if it races
  the dispatcher. The pattern divergence is the bug.
- **Two dispatchers composed at once** — `LevelLoadRequestSystem` and a format loader
  (`LDtkLevelLoadSystem`) both subscribe to `LoadLevelRequest`; composing both means the
  native-only one logs a fail-loud error for every id the format loader handles fine (and, in
  the reverse order, an LDtk parse over a level that also has a native scene).
- **Stale singleton on re-load / hot reload** — re-adding a level component over an
  already-loaded level re-triggers added-subscribers but leaves the prior level's entities
  in an undefined state (acknowledged gap; no `UnloadLevelRequest` exists).
- **Factory ignoring `CustomFields`** — the level loads and entities spawn, but the
  designer's per-entity tuning is silently discarded; every entity comes out default-shaped.
  A factory that reads a namespaced channel key (`ldtk:layerOpacity`) **without a default**
  fails the other way: it throws, or zeroes the sprite color, on the first spawn that did not
  come from that format's parser.
