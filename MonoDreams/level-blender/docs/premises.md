# level-blender — premises

> Technical invariants the engine assumes about the Blender level-loader
> module: `BlenderLevelParserSystem`, the `BlenderLevelData` /
> `BlenderObject` JSON schema, and the
> `Tools/blender_level_export.py` exporter plugin. Read this before
> changing the parser, the JSON schema, or the exporter plugin.

## The Blender parser is import-only (not wired to live game boot) — and is being RETIRED

Since PS5 `BlenderLevelParserSystem` is **import machinery**, not a live loader. The shipped game boots
native `.mdscene` levels only; the Blender parser runs once, via the import op, to migrate a
`blender_level.json` export into a native scene the game then owns. In the reference screen it is composed
only in `importMode` (the export op), never at boot — so the `Blender_` prefix dual-subscribe (below) no
longer runs on the live path. The Examples Blender level has been migrated to a committed
`Content/Levels/Blender_Level.mdscene` (the menu's "Level 1" boots it native, at scene **version 2**
after CE-B's `monodreams migrate-colliders`). The parser sets `SpriteInfoComponent.AssetKey` (from its
derived content path) so the imported native scene re-loads the GreasePencil textures by key.
Runtime-derived NPC affordances (the dialogue zone's live `Entity` icon reference + the icon's live font)
are excluded from the migrated scene — a follow-up.

> **RETIREMENT (user directive 2026-07-10).** Levels are authored in the MonoDreams level editor now, not
> imported from Blender — the Blender importer is slated for removal. In CE-B this meant **no new code was
> added to support it**: the collider-child production the other producers gained (LDtk factories, the
> committed scene migrated by `monodreams migrate-colliders`) was deliberately NOT wired into this parser
> (its box still embeds on the entity, correct only for a centered origin — see the `_ = boundsOffset` seam
> in `ProcessCollections`). The parser remains import-only and off the live boot path, so this is safe. Full
> deletion of the `level-blender` module (parser, the `Blender_` dispatch, the exporter plugin, Examples
> import wiring, this module's premises + the `LevelImporterTests` Blender-shaped fixture) is a tracked
> follow-up beyond CE-B's serialization scope.

**Why:** native `.mdscene` is the game's real level format; keeping the parser as a one-way importer is
what closes the parser-asymmetry (CORE_TENETS §6/§10) while preserving the Blender authoring path.
**Breaks:** re-wiring the parser to game boot reopens the asymmetry.
**Tests:** `MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelBootsNative` (the migrated
level boots native, no Blender parse); `MonoDreams.Tests/LevelEditor/LevelImporterTests.cs`,
`MonoDreams.Tests/LevelEditor/MigratedLevelTests.cs`.
**Depends on:** level-loading — "`LevelLoadRequestSystem` resolves `LoadLevelRequest` native-only (fails
loud otherwise)"; level-editor — "The Examples levels are migrated to native `.mdscene`".

## `Blender_` prefix is the parser's opt-in hook

`BlenderLevelParserSystem` subscribes to `LoadLevelRequest` and
processes the message only if `request.LevelIdentifier` starts with
the string `Blender_`. Any other identifier is ignored by this module.
That prefix is also what causes `LevelLoadRequestSystem` (in
`level-loading`) to fail the LDtk load path and remove
`CurrentLevelComponent`, leaving this module as the sole effective
loader for Blender levels. *Status: refactor candidate — this is a
quick hack.*

**Why:** the prefix is the only signal in `LoadLevelRequest` that
distinguishes Blender-from-LDtk. The two systems (Blender parser, LDtk
loader) both subscribe to the same message; the prefix is what gates
which one actually does work. The intended replacement is a format
field in `LoadLevelRequest` (or per-format registration on the loader)
so the prefix can go away.
**Breaks:** a Blender-exported level named without the prefix gets
ignored by this parser and falls into the LDtk path, which fails. A
non-Blender level with the prefix gets sent to this parser, which
tries to read `Content/blender_level.json` and either uses the wrong
data or fails.
**Tests:**
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelLoadsSuccessfully`
exercises the happy path end-to-end.
**Depends on:** level-loading — "`Blender_` identifier prefix
dispatches to the Blender parser".

## Parser is message-driven, unlike the LDtk parsers

`BlenderLevelParserSystem` is the lone exception to the engine-wide
"parsers are component-driven" pattern. It subscribes directly to
`LoadLevelRequest` instead of `CurrentLevelComponent` being added.
Consequently a test that adds `CurrentLevelComponent` manually triggers
the LDtk parsers but **not** this one — tests of Blender flow must
publish `LoadLevelRequest` explicitly.

**Why:** the parser predates the component-driven pattern. The
intended cleanup (also called out in `level-loading`'s aspirational
direction) is to move this parser onto the `CurrentLevelComponent`
hook so all parsers share the same lifecycle. Until then, this
discrepancy is itself a load-bearing premise: tests and tooling that
assume component-driven dispatch silently bypass Blender parsing.
**Breaks:** a test that swaps `LoadLevelRequest` for direct
`CurrentLevelComponent.Set` thinks it's exercising the parser when in
fact it's not — Blender entities never get created, and any assertion
on them silently fails as "no entities found."
**Tests:** none yet (the integration test publishes the message, so
the bypass path is not protected).
**Depends on:** level-loading — "Parsers are component-driven, not
message-driven".

## Parser depends on the JSON schema produced by the exporter plugin

The parser reads `Content/blender_level.json` and deserializes it into
`BlenderLevelData` (defined in `MonoDreams/level-blender/Level/BlenderLevelData.cs`).
That schema — `version`, `scaleFactor`, `collectionHierarchy`,
`objects[]` with `name`, `type`, `parent`, `collections`,
`collectionProperties`, `meshType`, `position`, `dimensions`, `scale`,
`rotation`, `originOffset`, `customProperties`, `uvMapping`,
`vertices` — is the contract between the exporter and the parser. The
exporter plugin must emit every field the parser reads, and the parser
must tolerate missing optional fields (`Dimensions`, `OriginOffset`,
etc., where it has fallbacks).

**Why:** the JSON format is the seam between the Blender-side
authoring tool and the engine-side runtime parser. Treating it as a
casual format invites drift: a Blender export omits a field, the
parser sees `null`, and entities load with wrong-but-plausible
defaults. The schema must be versioned (the `version` field is
present; the parser does not yet check it, but should).
**Breaks:** a field renamed in the exporter (e.g. `originOffset` to
`pivot`) but not in `BlenderLevelData` causes deserialization to
populate the old field with default and ignore the new one — entities
spawn with origins at `(0.5, 0.5)` regardless of the Blender artist's
intent.
**Tests:**
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelLoadsSuccessfully`
indirectly protects the field names by exercising end-to-end load
against a checked-in JSON file, but the schema-version invariant is
not exercised.
**Depends on:** —

## The exporter plugin is part of the module

`Tools/blender_level_export.py` ships in this module at
`MonoDreams/level-blender/Tools/blender_level_export.py`. It is the
Blender-side companion to `BlenderLevelParserSystem`. Updates to the
parser's expected JSON schema *must* be accompanied by matching updates
to the exporter, and vice versa — they are two halves of one contract.
The plugin's `bl_info.version` and the JSON `version` field are the
intended version signal.

**Why:** the plugin and parser are versioned together; a level
exported with plugin v1.7 may not parse correctly under a parser
expecting v1.8 schema fields. Treating the plugin as in-module (not as
external tooling) is what makes "update both at once" feel natural.
**Breaks:** a parser change that adds a required field without
updating the plugin produces JSON files missing that field; the parser
either deserializes to default or throws on `null`. A plugin change
that adds a new field without updating the parser produces JSON the
parser ignores — the Blender artist's intent is silently discarded.
**Tests:** none yet (the integration test uses a single fixed
`blender_level.json`; nothing exercises the schema-evolution path).
**Depends on:** —

## Blender level JSON is read as content, not host filesystem

`BlenderLevelParserSystem` reads `<ContentRoot>/blender_level.json` through
`TitleContainer.OpenStream` (the `Microsoft.Xna.Framework` content-stream
primitive that `ContentManager` itself uses), **not** through
`System.IO.File` or `IPlatformServices`. The JSON ships in the content
pipeline as a `/copy:` asset, so it sits beside the `.xnb` files under the
content root and resolves by the same relative path on every backend — a
file read on desktop, a synchronous HTTP fetch of the served asset on web
(BlazorGL). The distinction is deliberate: the level JSON is **read-only
game content**, not user data, so it travels the content path; user data
(settings, the debug input-replay plan) is what goes through
`IPlatformServices`, which has no readable disk on web.

**Why:** the browser sandbox has no readable host filesystem. The earlier
implementation read the JSON via `IPlatformServices.ReadAllText` off an
absolute disk path; `WebPlatformServices.ReadAllText` returns empty there,
so deserialization produced no objects and the Blender level loaded with
**zero entities on web** while working on desktop — a silent, platform-split
failure. Routing the read through `TitleContainer` is what makes the same
content load on both backends.
**Breaks:** reverting to `File.ReadAllText` / `IPlatformServices.ReadAllText`
for the JSON makes the Blender level silently empty on web again (no
exception — just no entities). Conversely, making `blender_level.json` a
processed `.xnb` asset (instead of `/copy:`) would break the raw-stream read
unless the path/extension is updated to match.
**Tests:**
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::BlenderLevelLoadsSuccessfully`
exercises the `TitleContainer` read path end-to-end on desktop (it asserts
"objects from Blender level" / "entities from Blender level" appear, which
only happens if the stream opened and deserialized). The web fetch path is
verified manually in-browser (Examples.Web "Level 2").
**Depends on:** foundation — "Engine source is backend/OS-agnostic —
non-portable calls go through `IPlatformServices`".

## Collider-child convention: `-collider` suffix attaches a ConvexCollider to the parent

Child meshes whose `name` ends with `-collider` (e.g. `Rock-collider`)
are not spawned as their own entities. The parser collects them
during pre-scan into `colliderChildMap` (keyed by parent name), skips
them during normal entity creation, then in a post-pass converts their
vertex data into a `ConvexColliderComponent` attached to the parent
entity (replacing any default `BoxColliderComponent` the parent's
collections produced). This is the supported way to give non-rectangular
Blender objects accurate SAT collision shapes.

**Why:** Blender's UI doesn't natively express "this mesh's silhouette
is the collision shape of this other mesh." The `-collider` suffix is
a naming convention the exporter and parser both honor; the artist
designs a low-poly collision mesh as a child of the visual mesh, names
it with the suffix, and gets a SAT-ready collider attached.
**Breaks:** a typo in the suffix (`-collide` or `_collider`) makes the
parser treat the mesh as a regular entity — it spawns as its own
visible sprite with no collision-shape behavior, and the parent keeps
its default `BoxColliderComponent`. The bug is visible (an extra
sprite appears) but the consequence (no SAT collision) is silent.
**Tests:** none yet.
**Depends on:** collision — "`ConvexColliderComponent.BroadPhaseAABB`
must be refreshed when vertices change".

## Open questions

- **Schema version enforcement** — `BlenderLevelData.Version` is
  deserialized but never read. The parser tolerates schema drift
  silently. A version-check premise should be added once the parser
  starts validating.
- **`Content/blender_level.json` path is hardcoded** — the parser
  reads from `<Content>/blender_level.json` regardless of what level
  identifier was requested. Multi-level Blender projects don't work
  yet; the dispatch by `Blender_` prefix is one-name-to-one-file.
  Whether to use the identifier as the filename, or to ship a
  manifest, is open.
- **Camera-from-Blender semantics** — `ProcessCamera` reads
  `ortho_scale` from custom properties and converts to engine zoom.
  Whether the camera position from Blender should be a one-shot
  initialization or a per-frame override is unsettled.

## Aspirational direction

- Move this parser onto the `CurrentLevelComponent` hook so it shares
  the engine-wide component-driven pattern. The `Blender_` prefix
  hack would then be replaced by a format field in the level data,
  resolved inside `LevelLoadRequestSystem`.
- Multi-file support so a single Blender project can export multiple
  level JSONs and the parser routes by level identifier.
- Schema versioning enforcement so old JSON files fail loudly against
  newer parsers (and vice versa).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `Blender_` prefix is the parser's opt-in hook (indirectly
  exercised by `BlenderLevelTests.BlenderLevelLoadsSuccessfully`)
- Parser is message-driven, unlike the LDtk parsers
- Parser depends on the JSON schema produced by the exporter plugin
- The exporter plugin is part of the module
- Collider-child convention: `-collider` suffix attaches a
  ConvexCollider to the parent
