# level-ldtk — premises

> Technical invariants the engine assumes about the LDtk level-loader
> module: `LDtkLevelLoadSystem`, `LDtkLevelDataComponent`,
> `LDtkSpawnFields`, `LDtkTileParserSystem` and `LDtkEntityParserSystem`.
> Read this before changing the loader or either parser, or wiring an
> LDtk-exported level into a screen.

## The LDtk module is import-only (not wired to live game boot)

Since PS5 the LDtk loader + parsers are **import machinery**, not live loaders. The shipped game boots native
`.mdscene` levels only (level-loading — "`LevelLoadRequestSystem` resolves `LoadLevelRequest`
native-only"); the LDtk path runs once, via the import op, to migrate an `.ldtk` level into a native
scene the game then owns. In the reference screen the whole module is composed only in `importMode` (the
export op), never at boot: `LDtkLevelLoadSystem` (this module's own `LoadLevelRequest` handler — issue #54)
plus both parsers plus `EntitySpawnSystem`, in place of the native `LevelLoadRequestSystem` the boot branch
composes. Nothing in `level-loading` reaches the LDtk content any more, so "import-only" is a structural
fact rather than a flag: an LDtk level loads exactly when a screen composes this module's loader. The parser
behaviour below (component-driven, `EntitySpawnRequest`-emitting) is otherwise unchanged. Their factories set
`SpriteInfoComponent.AssetKey` (the tileset content key) so the imported native scene re-loads the tiles by
key. Note: the LDtk `Level_0` is not yet migrated (its ~21k per-tile entities need a native tile-layer
batching primitive — a follow-up), so it is import-only and not offered by the reference menu.

**Why:** native `.mdscene` is the game's real level format; keeping this module as a one-way importer is
what closes the parser-asymmetry (CORE_TENETS §6/§10) while preserving the LDtk authoring path. Owning the
loader here — instead of a fallback branch inside the shared dispatcher — is what let `level-loading` become
LDtk-free.
**Breaks:** re-wiring the loader/parsers into a game-boot pipeline reopens the asymmetry; putting an LDtk
load branch back into `LevelLoadRequestSystem` re-couples every non-LDtk game to LDtk.
**Tests:** `MonoDreams.Tests/LevelEditor/LevelImporterTests.cs` (import → native round-trip);
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot`
(boot without this module fails loud) and `::ImportOp_StillParsesTilesAndSpawnsEntities` (the import
composition still parses tiles + spawns entities).
**Depends on:** level-loading — "`LevelLoadRequestSystem` resolves `LoadLevelRequest` native-only (fails
loud otherwise)" and "`level-loading` is LDtk-free; the dependency arrow points level-ldtk →
level-loading"; level-editor — "LDtk is import-only; the importer round-trips a parsed world
to native".

## `LDtkLevelDataComponent` is the LDtk module's own level singleton; both parsers subscribe to it being added

The full `LDtkLevel` payload lives on **`LDtkLevelDataComponent`**, a world-scoped singleton owned by this
module (issue #54 — the shared `CurrentLevelComponent` now carries only a `string LevelIdentifier`).
`LDtkTileParserSystem` and `LDtkEntityParserSystem` each call
`world.SubscribeWorldComponentAdded<LDtkLevelDataComponent>(...)` in their constructors — they do not
consume `LoadLevelRequest` directly. The dispatch chain is therefore: game code publishes
`LoadLevelRequest` → **`LDtkLevelLoadSystem`** (this module, composed in the import op) loads
`World/<identifier>` as an `LDtkLevel` and sets `LDtkLevelDataComponent` **plus** the decoupled
`CurrentLevelComponent(identifier)` and `CurrentBackgroundColorComponent` → both parsers fire on the
`LDtkLevelDataComponent` add event. A constructor-time check
(`if (_world.Has<LDtkLevelDataComponent>()) HandleLevelLoaded(...)`) also picks up the level if the world
already has one when the parser is registered. The tile parser additionally subscribes to the component
being **removed** to dispose its tracked tile entities.

**Why:** the parsers need the full LDtk model (layers, tilesets, entity instances), and that model cannot
live in `level-loading` without dragging LDtk into every consumer of the shared plumbing. Keying the
parsers on their own module's component keeps the component-driven pattern intact *and* keeps the shared
marker format-agnostic. Tests and tooling can still set the component manually and trigger a parse without
faking a `LoadLevelRequest`; the parsers stay decoupled from how the level arrived.
**Breaks:** keying the parsers on `CurrentLevelComponent` again would fire them for levels of any format
(a native `.mdscene` boot that set the marker would drive an LDtk parse over a null payload) and would need
the LDtk payload back in the shared component. If a future LDtk feature subscribes to `LoadLevelRequest`
directly instead, tests that bypass the message can't reach it, and a loader that sets the component
without publishing the message bypasses it too.
**Tests:**
`MonoDreams.Tests/LevelLdtk/LDtkEntityParserSystemTests.cs::EntityParser_OnLDtkLevelDataAdded_PublishesSpawnRequestsWithLdtkChannelFields`
(setting `LDtkLevelDataComponent` — no message, no content pipeline — drives the parse);
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::ImportOp_StillParsesTilesAndSpawnsEntities`
exercises the full loader → component → parsers → spawn chain end-to-end.
**Depends on:** level-loading — "Parsers are component-driven, not message-driven" and
"`CurrentLevelComponent` is a world-scoped singleton holding a plain string identifier".

## Layer-derived spawn data rides the `ldtk:` `CustomFields` channel

`EntitySpawnRequest` carries no LDtk type (issue #54 removed its `LayerInstance Layer` member), so the
per-layer values a factory needs travel in the request's `CustomFields` dictionary under keys owned by
the static **`LDtkSpawnFields`**: `LayerOpacity` = `"ldtk:layerOpacity"` (a `float`) and `GridSize` =
`"ldtk:gridSize"` (an `int`). Both parsers populate them on every request they publish; a factory reads
them through the key constants **with a safe default** (opacity `1f`, grid size `16`) so the same factory
also serves a spawn that came from somewhere else — the `prefab:` channel, a code-driven
`new EntitySpawnRequest(identifier, position)`, or a future format. The `ldtk:` prefix cannot collide with
a designer's own custom field: LDtk field identifiers cannot contain `':'`.

**Why:** the shared spawn message is the seam every format and every game factory meets at, so it must
stay free of any one format's types (level-loading — "`level-loading` is LDtk-free"). A namespaced
dictionary key is the framework's existing extension channel (`CustomFields` is already the designer's
per-entity config), so module-specific data needs no new mechanism and no new message shape.
**Breaks:** adding an LDtk-typed member back to `EntitySpawnRequest` re-couples `level-loading` — and
therefore every non-LDtk game — to the LDtk packages. Reading the channel keys without a default throws
(or silently zeroes the sprite's color, drawing nothing) on the first spawn that did not come from an
LDtk parse. Un-prefixed keys (`"layerOpacity"`) risk shadowing a designer field of the same name.
**Tests:**
`MonoDreams.Tests/LevelLdtk/LDtkEntityParserSystemTests.cs::EntityParser_OnLDtkLevelDataAdded_PublishesSpawnRequestsWithLdtkChannelFields`
(the emitted requests carry both `ldtk:` keys with the layer's values).
**Depends on:** level-loading — "`IEntityFactory.CreateEntity` receives a structured
`EntitySpawnRequest`".

## Tile parser and entity parser are independent systems both subscribing to the same component

`LDtkTileParserSystem` and `LDtkEntityParserSystem` are registered
separately and run independently — both subscribe to
`LDtkLevelDataComponent` added. The tile parser walks `LayerInstances`
of type `Tile` and `AutoLayer`, publishing one `EntitySpawnRequest`
per tile. The entity parser walks all layers' `EntityInstances`,
publishing one `EntitySpawnRequest` per entity. A screen that needs
both must register both; a screen that only needs entities (no tile
art) can omit the tile parser.

**Why:** decoupling tiles from entities lets a game customize each
independently. A puzzle game might want entity parsing only (board
state as entities, no tile background); a top-down game might want
both. Forcing one combined parser would couple them.
**Breaks:** if a future refactor merges them, a screen can no longer
opt out of tile parsing — every LDtk level processes the tile layers
even when the game doesn't render tiles.
**Tests:** partial — `MonoDreams.Tests/LevelLdtk/LDtkEntityParserSystemTests.cs`
drives the entity parser alone (no tile parser registered); the
integration test (`LDtkLevelTests::ImportOp_StillParsesTilesAndSpawnsEntities`)
registers both, so the isolated-*tile*-parser path is still unprotected.
**Depends on:** —

## LDtk's `layer.Visible` is **not** the engine's `VisibleComponent`

`LDtkTileParserSystem` reads `layer.Visible` from the LDtk layer data
to decide whether to publish tile spawn requests for that layer at
all. This is the LDtk editor's per-layer visibility toggle — a
parse-time filter on the level data. The engine's `VisibleComponent`
(in `rendering`) is a per-entity tag managed by `CullingSystem` at
render time. Despite the name collision, they have nothing to do with
each other.

**Why:** the LDtk layer toggle is a design-time convenience (hide a
debug-only layer in the editor and the parser respects it). The
`VisibleComponent` tag is a runtime culling signal. Conflating them
in a bulk rename or refactor would be a footgun — if a refactor
"renames `Visible` everywhere" naively, it might rename
`LDtkLayer.Visible` too and break the LDtk reader.
**Breaks:** a rename that hits `layer.Visible` would either
fail-to-compile (because `LDtkLayer` is a third-party type from
`LDtkMonogame`) or silently rebind the property — neither outcome is
desired.
**Tests:** none yet.
**Depends on:** rendering — "`VisibleComponent` is owned exclusively by
`CullingSystem`".

## LDtkMonogame is vendored as source, recompiled per backend via `$(MonoDreamsPlatform)`

The `LDtkMonogame` runtime + content pipeline are **vendored as source**
under `MonoDreams/level-ldtk/vendor/LDtkMonogame/` (from
`github.com/IrishBruse/LDtkMonogame`, MIT, pinned to tag 1.8.0 / commit
`4a652fb`; see `vendor/LDtkMonogame/LICENSE`). They are **not** consumed as
NuGet packages by the engine. The reason: the upstream packages reference
`MonoGame.Framework.DesktopGL` only, so there is no KNI/BlazorGL build on
NuGet — vendoring the source lets it recompile against whichever backend
`$(MonoDreamsPlatform)` selects (MonoGame for `desktop`, nkast.Xna.Framework.\*
for `web`), exactly like MonoDreams' own modules. `MonoDreams.csproj`
`ProjectReference`s the vendored *runtime* (`LDtk/LDtk.csproj`); the vendored
*content pipeline* (`LDtk.ContentPipeline/`) is built per-platform and
surfaced to MGCB by the consumer's `/reference:` line. Upstream's optional
example renderer (`Renderer/`) is excluded from compilation — it calls the
desktop-only `Texture2D.FromFile`, which KNI lacks, and MonoDreams renders
LDtk levels through its own parser systems + `MasterRenderSystem` instead.

**Why:** every *precompiled* third-party dep that links MonoGame needs a
KNI-built variant for the web backend; LDtkMonogame has none, so it is the
one dep MonoDreams owns as source (shadcn-style) and recompiles. Pinning the
version + commit lets a maintainer re-sync deliberately on upstream changes.
**Breaks:** if someone re-adds the NuGet `LDtkMonogame` package ref to the
engine, the web build pulls a DesktopGL-only assembly and fails to resolve
`Microsoft.Xna.Framework` against the nkast identity; if `Renderer/` is
re-included, the web build fails on `Texture2D.FromFile`. Bumping the
vendored source without updating the pinned version note (here +
`vendor/.../LDtk.csproj` `<LDtkVendoredVersion>` + `module.json`) silently
drifts the fork.
**Tests:**
`MonoDreams.Tests/IntegrationTests/KniBackendBuildTests.cs::VendoredLDtkRuntimeCompilesAgainstKniWebBackend`
(web recompile) and `::EngineCoreCompilesAgainstKniWebBackend` (core graph);
`LDtkLevelTests::ImportOp_StillParsesTilesAndSpawnsEntities` exercises the vendored
runtime end-to-end on desktop.
**Depends on:** foundation — "Engine source is backend/OS-agnostic".

## Consumers still surface the LDtk content-pipeline DLL to MGCB via `/reference:`

The LDtk file format is loaded via `_content.Load<LDtkFile>(...)` and
`_content.Load<LDtkLevel>(...)`, which require the `LDtkImporter` and
`LDtkProcessor` to be present at content-build time. MGCB runs as a
separate process from the consuming csproj and does not inherit its
references, so the consumer adds the LDtk content-pipeline DLL to
`MonoGameMGCBAdditionalArguments` with a `/reference:` line. For the
desktop reference app this still points at the NuGet
`LDtkMonogame.ContentPipeline` DLL; the web content build (Phase 3) points
at the vendored `LDtk.ContentPipeline` build output instead, built against
the KNI pipeline assemblies for BlazorGL-targeted XNB output. Either way the
`/reference:` mechanism is the same.

**Why:** `/reference:` arguments to MGCB are the supported way to surface
content-pipeline DLLs to the out-of-process builder. The same pattern
applies to `dialogue` (Yarn) and any future content-pipeline-using module.
**Breaks:** MGCB fails with `Importer LDtkImporter not found` at
content-build time. The fix is non-obvious because the csproj appears to
reference the assembly correctly — the issue is the MGCB-specific
`/reference:` line, and on web it must point at the KNI-built pipeline DLL,
not the desktop one.
**Tests:** none yet (content-pipeline failures fail the build, which
is its own protection; the desktop path is exercised by the Examples
content build that `LDtkLevelTests` depends on).
**Depends on:** —

## Open questions

- **Tile-layer ordering** — `LDtkTileParserSystem` walks
  `LayerInstances` in array order and assigns decreasing
  `LayerDepth` per layer. LDtk stores layers top-down (foreground
  first); the parser maps that to engine "high layer depth =
  closer". Is the relationship documented anywhere outside this
  comment? Probably not; promote to a premise if relied on.
- **Unknown LDtk field types** — `LDtkEntityParserSystem.ParseFieldValue`
  emits a warning and returns the raw value for unknown types.
  Whether the warning is reliable enough to detect schema drift is
  open.

## Aspirational direction

- Add streaming support for large LDtk worlds (multi-level
  concurrency from `level-loading`'s aspirational direction).

## Follow-up debt

The following premises currently have **Tests: none yet** (or partial
coverage):

- Tile parser and entity parser are independent systems both
  subscribing to the same component (the entity parser runs alone in
  `LDtkEntityParserSystemTests`; the isolated tile parser is unprotected)
- LDtk's `layer.Visible` is not the engine's `VisibleComponent`
- Consumers still surface the LDtk content-pipeline DLL to MGCB via
  `/reference:` (content build is its own protection; web path lands in
  Phase 3)
