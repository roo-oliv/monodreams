# level-ldtk — premises

> Technical invariants the engine assumes about the LDtk level-loader
> module: `LDtkTileParserSystem` and `LDtkEntityParserSystem`. Read this
> before changing either parser or wiring an LDtk-exported level into a
> screen.

## Both parsers subscribe to `CurrentLevelComponent` being added, not to `LoadLevelRequest`

`LDtkTileParserSystem` and `LDtkEntityParserSystem` each call
`world.SubscribeWorldComponentAdded<CurrentLevelComponent>(...)` in
their constructors. They do not consume `LoadLevelRequest` directly.
The dispatch chain is therefore: game code publishes `LoadLevelRequest`
→ `LevelLoadRequestSystem` (in `level-loading`) loads the file and
calls `world.Set(new CurrentLevelComponent(...))` → both parsers fire
on the add event. A constructor-time check
(`if (_world.Has<CurrentLevelComponent>()) HandleLevelLoaded(...)`)
also picks up the level if the world already has one when the parser
is registered.

**Why:** the component-driven pattern lets tests and tooling add
`CurrentLevelComponent` manually and trigger parsing without faking a
`LoadLevelRequest`. Both parsers stay decoupled from the load
mechanism — they react to "a level is now current," regardless of how
it got there.
**Breaks:** if a future LDtk feature subscribes to `LoadLevelRequest`
directly instead, tests that bypass the message can't reach it, and
loaders that add the component without publishing the message bypass
it too.
**Tests:**
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::LDtkLevelLoadsSuccessfully`
exercises the full chain end-to-end.
**Depends on:** level-loading — "Parsers are component-driven, not
message-driven".

## Tile parser and entity parser are independent systems both subscribing to the same component

`LDtkTileParserSystem` and `LDtkEntityParserSystem` are registered
separately and run independently — both subscribe to
`CurrentLevelComponent` added. The tile parser walks `LayerInstances`
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
**Tests:** none yet (the integration test registers both, so the
isolated-tile-parser path is unprotected).
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
`LDtkLevelTests::LDtkLevelLoadsSuccessfully` exercises the vendored runtime
end-to-end on desktop.
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

## `Blender_` prefix routes around this module

Levels whose `LoadLevelRequest.LevelIdentifier` starts with `Blender_`
are intercepted by `BlenderLevelParserSystem` (in `level-blender`),
which subscribes to the message directly. `LevelLoadRequestSystem`
also fires for those identifiers but fails to load them as LDtk and
explicitly removes `CurrentLevelComponent` — so the LDtk parsers in
this module do not fire for Blender-prefixed identifiers. This is the
dispatch hack between the two parser modules; restated here from this
module's viewpoint so a consumer reading the LDtk premises knows why
some loads don't reach them.

**Why:** the prefix-based dispatch is a quick hack documented in
`level-loading`'s premises. Naming an LDtk level with a `Blender_`
prefix accidentally routes it to the wrong parser.
**Breaks:** a developer renames an LDtk level to `Blender_World1`,
expecting the LDtk parser to handle it. Nothing in this module fires;
instead the Blender parser logs "no file found" and the level loads
empty.
**Tests:** none yet.
**Depends on:** level-loading — "`Blender_` identifier prefix
dispatches to the Blender parser".

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
- A content-driven format dispatch (level data declares its format)
  that replaces the `Blender_` prefix hack so this module doesn't
  need the prefix-routing premise at all.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Both parsers subscribe to `CurrentLevelComponent` being added, not
  to `LoadLevelRequest` (indirectly exercised by
  `LDtkLevelTests.LDtkLevelLoadsSuccessfully`)
- Tile parser and entity parser are independent systems both
  subscribing to the same component
- LDtk's `layer.Visible` is not the engine's `VisibleComponent`
- Consumers still surface the LDtk content-pipeline DLL to MGCB via
  `/reference:` (content build is its own protection; web path lands in
  Phase 3)
- `Blender_` prefix routes around this module
