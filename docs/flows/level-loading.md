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

The two parser entry points are **asymmetric by trigger**, and this is the load-bearing
subtlety. `LevelLoadRequestSystem` (the LDtk path) subscribes to `LoadLevelRequest`,
loads `World/{identifier}` as an `LDtkLevel`, and `world.Set`s the
`CurrentLevelComponent` singleton (plus `CurrentBackgroundColorComponent`). The LDtk
parsers (`LDtkEntityParserSystem`, `LDtkTileParserSystem`) never see the message — they
`SubscribeWorldComponentAdded<CurrentLevelComponent>` and parse when that component
appears (and each backfills in its constructor if the component is already present).
`BlenderLevelParserSystem` instead subscribes to `LoadLevelRequest` directly, gating on
`request.LevelIdentifier.StartsWith("Blender_")`, and parses straight from the message
without ever setting `CurrentLevelComponent`. Both message subscribers fire on every
load: a `Blender_`-prefixed identifier is handled by the Blender parser while
`LevelLoadRequestSystem` fails to find the LDtk file, logs an error, and removes
`CurrentLevelComponent` to clean up; any other prefix is handled by the LDtk path and
the Blender parser early-returns.

**Native-first (PS4) — the unification.** `LevelLoadRequestSystem` is now a native-first
dispatcher: before the LDtk attempt it calls an optional `tryLoadNativeScene`
`Func<string,bool>` (built by `NativeLevelLoader.CreateProbe`, level-editor). That delegate
probes for a bundled `Content/Levels/<id>.mdscene` via `TitleContainer` and, on a hit, loads
it through `SceneReaderSystem` (generalized off the editor-only `LoadSceneRequest`, so the
same reader serves the game boot — in both run modes, and with no editor composed) and returns
`true`; the dispatcher then returns immediately, **skipping the LDtk `Content.Load` and the
`CurrentLevelComponent` removal** so a native load is never clobbered. Only when no native file
exists does the legacy LDtk/Blender path run, unchanged. This is the content-driven dispatch the
`Blender_`-prefix hack always wanted: a single dispatcher decides native-vs-LDtk, native-first;
PS4 keeps LDtk + Blender as migration fallback, PS5 removes them and closes the asymmetry. The
delegate is a plain `Func<string,bool>` so `level-loading` never depends upward on `level-editor`;
the manifest's `startScene` (read at boot from the bundled `game.mdproj` via `TitleContainer`)
drives the entry when its native scene exists.

## Entities & lifecycle

- **`CurrentLevelComponent`** — a world-scoped singleton (`world.Set`/`world.Get`), the
  *only* state the LDtk path threads from request to parser. Created by
  `LevelLoadRequestSystem` on a successful LDtk load; removed on a failed/Blender load
  (and by `LDtkTileParserSystem` on its removal subscription). Re-loading a different
  level removes and re-adds it, re-triggering the added-subscribers. The Blender path
  produces no `CurrentLevelComponent` at all.
- **Spawned entities** — created exclusively inside `IEntityFactory.CreateEntity(world,
  request)`, one factory invocation per `EntitySpawnRequest`. The request carries
  `Identifier`, `InstanceIid`, `Position`, `Size`, `Pivot`, `TilesetPosition`, `Layer`,
  and a `CustomFields` dictionary (the level designer's per-entity config channel). The
  factory owns the full component stack of the entity it builds.
- **Two creators, one shape** — entities reach the world only via factories dispatched
  by `EntitySpawnSystem`; the LDtk and Blender parsers are the two upstream emitters of
  spawn requests. A parser that creates entities inline (bypassing `EntitySpawnRequest`)
  is the un-enumerated writer to watch for.

## Invariants

Authoritative list in
[`MonoDreams/level-loading/docs/premises.md`](../../MonoDreams/level-loading/docs/premises.md);
the ones this flow's ordering and dispatch lean on:

- LDtk parsers react to `CurrentLevelComponent` being **added**, not to
  `LoadLevelRequest`. Game code that subscribes to the message to react to a load races
  `LevelLoadRequestSystem` and may run before the level is actually loaded.
- `CurrentLevelComponent` is a single world singleton; a second one without removing the
  first leaves parsers reading ambiguous state.
- Dispatch is by `Identifier` string: parser→request by level prefix (`Blender_` vs
  not), request→factory by the registered identifier. Both keys are stringly-typed and
  unchecked at compile time.
- An unregistered factory identifier logs a `Logger.Warning` and **silently drops** the
  spawn (refactor candidate — intended behavior is to throw).
- Native-first: `LevelLoadRequestSystem` probes a bundled `Content/Levels/<id>.mdscene`
  (`TitleContainer`) **before** the LDtk attempt and short-circuits on a hit, so a native
  load is never followed by the LDtk remove-on-miss. Native scenes are `/copy:`-bundled and
  read via `TitleContainer` (console-portable); only the desktop editor writes them.

## Load-bearing quantities

This flow is structural, not numeric — it routes messages and dispatches by string key
rather than computing values. The only "quantities" are identity strings: the level
identifier (`World/{identifier}` content path + the `Blender_` prefix discriminator) and
the factory `Identifier` dictionary key. Both are exact-match — a typo or a renamed
level silently reroutes or drops, it does not throw.

## Failure modes

- **Unregistered / misspelled factory identifier** — `EntitySpawnSystem` warns and drops
  the spawn; the entity is silently absent. The dev sees nothing at a location, hunts
  level data, and finds a buried `Logger.Warning` hours later. Highest-frequency real bug
  in this flow, and the one the backlog wants converted to a throw.
- **Parser subscribing to `LoadLevelRequest` directly** — a new parser that copies the
  Blender path instead of the LDtk component-driven pattern can't be triggered by a test
  that sets `CurrentLevelComponent`, and runs before the level state exists if it races
  `LevelLoadRequestSystem`. The pattern divergence is the bug.
- **`Blender_` prefix collision** — naming an LDtk level `Blender_*` sends it to the
  Blender parser; the dual-subscriber design also means every Blender load logs an LDtk
  load error, which can mask a real LDtk failure. File naming is a load-time contract.
- **Stale singleton on re-load / hot reload** — re-adding `CurrentLevelComponent` over an
  already-loaded level re-triggers added-subscribers but leaves the prior level's entities
  in an undefined state (acknowledged gap; no `UnloadLevelRequest` exists).
- **Factory ignoring `CustomFields`** — the level loads and entities spawn, but the
  designer's per-entity tuning is silently discarded; every entity comes out default-shaped.
