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

> **PS5 status — native-only boot.** The game boot is **native-only**: `LevelLoadRequestSystem`
> resolves `LoadLevelRequest` to a native `.mdscene` (or fails loud). The legacy LDtk parser is
> **import-only** machinery, composed only in the reference screen's `importMode` (the export op), never
> at boot. The component-driven LDtk description below is therefore the **import path** now
> (and the historical live path); read it as "what runs when the import op re-parses a legacy level."

The legacy import path is **component-driven** (this machinery now lives only in the import
composition). `LevelLoadRequestSystem` (the LDtk path) subscribes to `LoadLevelRequest`,
loads `World/{identifier}` as an `LDtkLevel`, and `world.Set`s the
`CurrentLevelComponent` singleton (plus `CurrentBackgroundColorComponent`). The LDtk
parsers (`LDtkEntityParserSystem`, `LDtkTileParserSystem`) never see the message — they
`SubscribeWorldComponentAdded<CurrentLevelComponent>` and parse when that component
appears (and each backfills in its constructor if the component is already present). A load
whose `World/{identifier}` file is missing logs an error and removes `CurrentLevelComponent`
to clean up.

**Native-only (PS5) — the unification.** At game boot `LevelLoadRequestSystem` is a
native-only dispatcher: it calls the `tryLoadNativeScene` `Func<string,bool>` (built by
`NativeLevelLoader.CreateProbe`, level-editor), which probes for a bundled
`Content/Levels/<id>.mdscene` via `TitleContainer` and, on a hit, loads it through `SceneReaderSystem`
(generalized off the editor-only `LoadSceneRequest`, so the same reader serves the game boot — in both
run modes, and with no editor composed) and returns `true`. **No native scene ⇒ it fails loud** (a
logged error, no entities) — there is no silent LDtk attempt. The legacy path runs **only** when
a caller passes `enableLegacyLdtkFallback: true` — the import op's composition, which also composes the
LDtk parser + factories; the export op then hands the re-parsed world to `LevelImporter`. This keeps one
dispatch at boot — the native reader — with the legacy LDtk parser confined to the import op
(CORE_TENETS §6/§10). The delegate is a plain `Func<string,bool>` so
`level-loading` never depends upward on `level-editor`; the manifest's `startScene` (read at boot from
the bundled `game.mdproj` via `TitleContainer`) drives the entry when its native scene exists.

## Entities & lifecycle

- **`CurrentLevelComponent`** — a world-scoped singleton (`world.Set`/`world.Get`), the
  *only* state the LDtk path threads from request to parser. Created by
  `LevelLoadRequestSystem` on a successful LDtk load; removed on a failed load
  (and by `LDtkTileParserSystem` on its removal subscription). Re-loading a different
  level removes and re-adds it, re-triggering the added-subscribers.
- **Spawned entities** — created exclusively inside `IEntityFactory.CreateEntity(world,
  request)`, one factory invocation per `EntitySpawnRequest`. The request carries
  `Identifier`, `InstanceIid`, `Position`, `Size`, `Pivot`, `TilesetPosition`, `Layer`,
  and a `CustomFields` dictionary (the level designer's per-entity config channel). The
  factory owns the full component stack of the entity it builds.
- **One emitter, one shape** — entities reach the world only via factories dispatched
  by `EntitySpawnSystem`; the LDtk parser is the upstream emitter of spawn requests. A
  parser that creates entities inline (bypassing `EntitySpawnRequest`) is the
  un-enumerated writer to watch for.

## Invariants

Authoritative list in
[`MonoDreams/level-loading/docs/premises.md`](../../MonoDreams/level-loading/docs/premises.md);
the ones this flow's ordering and dispatch lean on:

- LDtk parsers react to `CurrentLevelComponent` being **added**, not to
  `LoadLevelRequest`. Game code that subscribes to the message to react to a load races
  `LevelLoadRequestSystem` and may run before the level is actually loaded.
- `CurrentLevelComponent` is a single world singleton; a second one without removing the
  first leaves parsers reading ambiguous state.
- Dispatch is by `Identifier` string: each `EntitySpawnRequest` routes to the factory
  registered under its identifier — stringly-typed and unchecked at compile time.
- An unregistered factory identifier logs a `Logger.Warning` and **silently drops** the
  spawn (refactor candidate — intended behavior is to throw).
- Native-first: `LevelLoadRequestSystem` probes a bundled `Content/Levels/<id>.mdscene`
  (`TitleContainer`) **before** the LDtk attempt and short-circuits on a hit, so a native
  load is never followed by the LDtk remove-on-miss. Native scenes are `/copy:`-bundled and
  read via `TitleContainer` (console-portable); only the desktop editor writes them.

## Load-bearing quantities

This flow is structural, not numeric — it routes messages and dispatches by string key
rather than computing values. The only "quantities" are identity strings: the level
identifier (`World/{identifier}` content path) and the factory `Identifier` dictionary key.
Both are exact-match — a typo or a renamed identifier silently misses or drops, it does not throw.

## Failure modes

- **Unregistered / misspelled factory identifier** — `EntitySpawnSystem` warns and drops
  the spawn; the entity is silently absent. The dev sees nothing at a location, hunts
  level data, and finds a buried `Logger.Warning` hours later. Highest-frequency real bug
  in this flow, and the one the backlog wants converted to a throw.
- **Parser subscribing to `LoadLevelRequest` directly** — a new parser that subscribes to
  the message instead of following the LDtk component-driven pattern can't be triggered by a
  test that sets `CurrentLevelComponent`, and runs before the level state exists if it races
  `LevelLoadRequestSystem`. The pattern divergence is the bug.
- **Stale singleton on re-load / hot reload** — re-adding `CurrentLevelComponent` over an
  already-loaded level re-triggers added-subscribers but leaves the prior level's entities
  in an undefined state (acknowledged gap; no `UnloadLevelRequest` exists).
- **Factory ignoring `CustomFields`** — the level loads and entities spawn, but the
  designer's per-entity tuning is silently discarded; every entity comes out default-shaped.
