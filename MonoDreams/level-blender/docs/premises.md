# level-blender — premises

> Technical invariants the engine assumes about the Blender level-loader
> block: `BlenderLevelParserSystem`, the `BlenderLevelData` /
> `BlenderObject` JSON schema, and the
> `Tools/blender_level_export.py` exporter plugin. Read this before
> changing the parser, the JSON schema, or the exporter plugin.

## `Blender_` prefix is the parser's opt-in hook

`BlenderLevelParserSystem` subscribes to `LoadLevelRequest` and
processes the message only if `request.LevelIdentifier` starts with
the string `Blender_`. Any other identifier is ignored by this block.
That prefix is also what causes `LevelLoadRequestSystem` (in
`level-loading`) to fail the LDtk load path and remove
`CurrentLevelComponent`, leaving this block as the sole effective
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

## The exporter plugin is part of the block

`Tools/blender_level_export.py` ships in this block at
`MonoDreams/level-blender/Tools/blender_level_export.py`. It is the
Blender-side companion to `BlenderLevelParserSystem`. Updates to the
parser's expected JSON schema *must* be accompanied by matching updates
to the exporter, and vice versa — they are two halves of one contract.
The plugin's `bl_info.version` and the JSON `version` field are the
intended version signal.

**Why:** the plugin and parser are versioned together; a level
exported with plugin v1.7 may not parse correctly under a parser
expecting v1.8 schema fields. Treating the plugin as in-block (not as
external tooling) is what makes "update both at once" feel natural.
**Breaks:** a parser change that adds a required field without
updating the plugin produces JSON files missing that field; the parser
either deserializes to default or throws on `null`. A plugin change
that adds a new field without updating the parser produces JSON the
parser ignores — the Blender artist's intent is silently discarded.
**Tests:** none yet (the integration test uses a single fixed
`blender_level.json`; nothing exercises the schema-evolution path).
**Depends on:** —

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
- The exporter plugin is part of the block
- Collider-child convention: `-collider` suffix attaches a
  ConvexCollider to the parent
