# level-loading — premises

> Technical invariants the engine assumes about the shared level/spawn
> plumbing: `LoadLevelRequest`, `CurrentLevelComponent`,
> `LevelLoadRequestSystem`, `EntitySpawnRequest`, `EntitySpawnSystem`,
> and `IEntityFactory`. Parser-specific invariants live in the
> `level-ldtk` module; this file covers the
> contract every parser ships into.

## `level-loading` is LDtk-free; the dependency arrow points level-ldtk → level-loading

No LDtk type appears anywhere in `level-loading` source (issue #54). `CurrentLevelComponent`
holds a plain `string LevelIdentifier`; `EntitySpawnRequest` has no `LayerInstance` member;
`LevelLoadRequestSystem` takes no `ContentManager` and has no `Content.Load<LDtkLevel>` path.
The arrow is one-way and stays that way: **`level-ldtk` depends on `level-loading`, never the
reverse**. Everything LDtk-shaped that used to ride the shared plumbing lives one module over —
the full `LDtkLevel` payload on `level-ldtk`'s `LDtkLevelDataComponent`, the import-path loader in
its `LDtkLevelLoadSystem`, and layer-derived per-spawn data in `EntitySpawnRequest.CustomFields`
under the `ldtk:`-prefixed keys of `LDtkSpawnFields`. A game that never installs `level-ldtk`
therefore never compiles against LDtk and never ships its packages.

**Why:** `level-loading` is the plumbing *every* level format rides — native `.mdscene` levels
authored in the editor, LDtk imports, and any future parser. A game whose levels are native (the
shipped path, see the native-only premise below) must be able to install the plumbing without
taking on `LDtkMonogame`, its content-pipeline DLL, and its `/reference:` MGCB wiring. The module
boundary is also what keeps the import-only story honest: if the shared contract still named an
LDtk type, "import-only" would be a composition convention rather than a structural fact.
**Breaks:** reintroducing an LDtk type into `level-loading` (a typed member on
`EntitySpawnRequest`, an `LDtkLevel` payload back on `CurrentLevelComponent`, a `Content.Load`
branch in `LevelLoadRequestSystem`) re-couples every non-LDtk game to the LDtk packages, pulls a
DesktopGL-only dependency into the web graph, and reopens the parser-asymmetry the native-only
boot closed. Module-specific data belongs on that module's own component or in the
`CustomFields` channel.
**Tests:**
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot`
(an unmigrated LDtk id fails loud at boot — the LDtk loader is not composed there) and
`::ImportOp_StillParsesTilesAndSpawnsEntities` (the import op, which *does* compose the LDtk
module, still parses tiles and spawns entities across the decoupled seam).
**Depends on:** level-ldtk — "`LDtkLevelDataComponent` is the LDtk module's own level singleton;
both parsers subscribe to it being added" and "Layer-derived spawn data rides the `ldtk:`
`CustomFields` channel".

## `LevelLoadRequestSystem` resolves `LoadLevelRequest` native-only (fails loud otherwise)

`LevelLoadRequestSystem` is the load dispatcher on `LoadLevelRequest`, and it is
**native-only, unconditionally** — its whole constructor is
`(World world, Func<string,bool>? tryLoadNativeScene = null)`. The composed
`tryLoadNativeScene` `Func<string,bool>` (built by `NativeLevelLoader.CreateProbe` in
`level-editor`) is called FIRST for every request. The probe resolves the scene
**source-first when an editor `EditorProjectContext` is resolved** (UX-D): a resolved
context + an existing source `<LevelsPath>/<id>.mdscene` publishes
`LoadSceneRequest(sourcePath, fromContent:false)`; otherwise it probes the bundled
`Content/Levels/<id>.mdscene` via `TitleContainer` (`fromContent:true`, the console-portable
read). A **null** context (a shipped / console / web build) skips the source branch entirely,
so the bundled path is byte-identical to before. On a hit it loads through the native reader
(`SceneReaderSystem`, generalized off the editor-only `LoadSceneRequest`) and returns `true` —
`LevelLoadRequestSystem` then returns. **No native scene ⇒ it fails loud** (a logged error, no
entities): there is no LDtk branch left in this system to fall back to. Loading an `.ldtk`
level is a job for a *different* system in a *different* module — the import op composes
`level-ldtk`'s `LDtkLevelLoadSystem` in place of this one (issue #54), so the import path is a
composition choice at the screen, not a flag on the shipped dispatcher. The delegate is a plain
`Func<string,bool>` so `level-loading` never depends upward on `level-editor`; the
native reader runs in BOTH run modes and in a plain game with no editor composed (a
shipped game boots native scenes too).

**Source-first is why Restart-after-Save is honest (UX-D, pre-mortem #5).** The transport's
Restart re-publishes the screen's original `LoadLevelRequest`, which goes through this same
probe. Without source-first the probe would read the **bundled** copy — stale the moment the
editor Saves to the source tree (the bundle only updates at the next build) — so a Restart would
silently revert to the *last build*, not the last save. The resolved-context source-first branch
makes the reload reflect the last SAVE. `CreateProbe` and the editor-bound-screen
`TryPublishSceneLoad(world, contentRoot, sceneId, projectContext)` optional load share ONE
source-first resolution (`NativeLevelLoader.TryPublishSourceFirst`), so the boot probe, the
Restart reload, and the bound-screen load all agree. `TryPublishSceneLoad` publishes
`LoadSceneRequest` **directly** (never through `LevelLoadRequestSystem`), so a bound screen
can load its scene without composing the dispatcher at all.

**Why:** native `.mdscene` is the game's real level format (the shipped game reads
bundled scenes via `TitleContainer`). A single native-only load path is what closed
the LDtk parser-asymmetry — the LDtk loader + parsers are import-only machinery in another
module now, off the boot path and out of this module's source (see "`level-loading` is
LDtk-free").
**Breaks:** if the dispatcher regrew a fallback branch on a native miss, the
asymmetry would reopen and an unmigrated id would load stale content instead of
surfacing the migration gap. If the delegate imported a `level-editor` type into
`level-loading`, the module layering inverts.
**Tests:**
`MonoDreams.Tests/IntegrationTests/NativeSceneBootTests.cs` (the real headless game
boots the committed `Levels/sample.mdscene` native),
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelBootsNative`
(the migrated `Blender_Level` boots native, no legacy parse), and
`MonoDreams.Tests/IntegrationTests/LDtkLevelTests.cs::UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot`
(no native scene ⇒ fail loud, no LDtk boot), plus in-process
`MonoDreams.Tests/LevelEditor/NativeFirstLoadTests.cs`
(`Probe_WithResolvedContext_ResolvesSourceFirst_PublishingTheSourcePath`,
`Probe_WithNullContext_SkipsSourceFirst_AndUsesTheBundledPathUnchanged`,
`StaleBundleRegression_ResolvedContextLoadsSource_UnresolvedLoadsBundled` — the source-first
probe + the pre-mortem #5 regression) and
`MonoDreams.Tests/LevelEditor/OptionalSceneLoadTests.cs` (the shared source-first helper).
**Depends on:** level-editor — "The game boots native scenes native-first via
LoadLevelRequest".

## Native `.mdscene` levels are bundled by an MGCB `/copy:` entry and read via `TitleContainer`

Native scene files live in the source content tree at `Content/Levels/<id>.mdscene`
(versioned in git). They are bundled to the title content by an MGCB `/copy:` entry
(the same raw-copy mechanism as `game.mdproj`); on build each
file lands at `<ContentRoot>/Levels/<id>.mdscene` (verified: desktop
`bin/…/Content/Levels/`; web `wwwroot/Content/Levels/`). The shipped game reads them
read-only through `TitleContainer.OpenStream(Path.Combine(ContentRoot, "Levels",
id + ".mdscene"))` — console-portable, never `System.IO.File`. Only the desktop
editor writes scenes (file IO into the source tree, PS3).

**Zero-touch for new levels (PS6).** MGCB's `.mgcb` is an explicit list with **no glob
syntax**, so a new level's `/copy:` line is added WITHOUT a human: on first Save the
editor appends it (see level-editor — "New levels bundle zero-touch: the editor appends
the MGCB `/copy:` entry on first save"; idempotent, desktop-editor-only). **Backups
(Save Backup As…, UX-D) are deliberately NOT bundled** — a backup is a dangling
`<name>.mdscene` written to `Content/Levels` for safekeeping, not a shippable level, so the
editor skips the `/copy:` append for it. It still shows in the Scenes panel (it lives under
`LevelsPath`) and loads source-first in the editor; it simply never joins the title content
until a designer promotes it (renames/re-saves it as a real scene, which then bundles). A build-time
`Content.npl` Nopipeline regen was rejected — a full regen sweeps the gitignored Island
placeholder-art pack into the MGCB texture build (the recursive `*.png` group) and breaks
a fresh checkout — so the `.npl` `Levels/*.mdscene` copy group is a **declarative record
only** here (Nopipeline is not wired to regenerate this project's hand-maintained `.mgcb`).
The `/copy:` entry is the one all-platform mechanism (a raw-copy `<None>`/`.targets` reaches
the desktop output but not the web `wwwroot/Content/`), so there is exactly one bundling
mechanism and no double-copy.

**Prefabs join the same mechanism (PF-C).** Native `.mdprefab` files live at
`Content/Prefabs/<id>.mdprefab` and bundle by the identical MGCB `/copy:` entry (appended
zero-touch by the editor on first Save-Prefab — `MgcbLevelBundle.EnsurePrefabCopyEntry`),
read the same way through `TitleContainer` (source-first in the editor via `PrefabFileSource`).
The Prefabs dir is simply a second `/copy:`-bundled content subtree beside Levels.

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

## `CurrentLevelComponent` is a world-scoped singleton holding a plain string identifier

Exactly one `CurrentLevelComponent` exists in the world at a time, and it carries a
**format-agnostic `string LevelIdentifier`** — the name of the level that is current, nothing
more (issue #54). It is the world-scoped marker for "this level is loaded", not a payload:
format-specific level data lives on the owning module's own component (`level-ldtk`'s
`LDtkLevelDataComponent` holds the full `LDtkLevel`). Loading a different level removes the
previous component and re-adds it with the new identifier. The native boot path does not set it
— the native reader reconstructs entities from serialized components and has nothing to
announce; the LDtk **import** loader sets it alongside its own level component. The editor
transport's Restart removes it as part of returning the world to its loaded state.

**Why:** a single-level invariant keeps subscriptions simple — a subscriber doesn't need to
disambiguate between levels. Holding only an identifier is what lets the component live in the
shared plumbing at all: an `LDtkLevel`-typed payload made every consumer of `level-loading`
compile against LDtk (see "`level-loading` is LDtk-free"). Multi-level support is an explicit
aspirational direction (see below).
**Breaks:** adding a second `CurrentLevelComponent` without removing the first leaves
subscribers reading ambiguous state; some may see the old identifier, some the new. Putting a
format-specific payload back on it re-couples the module to that format.
**Tests:** none yet.
**Depends on:** level-ldtk — "`LDtkLevelDataComponent` is the LDtk module's own level
singleton; both parsers subscribe to it being added".

## Parsers are component-driven, not message-driven

A parser subscribes to **its own module's level component being added** — it does not consume
`LoadLevelRequest` directly. The LDtk parsers (`LDtkEntityParserSystem`,
`LDtkTileParserSystem`) subscribe to `level-ldtk`'s `LDtkLevelDataComponent` (issue #54: the
component carrying the data they actually read moved into their module with them). This is the
engine-wide default: react to component lifecycle events when the work depends on persistent
state; reserve push messages for one-shot events (input, collision, screen transitions). All
shipping parsers — and the tile-grid bake, further down this file — follow the pattern.

**Why:** the component-driven pattern lets a test or tool set the level component manually and
trigger the parser without faking a message. The pattern generalizes — any system whose work
depends on a piece of persistent state should subscribe to its lifecycle rather than to a "go"
message. Keying on the parser's *own* component (rather than the shared
`CurrentLevelComponent`) is what lets the shared marker stay format-agnostic while the parser
still gets a lifecycle trigger carrying exactly the data it needs.
**Breaks:** a new parser that consumes `LoadLevelRequest` directly diverges from the pattern;
tests can't trigger it the standard way, and a loader that sets the level state without
publishing the message bypasses it. A parser keyed on `CurrentLevelComponent` instead of its own
component would fire on levels of a format it cannot read.
**Tests:**
`MonoDreams.Tests/LevelLdtk/LDtkEntityParserSystemTests.cs::EntityParser_OnLDtkLevelDataAdded_PublishesSpawnRequestsWithLdtkChannelFields`
(setting the component — no message — drives the parse).
**Depends on:** level-ldtk — "`LDtkLevelDataComponent` is the LDtk module's own level
singleton; both parsers subscribe to it being added".

## `EntitySpawnRequest` → `EntitySpawnSystem` → registered `IEntityFactory`

Parser systems emit `EntitySpawnRequest` messages instead of creating
entities directly. `EntitySpawnSystem` consumes each request and
dispatches to an `IEntityFactory` registered for the request's string
identifier. Game code registers factories at screen setup via
`EntitySpawnSystem.RegisterEntityFactory(identifier, factory)`.

There is also a **prefix channel** (`RegisterEntityFactoryPrefix(prefix,
factory)`): one factory serves every identifier beginning with a prefix,
so a family of dynamic ids routes to a single factory that parses the id
off the identifier. Exact-match registrations win; among prefixes the
LONGEST match wins (deterministic). The level-editor's `prefab:` channel
uses this: `EntitySpawnRequest("prefab:<id>", pos)` routes to the one
`PrefabFactory`, which spawns a full linked prefab instance through the
shared `PrefabExpander` (see level-editor — "Prefabs are LINKED
instances…"). An unknown prefab id warns-and-drops (this premise's
loud-warning convention).

**Why:** the indirection lets game code customize entity creation per
identifier without modifying the parsers. The same parser can drive a
"hit-test gameplay" build, a "render-only preview" build, and a
"physics-only headless test" build by swapping the factory map. The
prefix channel extends that to a family of dynamic ids (every prefab)
without a registration per id.
**Breaks:** a parser that creates entities directly couples to a
specific entity shape; a different game using the same parser must
fork the parser or post-process the entities.
**Tests:** `MonoDreams.Tests/LevelEditor/PrefabExpansionTests.cs`
(`EntitySpawnSystem_PrefixDispatch_RoutesPrefabRequestsToTheFactory`,
`Factory_UnknownPrefabId_WarnsAndDrops_NoThrow`); the exact-match spawn
path is still exercised indirectly via `InfiniteRunnerTests`.
**Depends on:** level-editor — "Prefabs are LINKED instances…" (the
`prefab:` prefix channel's factory + expander).

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

The request struct carries identifier, instance IID, position, size, pivot, tileset position,
and a `CustomFields` dictionary — **format-agnostic fields only** (issue #54 removed the
LDtk `LayerInstance Layer` member). Anything a specific level format needs to hand a factory
rides `CustomFields` under a namespaced key: `level-ldtk` publishes its layer-derived values
there (`LDtkSpawnFields.LayerOpacity` = `"ldtk:layerOpacity"`, `LDtkSpawnFields.GridSize` =
`"ldtk:gridSize"`), and a factory reads them with a safe default so the same factory also
serves a spawn that came from somewhere else. Factories that ignore `CustomFields` cannot be
configured from the level editor.

**Why:** the custom-fields dictionary is the level designer's configuration channel *and* the
extension seam for format-specific data — it is what lets the shared message stay free of any
one format's types (see "`level-loading` is LDtk-free"). A factory that ignores it accepts only
default-shaped entities — the editor can't tune them without a framework change.
**Breaks:** a level with per-entity tuning in custom fields produces identical, untuned entities
at runtime; the designer's intent is silently discarded. Adding a format-typed member back to
the struct instead of using the channel re-couples every consumer of `level-loading` to that
format. Reading a namespaced key without a default crashes on a spawn from another source.
**Tests:**
`MonoDreams.Tests/LevelLdtk/LDtkEntityParserSystemTests.cs::EntityParser_OnLDtkLevelDataAdded_PublishesSpawnRequestsWithLdtkChannelFields`
(the `ldtk:` channel is populated on the emitted requests).
**Depends on:** level-ldtk — "Layer-derived spawn data rides the `ldtk:` `CustomFields`
channel".

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
correct per-platform builder/processor wiring);
`MonoDreams.Cli.Tests/WebContentBuildTemplateTests.cs` (a scaffolded project
ships ONE `Content.mgcb` in Core that both heads build — `MonoGameContentReference`
from the desktop head, `KniContentReference` + the full BlazorGL shim from the
web head — and the emitted web block cannot drift from `MonoDreams.Demos.Web.csproj`);
the desktop content build is exercised end-to-end by `LDtkLevelTests` and
`BlenderLevelTests`.
**Depends on:** level-ldtk — "Consumers still surface the LDtk
content-pipeline DLL to MGCB via `/reference:`"; foundation — "The platform
(backend + OS services) is selected by the head project".

## The paint grid is authored cells + values; everything visible/collidable is a bake product

`TileGridComponent` (the LDtk-IntGrid analog, under `Component/Level/`) is the scene's paintable
logical grid: a sparse `Cells` map (packed signed cell → value id) plus the `TilePaintValue`
definitions (name, overlay color, collision layers/passivity/identity, tileset key, autotile rule
DSL, layer depth). The component is PURE AUTHORED DATA and serializes as `core.TileGrid`
(one-data-model — no special file block); the grid ENTITY's transform is the one anchor, with cell
(0,0)'s top-left sitting on it, so moving the entity slides the whole painted terrain. Everything
the player SEES or COLLIDES with is DERIVED: `TileGridBakeSystem` (level-editor module, beside the
scene reader it serves) disposes + re-creates `BakedProductComponent` children whenever the component
is added or changed — tile SPRITES whose source rect comes from the value's 4-bit same-neighbor
autotile rules (`TileGridBaking.NeighborMask` / `ParseRules` / `PickTile` — U=1, R=2, D=4, L=8, a bit
SET meaning that orthogonal neighbor holds the SAME value id; alternates picked by a deterministic
cell hash), and GREEDY-MERGED collider rectangles (`TileGridBaking.MergeRectangles` — never
per-cell: flush-adjacent colliders seam-catch swept AABBs). A game hook (`configureCollider`)
attaches gameplay components per paint value (a hazard marker on spike rects), so the module never
references a game type. The bake runs in BOTH the editor and the game — a loaded scene's grid bakes
at boot, before the first physics frame, because the scene reader ADDING the component is the bake
trigger (the component-lifecycle convention, not a message). Added events bake immediately; changed
events debounce `TileGridBakeSystem.QuietFrames` frames of silence, so a paint stroke does not thrash
thousands of entities per frame.

**Why:** painting logical cells that derive art + collision is the LDtk/Tiled workflow (colliders
separate from art, rules pick the tile); baking keeps the scene file small (cells, not thousands of
tile entities) and makes iteration free — replace the tileset PNG or edit a rule and the next bake
re-skins the world.
**Breaks:** serializing bake products doubles the world on every load (the writer's
`BakedProductComponent` exclusion is the guard); per-cell colliders seam-catch the swept AABB; a bake
that runs only in the editor ships a scene the game cannot collide with; a non-deterministic
alternate pick reshuffles the terrain's look on every repaint.
**Tests:** `MonoDreams.Tests/LevelEditor/TileGridBakingTests.cs` — the derivation maths
(`NeighborMask_SingleNeighbor_SetsExactlyItsBit`,
`NeighborMask_NeighborWithADifferentValue_DoesNotSetTheBit`,
`ParseRules_InteriorEntry_IsTheFallbackForUnmappedMasks`,
`ParseRules_GarbledEntries_AreSkippedWithoutThrowing`,
`PickTile_WithAlternates_IsDeterministicAcrossCallsAndRebuiltTables`,
`MergeRectangles_ProducesNoFlushAdjacentSeams`,
`MergeRectangles_CoversExactlyThePaintedCells`);
`MonoDreams.Tests/LevelEditor/TileGridBakeSystemTests.cs` — the bake products
(`ComponentAdded_Bakes_MergedColliderChildren_AtTheRectCentres`,
`ColliderIdentity_UsesEntityTypeWhenSet_ElseTheValueName`,
`ConfigureColliderCallback_IsInvokedOncePerBakedCollider_WithItsPaintValue`,
`ReBake_DisposesTheOldProducts_LeavingNoDuplicates`,
`ChangedGrid_ReBakesOnlyAfterTheQuietWindow`, `Bake_RunsInPlayMode_Too`);
`MonoDreams.Tests/LevelEditor/ComponentSerializerRegistryTest.cs::TileGrid_RoundTrips_ValuesAndCells_Canonically`
(only the authored cells + values persist, canonically and byte-stably).
**Depends on:** level-editor — "The editor Save writes versioned `.mdscene` into the project source
tree" (the grid is ordinary component state, so it needs no bespoke save path), "Trigger zones are
Passive colliders identified by an auto-numbered EntityInfo string" (the same collider naming +
passivity conventions), "Tile sprites stream per chunk; colliders bake whole" (what the bake emits
per frame); collision — "Overlapping bodies depenetrate; only separated ones sweep" (why one merged
rect per stretch beats many per-cell ones); this file — "Parsers are component-driven, not
message-driven" (the bake follows the same trigger convention).

## Scene layers are entities; member draw order derives from (layer order, within-layer key)

`SceneLayerComponent` makes the designer's LAYER an ordinary scene ENTITY (the camera
precedent): its name is its `EntityInfoComponent.Name`, its members are its `ChildOf`
children, its kind is DERIVED from what else the layer entity carries (today every layer is
a Sprites layer; a later tile-paint wave's marker component makes a layer a paint layer by
being present on it — there is no kind enum), and it serializes as `core.SceneLayer`
(`order`, `visible`, `locked`, `screenSpace`). The load-bearing depth rule: a member sprite's
persisted `SpriteInfoComponent.LayerDepth` is reinterpreted as its WITHIN-layer position
(0..1) and `SceneLayerSystem` — woven into the draw prep between `SpritePrepSystem` and
`YSortSystem` (the layer-depth ownership chain gains one stage) — computes the final
`DrawComponent.LayerDepth` as `slice.Min + key * slice.Width`, slicing
`BandMin..BandMax` (0.05..0.9) evenly across the layers by `Order` (ties by name, for
determinism). REORDERING layers therefore never rewrites member data (member depths are
layer-relative, so a reorder is a one-line diff); hiding a layer draws its members fully
transparent (post-prep color zero — no render-path or culling-query changes); entities on NO
layer pass through with their authored depths (full backward compatibility — legacy scenes
and code-built HUD/overlay entities are untouched). Membership is the whole `ChildOf`
ancestor chain (bounded by a cycle guard), so a prefab instance's sprites remap through their
instance root's layer. It runs in the editor AND the game (a hidden layer ships hidden). A
`screenSpace` layer (the HUD grouping) is organizational only: EXCLUDED from the band
slicing, so its members keep their own authored depths and the game's HUD pass is untouched.
Because `YSortSystem` runs AFTER this stage and keys its band lookup on the SOURCE
`SpriteInfoComponent.LayerDepth` (an exact-match `DrawLayerMap` lookup), a member whose
within-layer key happens to equal a registered Y-sorted band value is still Y-sorted —
`YSortSystem` keeps the last word, exactly as the ownership chain says.

**Why:** designer-created, renamable, reorderable layers are the Figma/Aseprite/LDtk model an
editor layer panel exposes; entity-membership via `ChildOf` gives free tree grouping,
rename-safety (references are entity links, not strings), serialization (the parent index
already round-trips), and lifecycle. Deriving the final depth per frame — instead of baking
it — is what keeps a reorder from touching member data.
**Breaks:** deriving member depths by REWRITING their source fields on reorder churns every
member row in the diff (the within-layer-key model is what keeps a reorder a one-line
change); a visibility mechanism that fights `CullingSystem` over `VisibleComponent` flickers
(hence the post-prep color zero); registering the system after `YSortSystem` would clobber
Y-sorted members' depths; writing the layer slice into `SpriteInfoComponent.LayerDepth`
instead of `DrawComponent.LayerDepth` would persist a derived value.
**Tests:** `MonoDreams.Tests/Rendering/SceneLayerSystemTests.cs` (band slicing +
within-layer key, hidden-layer transparency, screen-space exclusion, `ChildOf`-ancestor
membership, non-layered pass-through) and the `core.SceneLayer` round-trip in
`MonoDreams.Tests/LevelEditor/ComponentSerializerRegistryTest.cs`.
**Depends on:** rendering — "Layer-depth ownership pipeline" (the chain this system joins)
and "`VisibleComponent` is owned exclusively by `CullingSystem`" (why hiding is a color
zero, not a tag removal); level-editor — "The serializer persists SOURCE sort fields, never
the per-frame-derived `DrawComponent.LayerDepth`" and "Within-band ordering nudges SOURCE
sort fields and never breaks the band" (the within-layer nudges that keep working).

## Known limitations (acknowledged gaps)

- **Hot reload doesn't fully work** — re-adding a level component over an
  already-loaded level (`CurrentLevelComponent`, or `LDtkLevelDataComponent`
  in an LDtk import composition) partially re-triggers parsers but leaves
  the previous level's entities in unpredictable state.
  Needs attention before being relied on. *Status: known gap.*

## Open questions

- **Factory re-registration** — replacing a factory under the same
  identifier hasn't been tested. Behavior is undefined; if it becomes
  a use case, treat it as a framework change to think through.

## Aspirational direction

- **Throw on unregistered factory identifier** instead of warn-and-drop.
- **Multi-level concurrency** for streaming adjacent regions (e.g., a
  large overworld split into chunks loaded near the player).
- **Reliable hot reload** with explicit unload semantics.
- An explicit `UnloadLevelRequest` message rather than relying on the
  next load.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `CurrentLevelComponent` is a world-scoped singleton holding a plain string
  identifier (the singleton invariant itself; the string shape is covered
  indirectly by the LDtk decoupling tests)
- `EntitySpawnRequest` → `EntitySpawnSystem` → registered `IEntityFactory`
- Unregistered factory identifiers log a warning and silently drop the
  spawn

The `LoadLevelRequest` flow is the only one with happy-path test
coverage. Two architectural tests would protect the module boundary:
one asserting that no parser system subscribes directly to
`LoadLevelRequest` (the component-driven pattern), and one asserting
that no type in `MonoDreams/level-loading/` references `LDtk` (the
LDtk-free premise, enforced today only by review).
