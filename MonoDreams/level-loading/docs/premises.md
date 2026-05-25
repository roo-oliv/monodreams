# level-loading — premises

> Technical invariants the engine assumes about the shared level/spawn
> plumbing: `LoadLevelRequest`, `CurrentLevelComponent`,
> `LevelLoadRequestSystem`, `EntitySpawnRequest`, `EntitySpawnSystem`,
> and `IEntityFactory`. Parser-specific invariants live in the
> `level-ldtk` and `level-blender` blocks; this file covers the
> contract every parser ships into.

## `LoadLevelRequest` triggers `LevelLoadRequestSystem`, which adds `CurrentLevelComponent`

Game code publishes a `LoadLevelRequest` message.
`LevelLoadRequestSystem` consumes it, loads the file as an LDtk level,
and adds `CurrentLevelComponent` to the world (and
`CurrentBackgroundColorComponent` for the background color). The
component-driven parsers (`LDtkEntityParserSystem`, `LDtkTileParserSystem`)
subscribe to the component being added, not to the message.

**Why:** the separation lets parsers be ignorant of how the level
arrived — a test that adds `CurrentLevelComponent` manually triggers
parsing equivalently. This is the engine-wide pattern (see
"Parsers are component-driven").
**Breaks:** game code that subscribes to `LoadLevelRequest` to react
to a load competes with `LevelLoadRequestSystem`, possibly seeing the
message before the level is actually loaded.
**Tests:**
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelLoadsSuccessfully`
and
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::LDtkLevelLoadsSuccessfully`
exercise the happy path end-to-end.
**Depends on:** "Parsers are component-driven, not message-driven".

## `CurrentLevelComponent` is a world-scoped singleton

Exactly one `CurrentLevelComponent` exists in the world at a time.
Loading a different level removes the previous component and re-adds
with new data, re-triggering parsers.

**Why:** a single-level invariant keeps parser subscriptions simple —
they don't need to disambiguate between levels. Multi-level support
is an explicit aspirational direction (see below).
**Breaks:** adding a second `CurrentLevelComponent` without removing
the first triggers parsers against ambiguous state; some parsers may
see the old, some the new.
**Tests:** none yet.
**Depends on:** —

## Parsers are component-driven, not message-driven

The LDtk parsers (`LDtkEntityParserSystem`, `LDtkTileParserSystem`)
subscribe to `CurrentLevelComponent` being added — they do not consume
`LoadLevelRequest` directly. This is the engine-wide default: react to
component lifecycle events when the work depends on persistent state;
reserve push messages for one-shot events (input, collision, screen
transitions). The Blender parser is currently an exception (see the
"`Blender_` identifier prefix" premise below); the long-term direction
is to move it onto the component-driven path too.

**Why:** the component-driven pattern lets a test or tool add
`CurrentLevelComponent` manually and trigger the parsers without
faking a message. The pattern generalizes — any system whose work
depends on a piece of persistent state should subscribe to its
lifecycle rather than to a "go" message.
**Breaks:** a new parser that consumes `LoadLevelRequest` directly
diverges from the pattern; tests can't trigger it the standard way,
and game code that adds the level state without publishing the
message bypasses it.
**Tests:** none yet.
**Depends on:** —

## `Blender_` identifier prefix dispatches to the Blender parser

The Blender parser (`level-blender` block) subscribes directly to
`LoadLevelRequest` and processes it only when `request.LevelIdentifier`
starts with the string `Blender_`; `LevelLoadRequestSystem` also
subscribes and unconditionally attempts to load the same identifier as
an LDtk level, which fails for Blender names and produces a logged
error plus an explicit `world.Remove<CurrentLevelComponent>()`. So the
practical effect is: Blender prefix → Blender parser handles the load
and the LDtk path no-ops out; any other prefix → LDtk path handles the
load. *Status: refactor candidate — this is a quick hack.*

**Why:** the dispatch landed as a quick path for the Blender export
plugin. The intended replacement is content-driven dispatch (a format
field inside the level data, or explicit per-format registration on the
loader).
**Breaks:** a developer naming an LDtk level with a `Blender_` prefix
sends it to the wrong parser. Renaming files becomes a load-time
contract. The dual-subscriber design also means the LDtk path always
logs an error for Blender loads, which can mask real failures.
**Tests:** none yet.
**Depends on:** level-blender — "`Blender_` prefix is the parser's opt-in
hook".

## `EntitySpawnRequest` → `EntitySpawnSystem` → registered `IEntityFactory`

Parser systems emit `EntitySpawnRequest` messages instead of creating
entities directly. `EntitySpawnSystem` consumes each request and
dispatches to an `IEntityFactory` registered for the request's string
identifier. Game code registers factories at screen setup via
`EntitySpawnSystem.RegisterEntityFactory(identifier, factory)`.

**Why:** the indirection lets game code customize entity creation per
identifier without modifying the parsers. The same parser can drive a
"hit-test gameplay" build, a "render-only preview" build, and a
"physics-only headless test" build by swapping the factory map.
**Breaks:** a parser that creates entities directly couples to a
specific entity shape; a different game using the same parser must
fork the parser or post-process the entities.
**Tests:** none yet (the test suite exercises the spawn path
indirectly via `BlenderLevelTests` and `InfiniteRunnerTests`).
**Depends on:** —

## Unregistered factory identifiers log a warning and silently drop the spawn

`EntitySpawnSystem` writes a `Logger.Warning("No factory registered for
entity type ...")` and drops the spawn if no factory is registered for
the request's identifier. *Status: refactor candidate — intended
behavior is to throw.*

**Why:** the current silent-drop was a development convenience that
became a footgun. A misspelled identifier or a forgotten registration
makes entities silently absent, producing the same class of bug as
the missing-`VisibleComponent` story but harder to diagnose.
**Breaks:** dev loads a level, sees no entity at a location, hunts
through level data, finally finds a `Logger` warning hours later. The
intended throw moves the failure to load time.
**Tests:** none yet.
**Depends on:** —

## `IEntityFactory.CreateEntity` receives a structured `EntitySpawnRequest`

The request struct carries identifier, instance IID, position, size,
pivot, tileset position, layer, and a `CustomFields` dictionary.
Factories that ignore `CustomFields` cannot be configured from the
level editor.

**Why:** the custom-fields dictionary is the level designer's
configuration channel. A factory that ignores it accepts only
default-shaped entities — the editor can't tune them without a
framework change.
**Breaks:** a level with per-entity tuning in custom fields produces
identical, untuned entities at runtime; the designer's intent is
silently discarded.
**Tests:** none yet.
**Depends on:** —

## Known limitations (acknowledged gaps)

- **Hot reload doesn't fully work** — adding `CurrentLevelComponent`
  again over an already-loaded level partially re-triggers parsers
  but leaves the previous level's entities in unpredictable state.
  Needs attention before being relied on. *Status: known gap.*

## Open questions

- **Factory re-registration** — replacing a factory under the same
  identifier hasn't been tested. Behavior is undefined; if it becomes
  a use case, treat it as a framework change to think through.

## Aspirational direction

- **Content-driven format dispatch** instead of identifier-prefix
  hack — a format field in the level data, or explicit per-format
  registration on the loader.
- **Move the Blender parser onto the component-driven path** so all
  parsers share the same lifecycle hook.
- **Throw on unregistered factory identifier** instead of warn-and-drop.
- **Multi-level concurrency** for streaming adjacent regions (e.g., a
  large overworld split into chunks loaded near the player).
- **Reliable hot reload** with explicit unload semantics.
- An explicit `UnloadLevelRequest` message rather than relying on the
  next load.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `CurrentLevelComponent` is a world-scoped singleton
- Parsers are component-driven, not message-driven
- `Blender_` identifier prefix dispatches to the Blender parser
- `EntitySpawnRequest` → `EntitySpawnSystem` → registered `IEntityFactory`
- Unregistered factory identifiers log a warning and silently drop the
  spawn
- `IEntityFactory.CreateEntity` receives a structured
  `EntitySpawnRequest`

The `LoadLevelRequest` flow is the only one with happy-path test
coverage. An architectural test asserting that no parser system
subscribes directly to `LoadLevelRequest` would protect the
component-driven pattern (and would currently flag the Blender parser as
the lone exception).
