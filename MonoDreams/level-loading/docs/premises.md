# level-loading — premises

> Technical invariants the engine assumes about the shared level/spawn
> plumbing: `LoadLevelRequest`, `CurrentLevelComponent`,
> `LevelLoadRequestSystem`, `EntitySpawnRequest`, `EntitySpawnSystem`,
> and `IEntityFactory`. Parser-specific invariants live in the
> `level-ldtk` and `level-blender` modules; this file covers the
> contract every parser ships into.

## `LevelLoadRequestSystem`'s LDtk path adds `CurrentLevelComponent` (import-only now)

When `LevelLoadRequestSystem` is composed with `enableLegacyLdtkFallback: true`
(the **import** composition only — PS5), a `LoadLevelRequest` with no native scene
loads the file as an LDtk level and adds `CurrentLevelComponent` (and
`CurrentBackgroundColorComponent`). The component-driven parsers
(`LDtkEntityParserSystem`, `LDtkTileParserSystem`) subscribe to the component being
added, not to the message. At **game boot** this path does not run:
`enableLegacyLdtkFallback` is `false`, so an id with no native scene fails loud (see
the native-only premise below) — `CurrentLevelComponent` is never added at boot.

**Why:** the separation lets parsers be ignorant of how the level arrived — a test
that adds `CurrentLevelComponent` manually triggers parsing equivalently. This is
the engine-wide pattern (see "Parsers are component-driven"); it now serves the
import op, which re-parses a legacy level so the importer can serialize it to native.
**Breaks:** game code that subscribes to `LoadLevelRequest` to react to a load
competes with `LevelLoadRequestSystem`, possibly seeing the message before the level
is actually loaded.
**Tests:**
`MonoDreams.Tests/LevelEditor/LevelImporterTests.cs` (the import → native round-trip
over LDtk-/Blender-shaped worlds) and
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot`
(an unmigrated LDtk id fails loud at boot — the LDtk path is not wired there).
**Depends on:** "Parsers are component-driven, not message-driven".

## `LevelLoadRequestSystem` resolves `LoadLevelRequest` native-only (fails loud otherwise)

`LevelLoadRequestSystem` is the load dispatcher on `LoadLevelRequest`. At game boot
(`enableLegacyLdtkFallback: false`, the default) it is **native-only**: the composed
`tryLoadNativeScene` `Func<string,bool>` (built by `NativeLevelLoader.CreateProbe` in
`level-editor`) is called FIRST for every request; it probes for a bundled
`Content/Levels/<id>.mdscene` via `TitleContainer` (the console-portable read) and,
on a hit, loads it through the native reader (`SceneReaderSystem`, generalized off
the editor-only `LoadSceneRequest`) and returns `true` — `LevelLoadRequestSystem`
then returns. **No native scene ⇒ it fails loud** (a logged error, no entities), with
no legacy LDtk/Blender attempt. The legacy LDtk `Content.Load<LDtkLevel>` path runs
**only** when a caller explicitly opts in with `enableLegacyLdtkFallback: true` — the
import op's dedicated composition, never the shipped boot. The delegate is a plain
`Func<string,bool>` so `level-loading` never depends upward on `level-editor`; the
native reader runs in BOTH run modes and in a plain game with no editor composed (a
shipped game boots native scenes too).

**Why:** native `.mdscene` is the game's real level format (the shipped game reads
bundled scenes via `TitleContainer`, exactly like `blender_level.json`). A single
native-only boot path is what closes the LDtk-vs-Blender parser-asymmetry (the
`Blender_` premise) — the parsers are import-only machinery now, off the boot path.
**Breaks:** if the boot path silently fell back to LDtk on a native miss, the
asymmetry would reopen and an unmigrated id would load stale content instead of
surfacing the migration gap. If the delegate imported a `level-editor` type into
`level-loading`, the module layering inverts.
**Tests:**
`MonoDreams.Tests/IntegrationTests/NativeSceneBootTests.cs` (the real headless game
boots the committed `Levels/sample.mdscene` native),
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelBootsNative`
(the migrated `Blender_Level` boots native, no Blender parse), and
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot`
(no native scene ⇒ fail loud, no LDtk boot), plus in-process
`MonoDreams.Tests/LevelEditor/NativeFirstLoadTests.cs`.
**Depends on:** level-editor — "The game boots native scenes native-first via
LoadLevelRequest".

## Native `.mdscene` levels are bundled by an MGCB `/copy:` entry and read via `TitleContainer`

Native scene files live in the source content tree at `Content/Levels/<id>.mdscene`
(versioned in git). They are bundled to the title content by an MGCB `/copy:` entry
(the same raw-copy mechanism as `blender_level.json` / `game.mdproj`); on build each
file lands at `<ContentRoot>/Levels/<id>.mdscene` (verified: desktop
`bin/…/Content/Levels/`; web `wwwroot/Content/Levels/`). The shipped game reads them
read-only through `TitleContainer.OpenStream(Path.Combine(ContentRoot, "Levels",
id + ".mdscene"))` — console-portable, never `System.IO.File`. Only the desktop
editor writes scenes (file IO into the source tree, PS3).

**Zero-touch for new levels (PS6).** MGCB's `.mgcb` is an explicit list with **no glob
syntax**, so a new level's `/copy:` line is added WITHOUT a human: on first Save the
editor appends it (see level-editor — "New levels bundle zero-touch: the editor appends
the MGCB `/copy:` entry on first save"; idempotent, desktop-editor-only). A build-time
`Content.npl` Nopipeline regen was rejected — a full regen sweeps the gitignored Island
placeholder-art pack into the MGCB texture build (the recursive `*.png` group) and breaks
a fresh checkout — so the `.npl` `Levels/*.mdscene` copy group is a **declarative record
only** here (Nopipeline is not wired to regenerate this project's hand-maintained `.mgcb`).
The `/copy:` entry is the one all-platform mechanism (a raw-copy `<None>`/`.targets` reaches
the desktop output but not the web `wwwroot/Content/`), so there is exactly one bundling
mechanism and no double-copy.

**Why:** `TitleContainer` over `/copy:`-bundled data is the one read path that works
on DesktopGL, KNI/web, AND consoles (Switch/PS/Xbox sandbox arbitrary file IO). A
scene is data (JSON parsed at load), so `/copy:` (raw) is correct — no MGCB processor
needed; the assets a scene references still go through the real content pipeline.
**Breaks:** reading a scene via `System.IO.File` breaks on console/web; a scene file
placed outside the bundled content root (or not `/copy:`-listed) is invisible to
`TitleContainer` at runtime and the level fails to boot.
**Tests:**
`MonoDreams.Tests/IntegrationTests/NativeSceneBootTests.cs` (the boot fails unless the
sample is bundled where `TitleContainer` finds it),
`MonoDreams.Tests/LevelEditor/NativeFirstLoadTests.cs::CommittedSampleScene_MatchesTheCanonicalShape`
(the committed sample stays byte-locked to the canonical serializer),
`MonoDreams.Tests/LevelEditor/MgcbLevelBundleTests.cs::CommittedMgcb_HasACopyEntry_ForEveryCommittedLevel`
(every committed level is `/copy:`-listed, so the bundling config is correct).
**Depends on:** level-editor — "New levels bundle zero-touch: the editor appends the MGCB
`/copy:` entry on first save".

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

## `Blender_` identifier prefix is import-only (never on the game boot path)

The `Blender_` name-prefix dispatch survives **only inside the import op's
composition** (`importMode`), where `BlenderLevelParserSystem` subscribes to
`LoadLevelRequest` (processing ids starting with `Blender_`) alongside a
`LevelLoadRequestSystem` composed with `enableLegacyLdtkFallback: true` (the LDtk
path harmlessly logs an error for a Blender id). At **game boot** neither parser is
composed — the single native-only dispatcher decides everything — so this
dual-subscribe name-prefix hack **never runs on the live path**. *Status: the
parser-asymmetry is RESOLVED (PS5): the game boots native `.mdscene` only; the
parsers are import machinery. The hack is retired end-to-end when the parsers are
eventually deleted (the import op then moves to a standalone tool).*

**Why:** the dispatch landed as a quick path for the Blender export plugin. Native
`.mdscene` replaced it as the game's real format: a level id resolves to a native
scene at boot, and the legacy parsers only run once, via the import op, to migrate a
legacy level. A native id never starts with `Blender_`, so the two never conflict
even inside the import composition.
**Breaks:** if the parsers were re-wired to game boot, the asymmetry reopens (the
LDtk path would log spurious errors for Blender ids again, masking real failures).
**Tests:**
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot`
(no legacy dispatch at boot) and `MonoDreams.Tests/LevelEditor/LevelImporterTests.cs`
(the import path over LDtk-/Blender-shaped worlds).
**Depends on:** level-blender — "`Blender_` prefix is the parser's opt-in hook".

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

## Content is built per-platform from the same `.mgcb`; custom processors must match the backend's pipeline assemblies

A game's content (`.mgcb`) is **not** platform-specific — the same content
project builds for either backend. What changes is the *builder* and the
*pipeline assemblies* the custom processors link: the platform is supplied
to MGCB at build time, taken from the head, not hard-coded in the `.mgcb`.
A **desktop** head builds it with `MonoGame.Content.Builder.Task`
(`/platform:DesktopGL`) and surfaces custom-processor DLLs
(`KNI.Extended.Content.Pipeline` → MonoGame's; the vendored LDtk pipeline;
the Yarn importer) built against MonoGame's pipeline assemblies. A **web**
head builds the *same* `.mgcb` with KNI's builder
(`nkast.Xna.Framework.Content.Pipeline`, `/platform:BlazorGL`) and surfaces
the *same* custom processors recompiled against KNI's pipeline assemblies
via `MonoDreamsPlatform=web`, passed to the out-of-process MGCB with
`/reference:` lines. A custom processor must link the pipeline assemblies
**matching the output backend**: a desktop processor cannot produce
BlazorGL `.xnb` and vice versa. (Off-Windows the KNI MGCB also needs a
one-time native-lib shim — see the web-targeting guide — because its
NuGet ships Windows-only `FreeImage`/`freetype`; this is a tooling-host
gap, not a content-format difference.)

**Why:** the `.xnb` binary format is platform-tagged, and a content
processor runs *inside* the builder process, so it must speak the same
`Microsoft.Xna.Framework.Content.Pipeline` identity the builder uses. One
`.mgcb` driving two backends is what lets a multi-platform game ship a
single content source; the only per-platform inputs are the builder task
and the processor references.
**Breaks:** building the web `.xnb` with the desktop processor DLL (or
vice versa) fails MGCB at content-build time with an
importer/processor-not-found or assembly-load error; the failure is
non-obvious because the csproj appears to reference the assembly correctly
— the issue is the *backend* the processor was compiled against and the
`/reference:` line that surfaces it to the separate MGCB process. Pointing
the web build's `/reference:` at the desktop pipeline DLL produces
DesktopGL-tagged output that a BlazorGL runtime cannot load.
**Tests:**
`MonoDreams.Tests/IntegrationTests/KniBackendBuildTests.cs::VendoredLDtkRuntimeCompilesAgainstKniWebBackend`
(the LDtk content pipeline recompiles against the KNI backend);
`MonoDreams.Cli.Tests/ScaffolderPlatformTests.cs::MgcbEditor_AppendsOnlyEntriesForTargetPlatform`
and `MonoDreams.Cli.Tests/ManifestPlatformTests.cs` (the CLI emits the
correct per-platform builder/processor wiring); the desktop content build
is exercised end-to-end by `LDtkLevelTests` and `BlenderLevelTests`.
**Depends on:** level-ldtk — "Consumers still surface the LDtk
content-pipeline DLL to MGCB via `/reference:`"; foundation — "The platform
(backend + OS services) is selected by the head project".

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
